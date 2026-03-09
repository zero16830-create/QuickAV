use crate::av_lib_packet::AvLibPacket;
use crate::av_lib_packet_recycler::AvLibPacketRecycler;
use crate::av_lib_source::{
    AvLibSource, AvLibSourceConnectionState, AvLibSourceRuntimeStats, AvLibStreamInfo,
};
use crate::av_lib_util::{
    best_audio_stream_index, best_video_stream_index, microseconds_to_seconds,
    seconds_to_microseconds, FFMPEG_OPEN_LOCK,
};
use crate::fixed_size_queue::FixedSizeQueue;
use ffmpeg_next::ffi::AVMediaType;
use ffmpeg_next::media::Type;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Condvar, Mutex,
};
use std::thread;
use std::time::Duration;

pub struct AvLibFileSource {
    pub uri: String,
    pub duration: f64,

    pub packet_queues: Vec<Arc<FixedSizeQueue<AvLibPacket>>>,
    pub mapping: Vec<i32>,

    stream_types: Vec<AVMediaType>,
    streams: Vec<AvLibStreamInfo>,
    time_bases: Vec<f64>,
    frame_rates: Vec<f64>,
    active_queues: Vec<bool>,
    queue_thresholds: Vec<usize>,
    seek_stream_index: i32,
    seek_time_base: f64,

    pub stay_alive: Arc<AtomicBool>,
    seek_request: Arc<Mutex<Option<(f64, f64)>>>,
    continue_condition: Arc<Condvar>,
    recycler: Arc<Mutex<AvLibPacketRecycler>>,
    thread: Option<thread::JoinHandle<()>>,
}

