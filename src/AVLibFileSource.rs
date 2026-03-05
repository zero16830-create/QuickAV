#![allow(non_snake_case)]

use crate::AVLibPacket::AVLibPacket;
use crate::AVLibPacketRecycler::AVLibPacketRecycler;
use crate::AVLibUtil::{
    BestAudioStreamIndex, BestVideoStreamIndex, MicrosecondsToSeconds, SecondsToMicroseconds,
    FFMPEG_OPEN_LOCK,
};
use crate::FixedSizeQueue::FixedSizeQueue;
use crate::IAVLibSource::{AVLibStreamInfo, IAVLibSource};
use ffmpeg_next::ffi::AVMediaType;
use ffmpeg_next::media::Type;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Condvar, Mutex,
};
use std::thread;
use std::time::Duration;

pub struct AVLibFileSource {
    pub _uri: String,
    pub _duration: f64,

    pub _packetQueues: Vec<Arc<FixedSizeQueue<AVLibPacket>>>,
    pub _mapping: Vec<i32>,

    _streamTypes: Vec<AVMediaType>,
    _streams: Vec<AVLibStreamInfo>,
    _timeBases: Vec<f64>,
    _frameRates: Vec<f64>,
    _activeQueues: Vec<bool>,
    _queueThresholds: Vec<usize>,
    _seekStreamIndex: i32,
    _seekTimeBase: f64,

    _isConnected: Arc<AtomicBool>,
    pub _stayAlive: Arc<AtomicBool>,
    _seekRequest: Arc<Mutex<Option<(f64, f64)>>>,
    _continueMutex: Arc<Mutex<()>>,
    _continueCondition: Arc<Condvar>,
    _recycler: Arc<Mutex<AVLibPacketRecycler>>,
    _thread: Option<thread::JoinHandle<()>>,
}

impl AVLibFileSource {
    const DEFAULT_VIDEO_PACKET_QUEUE_SIZE: usize = 50;
    const DEFAULT_AUDIO_PACKET_QUEUE_SIZE: usize = 100;
    const SEEK_THRESHOLD: f64 = 0.5;

