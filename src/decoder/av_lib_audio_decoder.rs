use crate::audio_frame::AudioFrame;
use crate::av_lib_source::{AvLibSource, AvLibStreamInfo};
use crate::fixed_size_queue::FixedSizeQueue;
use crate::logging::debug::Debug;
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

pub struct AvLibAudioDecoder {
    source: Arc<Mutex<Box<dyn AvLibSource + Send>>>,
    stream_index: i32,
    source_stream_info: AvLibStreamInfo,
    pub parsed_frames: Arc<FixedSizeQueue<AudioFrame>>,
    ready_frames: Arc<FixedSizeQueue<AudioFrame>>,
    resume_threshold: usize,
    stay_alive: Arc<AtomicBool>,
    thread: Option<thread::JoinHandle<()>>,
    pending_frame: Arc<Mutex<Option<AudioFrame>>>,
    seek_request: Arc<AtomicBool>,
    continue_condition: Arc<Condvar>,
}

impl AvLibAudioDecoder {
    const DEFAULT_AUDIO_FRAME_QUEUE_SIZE: usize = 96;
    const REALTIME_AUDIO_FRAME_QUEUE_SIZE: usize = 96;
    const REALTIME_RESUME_THRESHOLD: usize = 12;
    const DUE_EPSILON_SEC: f64 = 0.002;

    fn resolve_channel_layout(frame: &ffmpeg_next::util::frame::Audio) -> ChannelLayout {
        if frame.channel_layout().is_empty() {
            ChannelLayout::default(frame.channels() as i32)
        } else {
            frame.channel_layout()
        }
    }