impl AvLibFileSource {
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
            duration = microseconds_to_seconds(ictx.duration());
            let best_video_stream = best_video_stream_index(&ictx);
            let best_audio_stream = best_audio_stream_index(&ictx);
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
                let (width, height, sample_rate, channels) =
                    if media_type == AVMediaType::AVMEDIA_TYPE_VIDEO {
                        if let Ok(ctx) =
                            ffmpeg_next::codec::context::Context::from_parameters(s.parameters())
                        {
                            if let Ok(video) = ctx.decoder().video() {
                                (video.width() as i32, video.height() as i32, 0, 0)
                            } else {
                                (0, 0, 0, 0)
                            }
                        } else {
                            (0, 0, 0, 0)
                        }
                    } else if media_type == AVMediaType::AVMEDIA_TYPE_AUDIO {
                        if let Ok(ctx) =
                            ffmpeg_next::codec::context::Context::from_parameters(s.parameters())
                        {
                            if let Ok(audio) = ctx.decoder().audio() {
                                (0, 0, audio.rate() as i32, audio.channels() as i32)
                            } else {
                                (0, 0, 0, 0)
                            }
                        } else {
                            (0, 0, 0, 0)
                        }
                    } else {
                        (0, 0, 0, 0)
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
                    streams.push(AvLibStreamInfo {
                        index: s.index() as i32,
                        codec_type: media_type,
                        width,
                        height,
                        sample_rate,
                        channels,
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
                        active.push(true);
                        queue_thresholds.push(Self::DEFAULT_AUDIO_PACKET_QUEUE_SIZE / 2);
                    }

                    count += 1;
                }
            }
        }

        let stay_alive = Arc::new(AtomicBool::new(true));
        let seek_request = Arc::new(Mutex::new(None));
        let continue_mutex = Arc::new(Mutex::new(()));
        let continue_condition = Arc::new(Condvar::new());
        let recycler = Arc::new(Mutex::new(AvLibPacketRecycler::new(250)));

        let mut source = Self {
            uri: uri.clone(),
            duration: duration,
            packet_queues: queues,
            mapping,
            stream_types: stream_types,
            streams: streams,
            time_bases: time_bases,
            frame_rates: frame_rates,
            active_queues: active,
            queue_thresholds: queue_thresholds,
            seek_stream_index: seek_stream_index,
            seek_time_base: seek_time_base,
            stay_alive: stay_alive.clone(),
            seek_request: seek_request.clone(),
            continue_condition: continue_condition.clone(),
            recycler: recycler.clone(),
            thread: None,
        };

        let t_uri = source.uri.clone();
        let t_mapping = source.mapping.clone();
        let t_queues = source.packet_queues.clone();
        let t_stay_alive = stay_alive.clone();
        let t_seek_request = seek_request.clone();
        let t_time_bases = source.time_bases.clone();
        let t_active = source.active_queues.clone();
        let t_duration = source.duration;
        let t_seek_stream_index = source.seek_stream_index;
        let t_seek_time_base = source.seek_time_base;
        let t_continue_mutex = continue_mutex.clone();
        let t_continue_condition = continue_condition.clone();
        let t_recycler = recycler.clone();

        source.thread = Some(thread::spawn(move || {
            let mut seek_to = 0.0f64;
            let mut opened = loop {
                if !t_stay_alive.load(Ordering::SeqCst) {
                    return;
                }

                match {
                    let _l = FFMPEG_OPEN_LOCK.lock().unwrap();
                    ffmpeg_next::format::input(&t_uri)
                } {
                    Ok(ctx) => break ctx,
                    Err(_) => {
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
                            seconds_to_microseconds(target_to)
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
                                    q.flush();
                                }
                            }

                            for (i, q) in t_queues.iter().enumerate() {
                                if i < t_active.len() && t_active[i] {
                                    let mut seek_packet = t_recycler
                                        .lock()
                                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                                        .get_packet();
                                    seek_packet.set_seek_request(target_to);
                                    q.push(seek_packet);
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
                    .any(|(i, q)| i < t_active.len() && t_active[i] && q.is_full());
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
                                .get_packet();
                            eof_packet.set_eof();
                            q.push(eof_packet);
                        }
                    }
                    continue;
                };

                let mut wrapped = t_recycler
                    .lock()
                    .unwrap_or_else(|poisoned| poisoned.into_inner())
                    .get_packet();
                wrapped.packet = packet;

                if stream.index() >= t_mapping.len() {
                    t_recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .recycle(wrapped);
                    continue;
                }

                let internal = t_mapping[stream.index()];
                if internal < 0 {
                    t_recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .recycle(wrapped);
                    continue;
                }

                let i = internal as usize;
                if i >= t_queues.len() || i >= t_active.len() {
                    t_recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .recycle(wrapped);
                    continue;
                }

                if !t_active[i] {
                    t_recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .recycle(wrapped);
                    continue;
                }

                if seek_to > 0.0 {
                    let pts = wrapped.packet.pts().unwrap_or(0) as f64
                        * if i < t_time_bases.len() {
                            t_time_bases[i]
                        } else {
                            0.0
                        };
                    if pts < seek_to {
                        t_recycler
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner())
                            .recycle(wrapped);
                        continue;
                    }
                    seek_to = 0.0;
                }

                t_queues[i].push(wrapped);
            }
        }));

        source
    }

    pub fn get_stream_internal_index(&self, t: Type) -> i32 {
        let media_type = match t {
            Type::Video => AVMediaType::AVMEDIA_TYPE_VIDEO,
            Type::Audio => AVMediaType::AVMEDIA_TYPE_AUDIO,
            _ => AVMediaType::AVMEDIA_TYPE_UNKNOWN,
        };

        for (i, st) in self.stream_types.iter().enumerate() {
            if *st == media_type {
                return i as i32;
            }
        }

        -1
    }

    pub fn try_get_next(&self, stream_index: i32) -> Option<AvLibPacket> {
        if stream_index < 0 || stream_index >= self.packet_queues.len() as i32 {
            None
        } else {
            let idx = stream_index as usize;
            if idx < self.queue_thresholds.len()
                && self.packet_queues[idx].len() <= self.queue_thresholds[idx]
            {
                self.continue_condition.notify_one();
            }
            let popped = self.packet_queues[stream_index as usize].try_pop();
            if popped.is_some() {
                self.continue_condition.notify_one();
            }
            popped
        }
    }
    pub fn video_decoder(&self, stream_index: i32) -> Option<ffmpeg_next::decoder::Video> {
        let stream_info = self.stream(stream_index);
        if stream_info.index < 0 {
            return None;
        }

        let opened = {
            let _l = FFMPEG_OPEN_LOCK.lock().unwrap();
            ffmpeg_next::format::input(&self.uri).ok()?
        };

        let stream = opened.stream(stream_info.index as usize)?;
        let ctx =
            ffmpeg_next::codec::context::Context::from_parameters(stream.parameters()).ok()?;
        ctx.decoder().video().ok()
    }

    pub fn audio_decoder(&self, stream_index: i32) -> Option<ffmpeg_next::decoder::Audio> {
        let stream_info = self.stream(stream_index);
        if stream_info.index < 0 {
            return None;
        }

        let opened = {
            let _l = FFMPEG_OPEN_LOCK.lock().unwrap();
            ffmpeg_next::format::input(&self.uri).ok()?
        };

        let stream = opened.stream(stream_info.index as usize)?;
        let ctx =
            ffmpeg_next::codec::context::Context::from_parameters(stream.parameters()).ok()?;
        ctx.decoder().audio().ok()
    }
}

