#![allow(non_snake_case)]

use crate::AVLibPacket::AVLibPacket;
use crate::AVLibPacketRecycler::AVLibPacketRecycler;
use crate::AVLibUtil::FFMPEG_OPEN_LOCK;
use crate::FixedSizeQueue::FixedSizeQueue;
use crate::IAVLibSource::{AVLibStreamInfo, IAVLibSource};
use crate::Logging::Debug::Debug;
use ffmpeg_next::ffi::AVMediaType;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Mutex,
};
use std::thread;
use std::time::{Duration, Instant};

pub struct AVLibRTSPSource {
    _uri: String,
    _packetQueues: Arc<Mutex<Vec<Arc<FixedSizeQueue<AVLibPacket>>>>>,
    _streamTypes: Arc<Mutex<Vec<AVMediaType>>>,
    _streams: Arc<Mutex<Vec<AVLibStreamInfo>>>,
    _stayAlive: Arc<AtomicBool>,
    _thread: Option<thread::JoinHandle<()>>,
    _isConnected: Arc<AtomicBool>,
    _lastActivityTicks: Arc<Mutex<Option<Instant>>>,
    _checkingConnection: Arc<AtomicBool>,
    _checkStartTicks: Arc<Mutex<Option<Instant>>>,
    _packetCount: Arc<Mutex<u64>>,
    _connectRequested: Arc<AtomicBool>,
    _recycler: Arc<Mutex<AVLibPacketRecycler>>,
}

impl AVLibRTSPSource {
    const BEGIN_TIMEOUT_CHECK_SECONDS: u64 = 3;
    const TIMEOUT_SECONDS: u64 = 2;

