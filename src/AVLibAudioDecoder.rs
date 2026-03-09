#![allow(non_snake_case)]

use crate::AudioFrame::AudioFrame;
use crate::FixedSizeQueue::FixedSizeQueue;
use crate::IAVLibSource::{AVLibStreamInfo, IAVLibSource};
use crate::Logging::Debug::Debug;
use ffmpeg_next::software::resampling::Context as SwrContext;
use ffmpeg_next::util::error::EAGAIN;
use ffmpeg_next::{
    format::{sample::Type as SampleType, Sample},
    ChannelLayout,
};
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Condvar, Mutex,
};
use std::thread;

enum DrainStatus {
    NeedMoreInput,
    ReachedEof,
    Failed,
}

pub struct AVLibAudioDecoder {
    _source: Arc<Mutex<Box<dyn IAVLibSource + Send>>>,
    _streamIndex: i32,
    _sourceStreamInfo: AVLibStreamInfo,
    pub _parsedFrames: Arc<FixedSizeQueue<AudioFrame>>,
    _readyFrames: Arc<FixedSizeQueue<AudioFrame>>,
    _isRealtime: bool,
    _resumeThreshold: usize,
    _stayAlive: Arc<AtomicBool>,
    _thread: Option<thread::JoinHandle<()>>,
    _pendingFrame: Arc<Mutex<Option<AudioFrame>>>,
    _seekRequest: Arc<AtomicBool>,
    _continueMutex: Arc<Mutex<()>>,
    _continueCondition: Arc<Condvar>,
}

impl AVLibAudioDecoder {
    const DEFAULT_AUDIO_FRAME_QUEUE_SIZE: usize = 96;
    const REALTIME_AUDIO_FRAME_QUEUE_SIZE: usize = 96;
    const REALTIME_RESUME_THRESHOLD: usize = 12;
    const DUE_EPSILON_SEC: f64 = 0.002;

    fn ResolveChannelLayout(frame: &ffmpeg_next::util::frame::Audio) -> ChannelLayout {
        if frame.channel_layout().is_empty() {
            ChannelLayout::default(frame.channels() as i32)
        } else {
            frame.channel_layout()
        }
    }

    fn DrainDecodedFrames(
        decoder: &mut ffmpeg_next::decoder::Audio,
        stream_idx: i32,
        tb: f64,
        parsed: &Arc<FixedSizeQueue<AudioFrame>>,
        ready: &Arc<FixedSizeQueue<AudioFrame>>,
        resampler: &mut Option<SwrContext>,
        resampler_signature: &mut Option<(Sample, ChannelLayout, u32)>,
        next_frame_time_sec: &mut Option<f64>,
        decode_count: &mut u64,
        missing_pts_count: &mut u64,
    ) -> DrainStatus {
        let target_format = Sample::F32(SampleType::Packed);
        let mut decoded = ffmpeg_next::util::frame::Audio::empty();
        loop {
            match decoder.receive_frame(&mut decoded) {
                Ok(()) => {
                    let input_format = decoded.format();
                    let input_layout = Self::ResolveChannelLayout(&decoded);
                    let input_rate = decoded.rate();

                    let needs_rebuild = match *resampler_signature {
                        Some((format, layout, rate)) => {
                            format != input_format || layout != input_layout || rate != input_rate
                        }
                        None => true,
                    };

                    if needs_rebuild {
                        match SwrContext::get(
                            input_format,
                            input_layout,
                            input_rate,
                            target_format,
                            input_layout,
                            input_rate,
                        ) {
                            Ok(new_resampler) => {
                                *resampler = Some(new_resampler);
                                *resampler_signature = Some((input_format, input_layout, input_rate));
                                Debug::Log(&format!(
                                    "[AVLibAudioDecoder] stream={} rebuild_resampler rate={} channels={} format={:?}",
                                    stream_idx,
                                    input_rate,
                                    input_layout.channels(),
                                    input_format
                                ));
                            }
                            Err(err) => {
                                Debug::LogWarning(&format!(
                                    "AVLibAudioDecoder::DecodeThread - rebuild_resampler failed: {}",
                                    err
                                ));
                                return DrainStatus::Failed;
                            }
                        }
                    }

                    let Some(resampler_ref) = resampler.as_mut() else {
                        return DrainStatus::Failed;
                    };

                    let mut converted = ffmpeg_next::util::frame::Audio::empty();
                    if let Err(err) = resampler_ref.run(&decoded, &mut converted) {
                        Debug::LogWarning(&format!(
                            "AVLibAudioDecoder::DecodeThread - resample failed: {}",
                            err
                        ));
                        return DrainStatus::Failed;
                    }

                    let channels = converted.channels() as i32;
                    let sample_rate = converted.rate() as i32;
                    let samples = converted.samples() as i32;
                    if channels <= 0 || sample_rate <= 0 || samples <= 0 {
                        continue;
                    }

                    let required_bytes = channels as usize
                        * samples as usize
                        * AudioFrame::BYTES_PER_SAMPLE;
                    let data = converted.data(0);
                    if data.len() < required_bytes {
                        Debug::LogWarning(
                            "AVLibAudioDecoder::DecodeThread - converted frame buffer was too small",
                        );
                        continue;
                    }

                    let time_sec = if let Some(ts) = decoded.timestamp() {
                        ts as f64 * tb
                    } else {
                        *missing_pts_count += 1;
                        next_frame_time_sec.unwrap_or(0.0)
                    };
                    let duration_sec = samples as f64 / sample_rate as f64;
                    *next_frame_time_sec = Some(time_sec + duration_sec);

                    let mut frame = ready
                        .TryPop()
                        .unwrap_or_else(|| AudioFrame::new(sample_rate, channels, samples));
                    frame.EnsureLayout(sample_rate, channels, samples);
                    frame.ClearEOF();
                    frame.SetTime(time_sec);
                    frame.SetDuration(duration_sec);
                    frame.BufferMut()[..required_bytes].copy_from_slice(&data[..required_bytes]);
                    parsed.Push(frame);

                    *decode_count += 1;
                    if *decode_count % 120 == 0 {
                        Debug::Log(&format!(
                            "[AVLibAudioDecoder] stream={} decoded={} queue_count={} missing_pts={}",
                            stream_idx,
                            *decode_count,
                            parsed.Count(),
                            *missing_pts_count
                        ));
                    }
                }
                Err(ffmpeg_next::Error::Eof) => return DrainStatus::ReachedEof,
                Err(ffmpeg_next::Error::Other { errno }) if errno == EAGAIN => {
                    return DrainStatus::NeedMoreInput;
                }
                Err(err) => {
                    Debug::LogWarning(&format!(
                        "AVLibAudioDecoder::DecodeThread - receive_frame failed: {}",
                        err
                    ));
                    return DrainStatus::Failed;
                }
            }
        }
    }