impl AvLibSource for AvLibFileSource {
    fn connect(&mut self) {
        // File source connects in constructor.
    }

    fn connection_state(&self) -> AvLibSourceConnectionState {
        AvLibSourceConnectionState::Connected
    }

    fn duration(&self) -> f64 {
        self.duration
    }

    fn stream_count(&self) -> i32 {
        self.streams.len() as i32
    }

    fn stream_type(&self, stream_index: i32) -> AVMediaType {
        self.stream_types[stream_index as usize]
    }

    fn stream(&self, stream_index: i32) -> AvLibStreamInfo {
        self.streams[stream_index as usize]
    }

    fn time_base(&self, stream_index: i32) -> f64 {
        self.time_bases[stream_index as usize]
    }

    fn frame_rate(&self, stream_index: i32) -> f64 {
        self.frame_rates[stream_index as usize]
    }

    fn frame_duration(&self, stream_index: i32) -> f64 {
        1.0 / self.frame_rate(stream_index)
    }

    fn is_realtime(&self) -> bool {
        false
    }

    fn can_seek(&self) -> bool {
        true
    }

    fn seek(&mut self, from: f64, to: f64) {
        *self.seek_request.lock().unwrap() = Some((from, to));
        self.continue_condition.notify_all();
    }

    fn try_get_next(&mut self, stream_index: i32) -> Option<AvLibPacket> {
        if stream_index < 0 || stream_index as usize >= self.packet_queues.len() {
            return None;
        }

        let idx = stream_index as usize;
        if idx < self.queue_thresholds.len()
            && self.packet_queues[idx].len() <= self.queue_thresholds[idx]
        {
            self.continue_condition.notify_one();
        }

        let popped = self.packet_queues[idx].try_pop();
        if popped.is_some() {
            self.continue_condition.notify_one();
        }
        popped
    }

    fn recycle(&mut self, packet: AvLibPacket) {
        self.recycler.lock().unwrap().recycle(packet);
    }

    fn create_video_decoder(&self, stream_index: i32) -> Option<ffmpeg_next::decoder::Video> {
        self.video_decoder(stream_index)
    }

    fn create_audio_decoder(&self, stream_index: i32) -> Option<ffmpeg_next::decoder::Audio> {
        self.audio_decoder(stream_index)
    }

    fn runtime_stats(&self) -> AvLibSourceRuntimeStats {
        let mut stats = AvLibSourceRuntimeStats::empty();
        stats.connection_state = self.connection_state();
        stats
    }
}

impl Drop for AvLibFileSource {
    fn drop(&mut self) {
        self.stay_alive.store(false, Ordering::SeqCst);
        self.continue_condition.notify_all();
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}
