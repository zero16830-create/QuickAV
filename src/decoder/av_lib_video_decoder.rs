use crate::av_lib_source::{AvLibSource, AvLibStreamInfo};
use crate::av_lib_util::pixel_format_to_ffmpeg;
use crate::fixed_size_queue::FixedSizeQueue;
use crate::pixel_format::PixelFormat;
use crate::video_description::VideoDescription;
use crate::video_frame::VideoFrame;
use ffmpeg_next::software::scaling::{flag::Flags, Context as SwsContext};
use ffmpeg_next::util::error::EAGAIN;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Condvar, Mutex,
};
use std::thread;

pub struct AvLibVideoDecoder {
    source: Arc<Mutex<Box<dyn AvLibSource + Send>>>,
    stream_index: i32,
    source_stream_info: AvLibStreamInfo,
    decoded_shape: Arc<Mutex<(i32, i32)>>,
    pub parsed_frames: Arc<FixedSizeQueue<VideoFrame>>,
    ready_frames: Arc<FixedSizeQueue<VideoFrame>>,
    is_realtime: bool,
    resume_threshold: usize,
    stay_alive: Arc<AtomicBool>,
    thread: Option<thread::JoinHandle<()>>,
    last_frame: Arc<Mutex<Option<VideoFrame>>>,
    seek_request: Arc<AtomicBool>,
    continue_condition: Arc<Condvar>,
}

impl AvLibVideoDecoder {
    const DEFAULT_VIDEO_FRAME_QUEUE_SIZE: usize = 25;
    const REALTIME_VIDEO_FRAME_QUEUE_SIZE: usize = 3;
    const REALTIME_RESUME_THRESHOLD: usize = 1;
    const REALTIME_EARLY_FRAME_TOLERANCE_SEC: f64 = 0.060;