    pub fn new(uri: String) -> Self {
        let _ = ffmpeg_next::init();

        let mut duration = -1.0;
        let mut queues = Vec::new();
        let mut mapping = vec![-1i32; 256];
        let mut stream_types = Vec::new();
        let mut streams = Vec::new();
        let mut time_bases = Vec::new();
        let mut frame_rates = Vec::new();
        let mut active = Vec::new();
        let mut queue_thresholds = Vec::new();
        let mut seek_stream_index = 0i32;
        let mut seek_time_base = 0.0f64;

        if let Ok(ictx) = {
            let _l = FFMPEG_OPEN_LOCK.lock().unwrap();
            ffmpeg_next::format::input(&uri)
        } {
            duration = MicrosecondsToSeconds(ictx.duration());
            let best_video_stream = BestVideoStreamIndex(&ictx);
            let best_audio_stream = BestAudioStreamIndex(&ictx);
            let mut count = 0;

            for preferred_index in [best_video_stream, best_audio_stream] {
                if preferred_index < 0 {
                    continue;
                }

                let s = match ictx.stream(preferred_index as usize) {
                    Some(stream) => stream,
                    None => continue,
                };

                let medium = s.parameters().medium();
                let media_type = match medium {
                    Type::Video => AVMediaType::AVMEDIA_TYPE_VIDEO,
                    Type::Audio => AVMediaType::AVMEDIA_TYPE_AUDIO,
                    _ => AVMediaType::AVMEDIA_TYPE_UNKNOWN,
                };
                let (width, height) = if media_type == AVMediaType::AVMEDIA_TYPE_VIDEO {
                    if let Ok(ctx) =
                        ffmpeg_next::codec::context::Context::from_parameters(s.parameters())
                    {
                        if let Ok(video) = ctx.decoder().video() {
                            (video.width() as i32, video.height() as i32)
                        } else {
                            (0, 0)
                        }
                    } else {
                        (0, 0)
                    }
                } else {
                    (0, 0)
                };

                if (media_type == AVMediaType::AVMEDIA_TYPE_VIDEO
                    || media_type == AVMediaType::AVMEDIA_TYPE_AUDIO)
                    && (s.index() >= mapping.len() || mapping[s.index()] < 0)
                {
                    if s.index() >= mapping.len() {
                        mapping.resize(s.index() + 1, -1);
                    }

                    mapping[s.index()] = count as i32;
                    stream_types.push(media_type);
                    streams.push(AVLibStreamInfo {
                        index: s.index() as i32,
                        codec_type: media_type,
                        width,
                        height,
                    });
                    time_bases.push(f64::from(s.time_base()));
                    let fr = s.rate();
                    let den = fr.denominator();
                    let fps = if den == 0 {
                        0.0
                    } else {
                        fr.numerator() as f64 / den as f64
                    };
                    frame_rates.push(fps);

                    if media_type == AVMediaType::AVMEDIA_TYPE_VIDEO {
                        seek_stream_index = preferred_index;
                        seek_time_base = f64::from(s.time_base());
                        queues.push(Arc::new(FixedSizeQueue::new(
                            Self::DEFAULT_VIDEO_PACKET_QUEUE_SIZE,
                        )));
                        active.push(true);
                        queue_thresholds.push(Self::DEFAULT_VIDEO_PACKET_QUEUE_SIZE / 2);
                    } else {
                        queues.push(Arc::new(FixedSizeQueue::new(
                            Self::DEFAULT_AUDIO_PACKET_QUEUE_SIZE,
                        )));
                        active.push(false);
                        queue_thresholds.push(Self::DEFAULT_AUDIO_PACKET_QUEUE_SIZE / 2);
                    }

                    count += 1;
                }
            }
        }

        let stay_alive = Arc::new(AtomicBool::new(true));
        let is_connected = Arc::new(AtomicBool::new(true));
        let seek_request = Arc::new(Mutex::new(None));
        let continue_mutex = Arc::new(Mutex::new(()));
        let continue_condition = Arc::new(Condvar::new());
        let recycler = Arc::new(Mutex::new(AVLibPacketRecycler::new(250)));

        let mut source = Self {
            _uri: uri.clone(),
            _duration: duration,
            _packetQueues: queues,
            _mapping: mapping,
            _streamTypes: stream_types,
            _streams: streams,
            _timeBases: time_bases,
            _frameRates: frame_rates,
            _activeQueues: active,
            _queueThresholds: queue_thresholds,
            _seekStreamIndex: seek_stream_index,
            _seekTimeBase: seek_time_base,
            _isConnected: is_connected.clone(),
            _stayAlive: stay_alive.clone(),
            _seekRequest: seek_request.clone(),
            _continueMutex: continue_mutex.clone(),
            _continueCondition: continue_condition.clone(),
            _recycler: recycler.clone(),
            _thread: None,
        };

        let t_uri = source._uri.clone();
        let t_mapping = source._mapping.clone();
        let t_queues = source._packetQueues.clone();
        let t_stay_alive = stay_alive.clone();
        let t_seek_request = seek_request.clone();
        let t_is_connected = is_connected.clone();
        let t_time_bases = source._timeBases.clone();
        let t_active = source._activeQueues.clone();
        let t_duration = source._duration;
        let t_seek_stream_index = source._seekStreamIndex;
        let t_seek_time_base = source._seekTimeBase;
        let t_continue_mutex = continue_mutex.clone();
        let t_continue_condition = continue_condition.clone();
        let t_recycler = recycler.clone();

        source._thread = Some(thread::spawn(move || {
            let mut seek_to = 0.0f64;
            let mut opened = loop {
                if !t_stay_alive.load(Ordering::SeqCst) {
                    t_is_connected.store(false, Ordering::SeqCst);
                    return;
                }

                match {
                    let _l = FFMPEG_OPEN_LOCK.lock().unwrap();
                    ffmpeg_next::format::input(&t_uri)
                } {
                    Ok(ctx) => {
                        t_is_connected.store(true, Ordering::SeqCst);
                        break ctx;
                    }
                    Err(_) => {
                        t_is_connected.store(false, Ordering::SeqCst);
                        let guard = t_continue_mutex
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner());
                        let _ =
                            t_continue_condition.wait_timeout(guard, Duration::from_millis(250));
                    }
                }
            };

            let mut eof_reached = false;

            while t_stay_alive.load(Ordering::SeqCst) {
                if let Some((from, to)) = t_seek_request
                    .lock()
                    .unwrap_or_else(|poisoned| poisoned.into_inner())
                    .take()
                {
                    let mut target_to = to;
                    let mut target_from = from;

                    if target_to > t_duration {
                        target_to = t_duration;
                    } else if target_to < 0.0 {
                        target_to = 0.0;
                    }

                    if target_from > t_duration {
                        target_from = t_duration;
                    } else if target_from < 0.0 {
                        target_from = 0.0;
                    }

                    if (target_to - target_from).abs() > Self::SEEK_THRESHOLD {
                        let flags = if target_to >= target_from {
                            ffmpeg_next::ffi::AVSEEK_FLAG_ANY
                        } else {
                            ffmpeg_next::ffi::AVSEEK_FLAG_BACKWARD
                        };

                        let seek_ts = if t_seek_time_base > 0.0 {
                            (target_to / t_seek_time_base) as i64
                        } else {
                            SecondsToMicroseconds(target_to)
                        };

                        let seek_result = unsafe {
                            ffmpeg_next::ffi::av_seek_frame(
                                opened.as_mut_ptr(),
                                t_seek_stream_index,
                                seek_ts,
                                flags,
                            )
                        };

                        if seek_result >= 0 {
                            eof_reached = false;
                            seek_to = target_to;

                            for (i, q) in t_queues.iter().enumerate() {
                                if i < t_active.len() && t_active[i] {
                                    q.Flush();
                                }
                            }

                            for (i, q) in t_queues.iter().enumerate() {
                                if i < t_active.len() && t_active[i] {
                                    let mut seek_packet = t_recycler
                                        .lock()
                                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                                        .GetPacket();
                                    seek_packet.SetSeekRequest(target_to);
                                    q.Push(seek_packet);
                                }
                            }
                        }
                    }
                }

                if eof_reached {
                    let guard = t_continue_mutex
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let _ = t_continue_condition.wait_timeout(guard, Duration::from_millis(25));
                    continue;
                }

                let any_queue_full = t_queues
                    .iter()
                    .enumerate()
                    .any(|(i, q)| i < t_active.len() && t_active[i] && q.Full());
                if any_queue_full {
                    let guard = t_continue_mutex
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let _ = t_continue_condition.wait_timeout(guard, Duration::from_millis(5));
                    continue;
                }

                let next_packet = {
                    let mut packets = opened.packets();
                    packets.next()
                };

                let Some((stream, packet)) = next_packet else {
                    eof_reached = true;
                    for (i, q) in t_queues.iter().enumerate() {
                        if i < t_active.len() && t_active[i] {
                            let mut eof_packet = t_recycler
                                .lock()
                                .unwrap_or_else(|poisoned| poisoned.into_inner())
                                .GetPacket();
                            eof_packet.SetAsEOF();
                            q.Push(eof_packet);
                        }
                    }
                    continue;
                };

                let mut wrapped = t_recycler
                    .lock()
                    .unwrap_or_else(|poisoned| poisoned.into_inner())
                    .GetPacket();
                wrapped.Packet = packet;

                if stream.index() >= t_mapping.len() {
                    t_recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .Recycle(wrapped);
                    continue;
                }

                let internal = t_mapping[stream.index()];
                if internal < 0 {
                    t_recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .Recycle(wrapped);
                    continue;
                }

                let i = internal as usize;
                if i >= t_queues.len() || i >= t_active.len() {
                    t_recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .Recycle(wrapped);
                    continue;
                }

                if !t_active[i] {
                    t_recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .Recycle(wrapped);
                    continue;
                }

                if seek_to > 0.0 {
                    let pts = wrapped.Packet.pts().unwrap_or(0) as f64
                        * if i < t_time_bases.len() {
                            t_time_bases[i]
                        } else {
                            0.0
                        };
                    if pts < seek_to {
                        t_recycler
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner())
                            .Recycle(wrapped);
                        continue;
                    }
                    seek_to = 0.0;
                }

                t_queues[i].Push(wrapped);
            }

            t_is_connected.store(false, Ordering::SeqCst);
        }));

        source
    }

    pub fn GetStreamInternalIndex(&self, t: Type) -> i32 {
        let media_type = match t {
            Type::Video => AVMediaType::AVMEDIA_TYPE_VIDEO,
            Type::Audio => AVMediaType::AVMEDIA_TYPE_AUDIO,
            _ => AVMediaType::AVMEDIA_TYPE_UNKNOWN,
        };

        for (i, st) in self._streamTypes.iter().enumerate() {
            if *st == media_type {
                return i as i32;
            }
        }

        -1
    }

    pub fn TryGetNext(&self, i: i32) -> Option<AVLibPacket> {
        if i < 0 || i >= self._packetQueues.len() as i32 {
            None
        } else {
            let idx = i as usize;
            if idx < self._queueThresholds.len()
                && self._packetQueues[idx].Count() <= self._queueThresholds[idx]
            {
                self._continueCondition.notify_one();
            }
            let popped = self._packetQueues[i as usize].TryPop();
            if popped.is_some() {
                self._continueCondition.notify_one();
            }
            popped
        }
    }
    pub fn VideoDecoder(&self, streamIndex: i32) -> Option<ffmpeg_next::decoder::Video> {
        let stream_info = self.Stream(streamIndex);
        if stream_info.index < 0 {
            return None;
        }

        let opened = {
            let _l = FFMPEG_OPEN_LOCK.lock().unwrap();
            ffmpeg_next::format::input(&self._uri).ok()?
        };

        let stream = opened.stream(stream_info.index as usize)?;
        let ctx =
            ffmpeg_next::codec::context::Context::from_parameters(stream.parameters()).ok()?;
        ctx.decoder().video().ok()
    }
}