    pub fn new(uri: String) -> Self {
        let _ = ffmpeg_next::init();

        let packet_queues = Arc::new(Mutex::new(vec![Arc::new(FixedSizeQueue::new(10))]));
        let stream_types = Arc::new(Mutex::new(vec![AVMediaType::AVMEDIA_TYPE_VIDEO]));
        let streams = Arc::new(Mutex::new(vec![AVLibStreamInfo {
            index: 0,
            codec_type: AVMediaType::AVMEDIA_TYPE_VIDEO,
            width: 1280,
            height: 800,
        }]));
        let stay_alive = Arc::new(AtomicBool::new(true));
        let is_connected = Arc::new(AtomicBool::new(false));
        let last_activity_ticks = Arc::new(Mutex::new(None));
        let checking_connection = Arc::new(AtomicBool::new(false));
        let check_start_ticks = Arc::new(Mutex::new(None));
        let packet_count = Arc::new(Mutex::new(0_u64));
        let connect_requested = Arc::new(AtomicBool::new(true));
        let recycler = Arc::new(Mutex::new(AVLibPacketRecycler::new(10)));

        let mut source = Self {
            _uri: uri.clone(),
            _packetQueues: packet_queues.clone(),
            _streamTypes: stream_types.clone(),
            _streams: streams.clone(),
            _stayAlive: stay_alive.clone(),
            _thread: None,
            _isConnected: is_connected.clone(),
            _lastActivityTicks: last_activity_ticks.clone(),
            _checkingConnection: checking_connection.clone(),
            _checkStartTicks: check_start_ticks.clone(),
            _packetCount: packet_count.clone(),
            _connectRequested: connect_requested.clone(),
            _recycler: recycler.clone(),
        };

        source._thread = Some(thread::spawn(move || {
            while stay_alive.load(Ordering::SeqCst) {
                if !connect_requested.swap(false, Ordering::SeqCst) {
                    thread::sleep(Duration::from_millis(50));
                    continue;
                }

                let stay_alive_for_interrupt = stay_alive.clone();
                let mut opened = match {
                    let _l = FFMPEG_OPEN_LOCK.lock().unwrap();
                    ffmpeg_next::format::input_with_interrupt(&uri, move || {
                        !stay_alive_for_interrupt.load(Ordering::SeqCst)
                    })
                } {
                    Ok(ctx) => ctx,
                    Err(_) => {
                        is_connected.store(false, Ordering::SeqCst);
                        thread::sleep(Duration::from_millis(100));
                        continue;
                    }
                };

                let best_video_stream = match opened.streams().best(ffmpeg_next::media::Type::Video)
                {
                    Some(s) => s,
                    None => {
                        is_connected.store(false, Ordering::SeqCst);
                        thread::sleep(Duration::from_millis(100));
                        continue;
                    }
                };

                let stream_index = best_video_stream.index() as i32;
                let (video_width, video_height) = {
                    if let Ok(ctx) = ffmpeg_next::codec::context::Context::from_parameters(
                        best_video_stream.parameters(),
                    ) {
                        if let Ok(video) = ctx.decoder().video() {
                            (video.width() as i32, video.height() as i32)
                        } else {
                            (0, 0)
                        }
                    } else {
                        (0, 0)
                    }
                };

                {
                    let mut streams_guard = streams.lock().unwrap();
                    if let Some(first) = streams_guard.get_mut(0) {
                        first.index = stream_index;
                        first.codec_type = AVMediaType::AVMEDIA_TYPE_VIDEO;
                        first.width = video_width;
                        first.height = video_height;
                    }
                }

                is_connected.store(true, Ordering::SeqCst);

                for (stream, packet) in opened.packets() {
                    if !stay_alive.load(Ordering::SeqCst) {
                        break;
                    }

                    if stream.index() as i32 != stream_index {
                        continue;
                    }

                    let queue = {
                        let queues_guard = packet_queues.lock().unwrap();
                        queues_guard.get(0).cloned()
                    };

                    if let Some(queue) = queue {
                        queue.Push(AVLibPacket {
                            Packet: packet,
                            _isEOF: false,
                            _isSeekRequest: false,
                            _seekRequestTime: 0.0,
                        });

                        if let Ok(mut count) = packet_count.lock() {
                            *count += 1;
                        }
                        if let Ok(mut last) = last_activity_ticks.lock() {
                            *last = Some(Instant::now());
                        }
                        checking_connection.store(false, Ordering::SeqCst);
                    }
                }

                is_connected.store(false, Ordering::SeqCst);
                thread::sleep(Duration::from_millis(100));
            }
        }));

        source
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

impl IAVLibSource for AVLibRTSPSource {
    fn Connect(&mut self) {
        if !self._isConnected.load(Ordering::SeqCst) {
            self._connectRequested.store(true, Ordering::SeqCst);
        }
    }

    fn IsConnected(&self) -> bool {
        self._isConnected.load(Ordering::SeqCst)
    }

    fn Duration(&self) -> f64 {
        Debug::LogWarning("AVLibRTSPSource::Duration - realtime source has no known duration");
        -1.0
    }

    fn StreamCount(&self) -> i32 {
        self._streams.lock().unwrap().len() as i32
    }

    fn StreamType(&self, streamIndex: i32) -> AVMediaType {
        if streamIndex < 0 {
            Debug::LogError("AVLibRTSPSource::StreamType - streamIndex was out of range");
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        if !self.IsConnected() {
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        let types = self._streamTypes.lock().unwrap();
        let idx = streamIndex as usize;
        if idx >= types.len() {
            Debug::LogError("AVLibRTSPSource::StreamType - streamIndex was out of range");
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        types[idx]
    }

    fn Stream(&self, streamIndex: i32) -> AVLibStreamInfo {
        if streamIndex < 0 {
            Debug::LogError("AVLibRTSPSource::Stream - streamIndex was out of range");
            return AVLibStreamInfo::empty();
        }

        if !self.IsConnected() {
            return AVLibStreamInfo::empty();
        }

        let streams = self._streams.lock().unwrap();
        let idx = streamIndex as usize;
        if idx >= streams.len() {
            Debug::LogError("AVLibRTSPSource::Stream - streamIndex was out of range");
            return AVLibStreamInfo::empty();
        }

        streams[idx]
    }

    fn TimeBase(&self, streamIndex: i32) -> f64 {
        let _ = streamIndex;
        Debug::LogWarning("AVLibRTSPSource::TimeBase - realtime source has no known timebase");
        -1.0
    }

    fn FrameRate(&self, streamIndex: i32) -> f64 {
        let _ = streamIndex;
        Debug::LogWarning("AVLibRTSPSource::FrameRate - realtime source has no known framerate");
        -1.0
    }

    fn FrameDuration(&self, streamIndex: i32) -> f64 {
        let _ = streamIndex;
        Debug::LogWarning("AVLibRTSPSource::FrameDuration - realtime source has no frame duration");
        -1.0
    }

    fn IsRealtime(&self) -> bool {
        true
    }

    fn CanSeek(&self) -> bool {
        false
    }

    fn Seek(&mut self, _from: f64, _to: f64) {
        Debug::LogWarning("AVLibRTSPSource::Seek - realtime source cannot seek");
    }

    fn TryGetNext(&mut self, streamIndex: i32) -> Option<AVLibPacket> {
        if streamIndex < 0 {
            Debug::LogError("AVLibRTSPSource::TryGetNext - streamIndex was out of range");
            return None;
        }

        if !self.IsConnected() {
            return None;
        }

        let queue = {
            let queues = self._packetQueues.lock().unwrap();
            let idx = streamIndex as usize;
            if idx >= queues.len() {
                Debug::LogError("AVLibRTSPSource::TryGetNext - streamIndex was out of range");
                return None;
            }
            queues.get(idx).cloned()
        };

        let packet = queue.and_then(|q| q.TryPop());
        if packet.is_none() {
            let count = self._packetCount.lock().map(|c| *c).unwrap_or(0);
            if count > 0 {
                if !self._checkingConnection.load(Ordering::SeqCst) {
                    let last = self._lastActivityTicks.lock().ok().and_then(|v| *v);
                    if let Some(last_time) = last {
                        if last_time.elapsed().as_secs() > Self::BEGIN_TIMEOUT_CHECK_SECONDS {
                            self._checkingConnection.store(true, Ordering::SeqCst);
                            if let Ok(mut check_start) = self._checkStartTicks.lock() {
                                *check_start = Some(Instant::now());
                            }
                        }
                    }
                } else {
                    let started = self._checkStartTicks.lock().ok().and_then(|v| *v);
                    if let Some(start_time) = started {
                        if start_time.elapsed().as_secs() > Self::TIMEOUT_SECONDS {
                            self._isConnected.store(false, Ordering::SeqCst);
                            self._checkingConnection.store(false, Ordering::SeqCst);
                        }
                    }
                }
            }
        } else {
            self._isConnected.store(true, Ordering::SeqCst);
            self._checkingConnection.store(false, Ordering::SeqCst);
            if let Ok(mut last) = self._lastActivityTicks.lock() {
                *last = Some(Instant::now());
            }
        }

        packet
    }

    fn Recycle(&mut self, packet: AVLibPacket) {
        self._recycler.lock().unwrap().Recycle(packet);
    }
}

impl Drop for AVLibRTSPSource {
    fn drop(&mut self) {
        self._stayAlive.store(false, Ordering::SeqCst);
        if let Some(t) = self._thread.take() {
            let _ = t.join();
        }
    }
}