    fn drain_decoded_frames(
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
                    let input_layout = Self::resolve_channel_layout(&decoded);
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
                                *resampler_signature =
                                    Some((input_format, input_layout, input_rate));
                                Debug::log(&format!(
                                    "[AvLibAudioDecoder] stream={} rebuild_resampler rate={} channels={} format={:?}",
                                    stream_idx,
                                    input_rate,
                                    input_layout.channels(),
                                    input_format
                                ));
                            }
                            Err(err) => {
                                Debug::log_warning(&format!(
                                    "AvLibAudioDecoder::DecodeThread - rebuild_resampler failed: {}",
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
                        Debug::log_warning(&format!(
                            "AvLibAudioDecoder::DecodeThread - resample failed: {}",
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

                    let required_bytes =
                        channels as usize * samples as usize * AudioFrame::BYTES_PER_SAMPLE;
                    let data = converted.data(0);
                    if data.len() < required_bytes {
                        Debug::log_warning(
                            "AvLibAudioDecoder::DecodeThread - converted frame buffer was too small",
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
                        .try_pop()
                        .unwrap_or_else(|| AudioFrame::new(sample_rate, channels, samples));
                    frame.ensure_layout(sample_rate, channels, samples);
                    frame.clear_eof();
                    frame.set_time(time_sec);
                    frame.set_duration(duration_sec);
                    frame.buffer_mut()[..required_bytes].copy_from_slice(&data[..required_bytes]);
                    parsed.push(frame);

                    *decode_count += 1;
                    if *decode_count % 120 == 0 {
                        Debug::log(&format!(
                            "[AvLibAudioDecoder] stream={} decoded={} queue_count={} missing_pts={}",
                            stream_idx,
                            *decode_count,
                            parsed.len(),
                            *missing_pts_count
                        ));
                    }
                }
                Err(ffmpeg_next::Error::Eof) => return DrainStatus::ReachedEof,
                Err(ffmpeg_next::Error::Other { errno }) if errno == EAGAIN => {
                    return DrainStatus::NeedMoreInput;
                }
                Err(err) => {
                    Debug::log_warning(&format!(
                        "AvLibAudioDecoder::DecodeThread - receive_frame failed: {}",
                        err
                    ));
                    return DrainStatus::Failed;
                }
            }
        }
    }

    pub fn new(
        source: Arc<Mutex<Box<dyn AvLibSource + Send>>>,
        stream_idx: i32,
        mut decoder: ffmpeg_next::decoder::Audio,
        tb: f64,
    ) -> Self {
        let is_realtime = if let Ok(s) = source.lock() {
            s.is_realtime()
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
            s.stream(stream_idx)
        } else {
            AvLibStreamInfo::empty()
        };

        let mut obj = Self {
            source: source.clone(),
            stream_index: stream_idx,
            source_stream_info,
            parsed_frames: parsed.clone(),
            ready_frames: ready.clone(),
            resume_threshold,
            stay_alive: stay_alive.clone(),
            thread: None,
            pending_frame: pending.clone(),
            seek_request: seek_request.clone(),
            continue_condition: continue_condition.clone(),
        };

        let tsource = source.clone();
        let t_stay_alive = stay_alive.clone();
        let t_parsed = parsed.clone();
        let t_ready = ready.clone();
        let t_pending = pending.clone();
        let t_seek_request = seek_request.clone();
        let t_continue_mutex = continue_mutex.clone();
        let t_continue_condition = continue_condition.clone();

        obj.thread = Some(thread::spawn(move || {
            let mut resampler: Option<SwrContext> = None;
            let mut resampler_signature: Option<(Sample, ChannelLayout, u32)> = None;
            let mut next_frame_time_sec: Option<f64> = None;
            let mut decode_count: u64 = 0;
            let mut missing_pts_count: u64 = 0;

            let push_eof_frame = || {
                let mut eof = t_ready
                    .try_pop()
                    .unwrap_or_else(|| AudioFrame::new(0, 0, 0));
                eof.set_eof();
                t_parsed.push(eof);
            };

            while t_stay_alive.load(Ordering::SeqCst) {
                if t_parsed.is_full() {
                    let guard = t_continue_mutex
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let _ = t_continue_condition
                        .wait_timeout(guard, std::time::Duration::from_millis(5));
                    continue;
                }

                let packet_opt = if let Ok(mut s) = tsource.lock() {
                    s.try_get_next(stream_idx)
                } else {
                    None
                };

                if let Some(p) = packet_opt {
                    if p.is_eof() {
                        let _ = decoder.send_eof();
                        if matches!(
                            Self::drain_decoded_frames(
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
                        if let Ok(mut s) = tsource.lock() {
                            s.recycle(p);
                        }
                        continue;
                    }

                    if p.is_seek_request() {
                        t_parsed.flush();
                        if let Ok(mut pending_frame) = t_pending.lock() {
                            *pending_frame = None;
                        }
                        t_seek_request.store(true, Ordering::SeqCst);
                        next_frame_time_sec = Some(p.seek_time());
                        decoder.flush();
                        resampler = None;
                        resampler_signature = None;

                        if let Ok(mut s) = tsource.lock() {
                            s.recycle(p);
                        }
                        continue;
                    }

                    if let Err(err) = decoder.send_packet(&p.packet) {
                        Debug::log_warning(&format!(
                            "AvLibAudioDecoder::DecodeThread - send_packet failed: {}",
                            err
                        ));
                    }

                    let _ = Self::drain_decoded_frames(
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

                    if let Ok(mut s) = tsource.lock() {
                        s.recycle(p);
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

    pub fn stream_index(&self) -> i32 {
        self.stream_index
    }

    pub fn needs_recreate(&self) -> bool {
        let stream = if let Ok(source) = self.source.lock() {
            source.stream(self.stream_index)
        } else {
            return false;
        };

        Self::stream_shape_changed(self.source_stream_info, stream)
    }

    fn stream_shape_changed(previous: AvLibStreamInfo, current: AvLibStreamInfo) -> bool {
        let sample_rate_changed = previous.sample_rate > 0
            && current.sample_rate > 0
            && previous.sample_rate != current.sample_rate;
        let channels_changed =
            previous.channels > 0 && current.channels > 0 && previous.channels != current.channels;

        sample_rate_changed || channels_changed
    }

    pub fn recycle(&self, mut frame: AudioFrame) {
        frame.on_recycle();
        self.ready_frames.push(frame);
    }

    pub fn flush_frames(&self) {
        let drained = self.parsed_frames.drain();
        for frame in drained {
            self.recycle(frame);
        }

        let pending = {
            let mut guard = match self.pending_frame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };
            guard.take()
        };

        if let Some(frame) = pending {
            self.recycle(frame);
        }

        self.continue_condition.notify_all();
    }

    pub fn try_get_next(&self, time: f64) -> Option<AudioFrame> {
        if self.parsed_frames.len() <= self.resume_threshold {
            self.continue_condition.notify_all();
        }

        let seek_requested = self.seek_request.swap(false, Ordering::SeqCst);
        let mut pending = {
            let mut guard = match self.pending_frame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };

            if guard.is_none() || seek_requested {
                *guard = self.parsed_frames.try_pop();
            }

            guard.take()
        };

        let Some(frame) = pending.take() else {
            return None;
        };

        if frame.is_eof() {
            return Some(frame);
        }

        if time + Self::DUE_EPSILON_SEC < frame.time() {
            let mut guard = match self.pending_frame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };
            *guard = Some(frame);
            return None;
        }

        Some(frame)
    }

    pub fn dropped_frame_count(&self) -> u64 {
        self.parsed_frames.dropped_count()
    }
}

impl Drop for AvLibAudioDecoder {
    fn drop(&mut self) {
        self.stay_alive.store(false, Ordering::SeqCst);
        self.continue_condition.notify_all();
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}