impl IAVLibSource for AVLibFileSource {
    fn Connect(&mut self) {
        // File source connects in constructor.
    }

    fn IsConnected(&self) -> bool {
        true
    }

    fn Duration(&self) -> f64 {
        self._duration
    }

    fn StreamCount(&self) -> i32 {
        self._streams.len() as i32
    }

    fn StreamType(&self, streamIndex: i32) -> AVMediaType {
        self._streamTypes[streamIndex as usize]
    }

    fn Stream(&self, streamIndex: i32) -> AVLibStreamInfo {
        self._streams[streamIndex as usize]
    }

    fn TimeBase(&self, streamIndex: i32) -> f64 {
        self._timeBases[streamIndex as usize]
    }

    fn FrameRate(&self, streamIndex: i32) -> f64 {
        self._frameRates[streamIndex as usize]
    }

    fn FrameDuration(&self, streamIndex: i32) -> f64 {
        1.0 / self.FrameRate(streamIndex)
    }

    fn IsRealtime(&self) -> bool {
        false
    }

    fn CanSeek(&self) -> bool {
        true
    }

    fn Seek(&mut self, from: f64, to: f64) {
        *self._seekRequest.lock().unwrap() = Some((from, to));
        self._continueCondition.notify_all();
    }

    fn TryGetNext(&mut self, streamIndex: i32) -> Option<AVLibPacket> {
        if streamIndex < 0 || streamIndex as usize >= self._packetQueues.len() {
            return None;
        }

        let idx = streamIndex as usize;
        if idx < self._queueThresholds.len()
            && self._packetQueues[idx].Count() <= self._queueThresholds[idx]
        {
            self._continueCondition.notify_one();
        }

        let popped = self._packetQueues[idx].TryPop();
        if popped.is_some() {
            self._continueCondition.notify_one();
        }
        popped
    }

    fn Recycle(&mut self, packet: AVLibPacket) {
        self._recycler.lock().unwrap().Recycle(packet);
    }
}

impl Drop for AVLibFileSource {
    fn drop(&mut self) {
        self._stayAlive.store(false, Ordering::SeqCst);
        self._continueCondition.notify_all();
        if let Some(t) = self._thread.take() {
            let _ = t.join();
        }
    }
}