    pub fn new(
        source: Arc<Mutex<Box<dyn AvLibSource + Send>>>,
        stream_idx: i32,
        target_desc: &dyn VideoDescription,
        mut decoder: ffmpeg_next::decoder::Video,
        tb: f64,
    ) -> Self {
        let is_realtime = if let Ok(s) = source.lock() {
            s.is_realtime()
        } else {
            false
        };
        let source_stream_info = if let Ok(s) = source.lock() {
            s.stream(stream_idx)
        } else {
            AvLibStreamInfo::empty()
        };
        let frame_queue_size = if is_realtime {
            Self::REALTIME_VIDEO_FRAME_QUEUE_SIZE
        } else {
            Self::DEFAULT_VIDEO_FRAME_QUEUE_SIZE
        };
        let resume_threshold = if is_realtime {
            Self::REALTIME_RESUME_THRESHOLD
        } else {
            frame_queue_size / 2
        };
        let parsed = Arc::new(FixedSizeQueue::new(frame_queue_size));
        let ready = Arc::new(FixedSizeQueue::new(frame_queue_size));
        let stay_alive = Arc::new(AtomicBool::new(true));
        let seek_request = Arc::new(AtomicBool::new(true));
        let seek_request_time = Arc::new(Mutex::new(0.0));
        let last_frame = Arc::new(Mutex::new(None));
        let decoded_shape = Arc::new(Mutex::new((
            source_stream_info.width.max(0),
            source_stream_info.height.max(0),
        )));
        let continue_mutex = Arc::new(Mutex::new(()));
        let continue_condition = Arc::new(Condvar::new());
        let target_width = target_desc.width() as u32;
        let target_height = target_desc.height() as u32;
        let target_format = target_desc.format();
        let target_pixel = pixel_format_to_ffmpeg(target_format);

        let mut obj = Self {
            source: source.clone(),
            stream_index: stream_idx,
            source_stream_info,
            decoded_shape: decoded_shape.clone(),
            parsed_frames: parsed.clone(),
            ready_frames: ready.clone(),
            is_realtime,
            resume_threshold,
            stay_alive: stay_alive.clone(),
            thread: None,
            last_frame: last_frame.clone(),
            seek_request: seek_request.clone(),
            continue_condition: continue_condition.clone(),
        };

        let tsource = source.clone();
        let t_stay_alive = stay_alive.clone();
        let t_parsed = parsed.clone();
        let t_ready_recycle = ready.clone();
        let t_last_frame = last_frame.clone();
        let t_seek_request = seek_request.clone();
        let t_seek_request_time = seek_request_time.clone();
        let t_decoded_shape = decoded_shape.clone();
        let t_target_width = target_width;
        let t_target_height = target_height;
        let t_continue_mutex = continue_mutex.clone();
        let t_continue_condition = continue_condition.clone();

        obj.thread = Some(thread::spawn(move || {
            let mut scaler: Option<SwsContext> = None;
            let mut scalersource: Option<(ffmpeg_next::format::Pixel, u32, u32)> = None;

            let mut rgb_frame =
                ffmpeg_next::util::frame::Video::new(target_pixel, t_target_width, t_target_height);
            let w = t_target_width as usize;
            let h = t_target_height as usize;
            let mut decode_count: u64 = 0;
            let mut parse_count: u64 = 0;
            enum DrainStatus {
                NeedMoreInput,
                ReachedEof,
                Failed,
            }
            let copy_scaled_frame =
                |src: &ffmpeg_next::util::frame::Video, dst: &mut VideoFrame| -> bool {
                    let plane_count = dst.buffer_count().max(0) as usize;
                    if plane_count == 0 {
                        return false;
                    }

                    for plane in 0..plane_count {
                        let d_stride = dst.stride(plane as i32);
                        if d_stride <= 0 {
                            return false;
                        }

                        let d_stride = d_stride as usize;
                        let s_stride = src.stride(plane);
                        if s_stride == 0 {
                            return false;
                        }

                        let rows = if target_format == PixelFormat::Yuv420p && plane > 0 {
                            h / 2
                        } else {
                            h
                        };
                        let bytes_per_row = d_stride.min(s_stride);
                        let s_data = src.data(plane);
                        let Some(d_data) = dst.buffer_mut(plane) else {
                            return false;
                        };

                        if rows == 0 || bytes_per_row == 0 {
                            continue;
                        }

                        for y in 0..rows {
                            let s_pos = y * s_stride;
                            let d_pos = y * d_stride;
                            if s_pos + bytes_per_row > s_data.len()
                                || d_pos + bytes_per_row > d_data.len()
                            {
                                return false;
                            }

                            d_data[d_pos..d_pos + bytes_per_row]
                                .copy_from_slice(&s_data[s_pos..s_pos + bytes_per_row]);
                        }
                    }

                    true
                };
            let push_eof_frame = || {
                let mut eof = t_ready_recycle
                    .try_pop()
                    .unwrap_or_else(|| VideoFrame::new(w as i32, h as i32, target_format));
                eof.set_eof();
                t_parsed.push(eof);
            };
            let mut drain_decoded_frames = |decoder: &mut ffmpeg_next::decoder::Video,
                                            scaler: &mut Option<SwsContext>,
                                            scalersource: &mut Option<(
                ffmpeg_next::format::Pixel,
                u32,
                u32,
            )>,
                                            rgb_frame: &mut ffmpeg_next::util::frame::Video|
             -> DrainStatus {
                let mut decoded = ffmpeg_next::util::frame::Video::empty();
                loop {
                    match decoder.receive_frame(&mut decoded) {
                        Ok(()) => {
                            let decoded_fmt = decoded.format();
                            let decoded_w = decoded.width();
                            let decoded_h = decoded.height();

                            if let Ok(mut shape) = t_decoded_shape.lock() {
                                *shape = (decoded_w as i32, decoded_h as i32);
                            }

                            let needs_rebuild = match scalersource {
                                Some((fmt, w, h)) => {
                                    *fmt != decoded_fmt || *w != decoded_w || *h != decoded_h
                                }
                                None => true,
                            };

                            if needs_rebuild {
                                match SwsContext::get(
                                    decoded_fmt,
                                    decoded_w,
                                    decoded_h,
                                    target_pixel,
                                    t_target_width,
                                    t_target_height,
                                    Flags::BILINEAR,
                                ) {
                                    Ok(new_scaler) => {
                                        *scaler = Some(new_scaler);
                                        *scalersource = Some((decoded_fmt, decoded_w, decoded_h));
                                        crate::logging::debug::Debug::log(&format!(
                                            "[AvLibVideoDecoder] stream={} rebuild_scaler {}x{} -> {}x{}",
                                            stream_idx,
                                            decoded_w,
                                            decoded_h,
                                            t_target_width,
                                            t_target_height
                                        ));
                                    }
                                    Err(_) => return DrainStatus::Failed,
                                }
                            }

                            let Some(scaler_ref) = scaler.as_mut() else {
                                return DrainStatus::Failed;
                            };

                            if scaler_ref.run(&decoded, rgb_frame).is_err() {
                                return DrainStatus::Failed;
                            }

                            let mut vf = t_ready_recycle.try_pop().unwrap_or_else(|| {
                                VideoFrame::new(w as i32, h as i32, target_format)
                            });
                            vf.set_time(0.0);
                            vf.clear_eof();
                            parse_count += 1;
                            if !copy_scaled_frame(rgb_frame, &mut vf) {
                                continue;
                            }

                            let ts = decoded.timestamp().unwrap_or(0) as f64;
                            vf.set_time(ts * tb);
                            t_parsed.push(vf);
                        }
                        Err(ffmpeg_next::Error::Eof) => return DrainStatus::ReachedEof,
                        Err(ffmpeg_next::Error::Other { errno }) if errno == EAGAIN => {
                            return DrainStatus::NeedMoreInput;
                        }
                        Err(_) => return DrainStatus::Failed,
                    }
                }
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
                            drain_decoded_frames(
                                &mut decoder,
                                &mut scaler,
                                &mut scalersource,
                                &mut rgb_frame
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
                        if let Ok(mut last_frame_lock) = t_last_frame.lock() {
                            *last_frame_lock = None;
                        }
                        if let Ok(mut seek_to) = t_seek_request_time.lock() {
                            *seek_to = p.seek_time();
                        }
                        t_seek_request.store(false, Ordering::SeqCst);
                        decoder.flush();

                        if let Ok(mut s) = tsource.lock() {
                            s.recycle(p);
                        }
                        continue;
                    }

                    let send_result = decoder.send_packet(&p.packet);
                    if send_result.is_ok() {
                        decode_count += 1;
                        if decode_count % 120 == 0 {
                            crate::logging::debug::Debug::log(&format!(
                                "[AvLibVideoDecoder] stream={} send_packet_ok={} queue_count={}",
                                stream_idx,
                                decode_count,
                                t_parsed.len()
                            ));
                        }
                    } else {
                        crate::logging::debug::Debug::log_warning(
                            "AvLibVideoDecoder::DecodeThread - send_packet failed",
                        );
                    }

                    match drain_decoded_frames(
                        &mut decoder,
                        &mut scaler,
                        &mut scalersource,
                        &mut rgb_frame,
                    ) {
                        DrainStatus::ReachedEof => {
                            push_eof_frame();
                        }
                        DrainStatus::NeedMoreInput => {}
                        DrainStatus::Failed => {}
                    }

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
        let width_changed =
            previous.width > 0 && current.width > 0 && previous.width != current.width;
        let height_changed =
            previous.height > 0 && current.height > 0 && previous.height != current.height;

        width_changed || height_changed
    }

    pub fn recycle(&self, mut frame: VideoFrame) {
        frame.on_recycle();
        self.ready_frames.push(frame);
    }

    pub fn flush_realtime_frames(&self) {
        if !self.is_realtime {
            return;
        }

        let drained = self.parsed_frames.drain();
        for frame in drained {
            self.recycle(frame);
        }

        let last_frame = {
            let mut last = match self.last_frame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };
            last.take()
        };

        if let Some(frame) = last_frame {
            self.recycle(frame);
        }

        self.continue_condition.notify_all();
    }

    pub fn flush_frames(&self) {
        let drained = self.parsed_frames.drain();
        for frame in drained {
            self.recycle(frame);
        }

        let last_frame = {
            let mut last = match self.last_frame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };
            last.take()
        };

        if let Some(frame) = last_frame {
            self.recycle(frame);
        }

        self.continue_condition.notify_all();
    }

    pub fn try_get_next(&self, time: f64) -> Option<VideoFrame> {
        if self.parsed_frames.len() <= self.resume_threshold {
            self.continue_condition.notify_all();
        }

        let realtime_now = if let Ok(s) = self.source.lock() {
            s.is_realtime()
        } else {
            self.is_realtime
        };

        if realtime_now {
            let held_frame = {
                let mut last = match self.last_frame.lock() {
                    Ok(g) => g,
                    Err(poisoned) => poisoned.into_inner(),
                };
                last.take()
            };

            if let Some(frame) = held_frame {
                if frame.is_eof() || frame.time() <= time + Self::REALTIME_EARLY_FRAME_TOLERANCE_SEC
                {
                    return Some(frame);
                }

                let mut last = match self.last_frame.lock() {
                    Ok(g) => g,
                    Err(poisoned) => poisoned.into_inner(),
                };
                *last = Some(frame);
            }

            let mut drained = self.parsed_frames.drain();
            if drained.is_empty() {
                return None;
            }

            self.continue_condition.notify_all();

            let latest = drained.pop();
            for stale in drained {
                self.recycle(stale);
            }

            let Some(frame) = latest else {
                return None;
            };

            if !frame.is_eof() && frame.time() > time + Self::REALTIME_EARLY_FRAME_TOLERANCE_SEC {
                let mut last = match self.last_frame.lock() {
                    Ok(g) => g,
                    Err(poisoned) => poisoned.into_inner(),
                };
                if let Some(old_frame) = last.replace(frame) {
                    self.recycle(old_frame);
                }
                return None;
            }

            return Some(frame);
        }

        let seek_requested = !self.seek_request.swap(true, Ordering::SeqCst);

        let mut last_frame = {
            let mut last = match self.last_frame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };

            if last.is_none() || seek_requested {
                *last = self.parsed_frames.try_pop();
            }

            last.take()
        };

        if last_frame.is_none() {
            if seek_requested {
                // 与 C++ 一致：seek 后若还没拿到新帧，保持 seek 请求。
                self.seek_request.store(false, Ordering::SeqCst);
            }
            return None;
        }

        let mut last_frame = last_frame.take().unwrap();
        let eof = last_frame.is_eof();
        let mut behind = time >= last_frame.time();

        if !eof && behind {
            let mut available = !self.parsed_frames.is_empty();
            let mut next_frame: Option<VideoFrame> = None;
            let mut reached_eof = false;

            while behind && available && !reached_eof {
                next_frame = self.parsed_frames.try_pop();
                available = next_frame.is_some();

                if let Some(candidate) = next_frame.take() {
                    if candidate.is_eof() {
                        self.recycle(last_frame);
                        last_frame = candidate;
                        reached_eof = true;
                    } else {
                        behind = time >= candidate.time();
                        if behind {
                            self.recycle(last_frame);
                            last_frame = candidate;
                        } else {
                            next_frame = Some(candidate);
                        }
                    }
                }
            }

            if reached_eof {
                return Some(last_frame);
            }

            if !behind {
                let mut last = match self.last_frame.lock() {
                    Ok(g) => g,
                    Err(poisoned) => poisoned.into_inner(),
                };
                *last = next_frame;
                return Some(last_frame);
            }

            return Some(last_frame);
        }

        {
            let mut last = match self.last_frame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };
            *last = Some(last_frame);
        }

        None
    }

    pub fn dropped_frame_count(&self) -> u64 {
        self.parsed_frames.dropped_count()
    }

    pub fn actual_source_video_size(&self) -> Option<(i32, i32)> {
        let Ok(shape) = self.decoded_shape.lock() else {
            return None;
        };

        if shape.0 > 0 && shape.1 > 0 {
            Some(*shape)
        } else {
            None
        }
    }
}

impl Drop for AvLibVideoDecoder {
    fn drop(&mut self) {
        self.stay_alive.store(false, Ordering::SeqCst);
        self.continue_condition.notify_all();
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}