    pub fn new(
        source: Arc<Mutex<Box<dyn IAVLibSource + Send>>>,
        stream_idx: i32,
        mut decoder: ffmpeg_next::decoder::Audio,
        tb: f64,
    ) -> Self {
        let is_realtime = if let Ok(s) = source.lock() {
            s.IsRealtime()
        } else {
            false
        };
        let frame_queue_size = if is_realtime {
            Self::REALTIME_AUDIO_FRAME_QUEUE_SIZE
        } else {
            Self::DEFAULT_AUDIO_FRAME_QUEUE_SIZE
        };
        let resume_threshold = if is_realtime {
            Self::REALTIME_RESUME_THRESHOLD
        } else {
            frame_queue_size / 2
        };
        let parsed = Arc::new(FixedSizeQueue::new(frame_queue_size));
        let ready = Arc::new(FixedSizeQueue::new(frame_queue_size));
        let stay_alive = Arc::new(AtomicBool::new(true));
        let pending = Arc::new(Mutex::new(None));
        let seek_request = Arc::new(AtomicBool::new(false));
        let continue_mutex = Arc::new(Mutex::new(()));
        let continue_condition = Arc::new(Condvar::new());
        let source_stream_info = if let Ok(s) = source.lock() {
            s.Stream(stream_idx)
        } else {
            AVLibStreamInfo::empty()
        };

        let mut obj = Self {
            _source: source.clone(),
            _streamIndex: stream_idx,
            _sourceStreamInfo: source_stream_info,
            _parsedFrames: parsed.clone(),
            _readyFrames: ready.clone(),
            _isRealtime: is_realtime,
            _resumeThreshold: resume_threshold,
            _stayAlive: stay_alive.clone(),
            _thread: None,
            _pendingFrame: pending.clone(),
            _seekRequest: seek_request.clone(),
            _continueMutex: continue_mutex.clone(),
            _continueCondition: continue_condition.clone(),
        };

        let t_source = source.clone();
        let t_stay_alive = stay_alive.clone();
        let t_parsed = parsed.clone();
        let t_ready = ready.clone();
        let t_pending = pending.clone();
        let t_seek_request = seek_request.clone();
        let t_continue_mutex = continue_mutex.clone();
        let t_continue_condition = continue_condition.clone();

        obj._thread = Some(thread::spawn(move || {
            let mut resampler: Option<SwrContext> = None;
            let mut resampler_signature: Option<(Sample, ChannelLayout, u32)> = None;
            let mut next_frame_time_sec: Option<f64> = None;
            let mut decode_count: u64 = 0;
            let mut missing_pts_count: u64 = 0;

            let push_eof_frame = || {
                let mut eof = t_ready
                    .TryPop()
                    .unwrap_or_else(|| AudioFrame::new(0, 0, 0));
                eof.SetAsEOF();
                t_parsed.Push(eof);
            };

            while t_stay_alive.load(Ordering::SeqCst) {
                if t_parsed.Full() {
                    let guard = t_continue_mutex
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let _ = t_continue_condition
                        .wait_timeout(guard, std::time::Duration::from_millis(5));
                    continue;
                }

                let packet_opt = if let Ok(mut s) = t_source.lock() {
                    s.TryGetNext(stream_idx)
                } else {
                    None
                };

                if let Some(p) = packet_opt {
                    if p.IsEOF() {
                        let _ = decoder.send_eof();
                        if matches!(
                            Self::DrainDecodedFrames(
                                &mut decoder,
                                stream_idx,
                                tb,
                                &t_parsed,
                                &t_ready,
                                &mut resampler,
                                &mut resampler_signature,
                                &mut next_frame_time_sec,
                                &mut decode_count,
                                &mut missing_pts_count,
                            ),
                            DrainStatus::ReachedEof
                        ) {
                            push_eof_frame();
                        }
                        if let Ok(mut s) = t_source.lock() {
                            s.Recycle(p);
                        }
                        continue;
                    }

                    if p.IsSeekRequest() {
                        t_parsed.Flush();
                        if let Ok(mut pending_frame) = t_pending.lock() {
                            *pending_frame = None;
                        }
                        t_seek_request.store(true, Ordering::SeqCst);
                        next_frame_time_sec = Some(p.SeekTime());
                        decoder.flush();
                        resampler = None;
                        resampler_signature = None;

                        if let Ok(mut s) = t_source.lock() {
                            s.Recycle(p);
                        }
                        continue;
                    }

                    if let Err(err) = decoder.send_packet(&p.Packet) {
                        Debug::LogWarning(&format!(
                            "AVLibAudioDecoder::DecodeThread - send_packet failed: {}",
                            err
                        ));
                    }

                    let _ = Self::DrainDecodedFrames(
                        &mut decoder,
                        stream_idx,
                        tb,
                        &t_parsed,
                        &t_ready,
                        &mut resampler,
                        &mut resampler_signature,
                        &mut next_frame_time_sec,
                        &mut decode_count,
                        &mut missing_pts_count,
                    );

                    if let Ok(mut s) = t_source.lock() {
                        s.Recycle(p);
                    }
                } else {
                    let guard = t_continue_mutex
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let _ = t_continue_condition
                        .wait_timeout(guard, std::time::Duration::from_millis(5));
                }
            }
        }));

        obj
    }

    pub fn StreamIndex(&self) -> i32 {
        self._streamIndex
    }

    pub fn NeedsRecreate(&self) -> bool {
        let stream = if let Ok(source) = self._source.lock() {
            source.Stream(self._streamIndex)
        } else {
            return false;
        };

        Self::StreamShapeChanged(self._sourceStreamInfo, stream)
    }

    fn StreamShapeChanged(previous: AVLibStreamInfo, current: AVLibStreamInfo) -> bool {
        let sample_rate_changed = previous.sample_rate > 0
            && current.sample_rate > 0
            && previous.sample_rate != current.sample_rate;
        let channels_changed =
            previous.channels > 0 && current.channels > 0 && previous.channels != current.channels;

        sample_rate_changed || channels_changed
    }

    pub fn Recycle(&self, mut frame: AudioFrame) {
        frame.OnRecycle();
        self._readyFrames.Push(frame);
    }

    pub fn FlushFrames(&self) {
        let drained = self._parsedFrames.Drain();
        for frame in drained {
            self.Recycle(frame);
        }

        let pending = {
            let mut guard = match self._pendingFrame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };
            guard.take()
        };

        if let Some(frame) = pending {
            self.Recycle(frame);
        }

        self._continueCondition.notify_all();
    }

    pub fn TryGetNext(&self, time: f64) -> Option<AudioFrame> {
        if self._parsedFrames.Count() <= self._resumeThreshold {
            self._continueCondition.notify_all();
        }

        let seek_requested = self._seekRequest.swap(false, Ordering::SeqCst);
        let mut pending = {
            let mut guard = match self._pendingFrame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };

            if guard.is_none() || seek_requested {
                *guard = self._parsedFrames.TryPop();
            }

            guard.take()
        };

        let Some(frame) = pending.take() else {
            return None;
        };

        if frame.IsEOF() {
            return Some(frame);
        }

        if time + Self::DUE_EPSILON_SEC < frame.Time() {
            let mut guard = match self._pendingFrame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };
            *guard = Some(frame);
            return None;
        }

        Some(frame)
    }

    pub fn DroppedFrameCount(&self) -> u64 {
        self._parsedFrames.DroppedCount()
    }
}

impl Drop for AVLibAudioDecoder {
    fn drop(&mut self) {
        self._stayAlive.store(false, Ordering::SeqCst);
        self._continueCondition.notify_all();
        if let Some(t) = self._thread.take() {
            let _ = t.join();
        }
    }
}
