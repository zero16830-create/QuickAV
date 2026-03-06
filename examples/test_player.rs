#[cfg(not(windows))]
fn main() {
    eprintln!("test_player 仅支持 Windows");
    std::process::exit(1);
}

#[cfg(windows)]
mod win32_player {
    use rustav_native::AudioExportState::{ExportedAudioMeta, SharedExportedAudioState};
    use rustav_native::AudioFrame::AudioSampleFormat;
    use rustav_native::FrameExportClient::SharedExportedFrameState;
    use rustav_native::Logging::Debug::{Initialize, Teardown};
    use rustav_native::Player::Player;
    use std::ffi::c_void;
    use std::mem::size_of;
    use std::ptr::{addr_of, addr_of_mut};
    use std::sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex, OnceLock,
    };
    use std::thread;
    use std::time::{Duration, Instant};
    use windows::core::{PCWSTR, PSTR};
    use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, RECT, WPARAM};
    use windows::Win32::Graphics::Gdi::{
        BeginPaint, EndPaint, InvalidateRect, StretchDIBits, UpdateWindow, BITMAPINFO,
        BITMAPINFOHEADER, BI_RGB, DIB_RGB_COLORS, HBRUSH, PAINTSTRUCT, SRCCOPY,
    };
    use windows::Win32::Media::timeBeginPeriod;
    use windows::Win32::Media::timeEndPeriod;
    use windows::Win32::Media::{MMTIME, TIME_MS, TIME_SAMPLES, TIMERR_NOERROR};
    use windows::Win32::Media::Audio::{
        waveOutClose, waveOutGetPosition, waveOutOpen, waveOutPause, waveOutPrepareHeader,
        waveOutReset, waveOutRestart, waveOutUnprepareHeader, waveOutWrite, CALLBACK_NULL, HWAVEOUT,
        WAVEFORMATEX, WAVEHDR, WAVE_FORMAT_PCM, WAVE_MAPPER, WHDR_DONE,
    };
    use windows::Win32::System::LibraryLoader::GetModuleHandleW;
    use windows::Win32::System::Threading::{
        GetCurrentThread, SetThreadPriority, THREAD_PRIORITY_HIGHEST,
    };
    use windows::Win32::UI::WindowsAndMessaging::{
        AdjustWindowRectEx, CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW,
        GetClientRect, LoadCursorW, PeekMessageW, PostQuitMessage, RegisterClassW, ShowWindow,
        TranslateMessage, CS_HREDRAW, CS_VREDRAW, CW_USEDEFAULT, HMENU, IDC_ARROW, MSG,
        PM_REMOVE, SW_SHOW, WINDOW_EX_STYLE, WM_CLOSE, WM_DESTROY, WM_PAINT, WM_QUIT, WNDCLASSW,
        WS_OVERLAPPEDWINDOW, WS_VISIBLE,
    };

    static VIEWER_STATE: OnceLock<Arc<Mutex<ViewerState>>> = OnceLock::new();

    struct Config {
        uri: String,
        width: i32,
        height: i32,
        max_seconds: Option<f64>,
        loop_player: bool,
    }

    struct ViewerState {
        source_width: i32,
        source_height: i32,
        stride: i32,
        rgba_buffer: Vec<u8>,
        bgra_buffer: Vec<u8>,
        has_frame: bool,
        last_frame_index: i64,
        last_time_sec: f64,
        title: String,
    }

    impl ViewerState {
        fn new(width: i32, height: i32, title: String) -> Self {
            let pixel_bytes = width.saturating_mul(height).saturating_mul(4).max(0) as usize;
            Self {
                source_width: width,
                source_height: height,
                stride: width.saturating_mul(4),
                rgba_buffer: vec![0; pixel_bytes],
                bgra_buffer: vec![0; pixel_bytes],
                has_frame: false,
                last_frame_index: -1,
                last_time_sec: 0.0,
                title,
            }
        }
    }

    const AUDIO_THREAD_POLL_MILLISECONDS: u64 = 1;
    const AUDIO_CHUNK_MILLISECONDS: usize = 10;
    const AUDIO_START_BUFFER_MILLISECONDS: usize = 80;
    const AUDIO_TARGET_BUFFER_MILLISECONDS: usize = 90;
    const AUDIO_LOW_WATERMARK_MILLISECONDS: usize = 30;
    const AUDIO_UNDERFLOW_GRACE_MILLISECONDS: u64 = 40;
    const AUDIO_BUFFER_COUNT: usize = 16;
    const MAX_AUDIO_READ_BYTES: usize = 64 * 1024;

    struct WaveOutBuffer {
        data: Vec<u8>,
        header: WAVEHDR,
        prepared: bool,
        queued: bool,
    }

    impl WaveOutBuffer {
        fn new(capacity: usize) -> Self {
            let mut data = vec![0u8; capacity];
            let mut header = WAVEHDR::default();
            set_wavehdr_layout(&mut header, data.as_mut_ptr(), 0);
            Self {
                data,
                header,
                prepared: false,
                queued: false,
            }
        }
    }

    struct WaveOutAudioDevice {
        handle: HWAVEOUT,
        sample_rate: i32,
        channels: i32,
        chunk_input_bytes: usize,
        chunk_output_bytes: usize,
        start_threshold_bytes: usize,
        target_buffer_bytes: usize,
        low_watermark_bytes: usize,
        pending_pcm: std::collections::VecDeque<u8>,
        float_scratch: Vec<u8>,
        buffers: Vec<WaveOutBuffer>,
        started: bool,
        starving_since: Option<Instant>,
        underflow_logged: bool,
        submitted_frames: u64,
        last_source_time: f64,
        last_log: Instant,
    }

    impl WaveOutAudioDevice {
        fn open(meta: ExportedAudioMeta) -> Result<Self, String> {
            if meta.SampleRate <= 0
                || meta.Channels <= 0
                || meta.BytesPerSample != 4
                || meta.SampleFormat != AudioSampleFormat::F32 as i32
            {
                return Err(format!(
                    "unsupported audio format rate={} channels={} bytes_per_sample={} sample_format={}",
                    meta.SampleRate, meta.Channels, meta.BytesPerSample, meta.SampleFormat
                ));
            }

            let block_align = (meta.Channels as usize * std::mem::size_of::<i16>()) as u16;
            let format = WAVEFORMATEX {
                wFormatTag: WAVE_FORMAT_PCM as u16,
                nChannels: meta.Channels as u16,
                nSamplesPerSec: meta.SampleRate as u32,
                nAvgBytesPerSec: meta.SampleRate as u32 * block_align as u32,
                nBlockAlign: block_align,
                wBitsPerSample: 16,
                cbSize: 0,
            };

            let chunk_frames =
                ((meta.SampleRate as usize * AUDIO_CHUNK_MILLISECONDS) / 1000).max(1);
            let chunk_output_bytes = chunk_frames * meta.Channels as usize * std::mem::size_of::<i16>();
            let chunk_input_bytes = chunk_frames * meta.Channels as usize * meta.BytesPerSample as usize;
            let start_chunks =
                (AUDIO_START_BUFFER_MILLISECONDS / AUDIO_CHUNK_MILLISECONDS).max(1);
            let start_threshold_bytes = chunk_output_bytes * start_chunks;
            let target_chunks =
                (AUDIO_TARGET_BUFFER_MILLISECONDS / AUDIO_CHUNK_MILLISECONDS).max(start_chunks);
            let target_buffer_bytes = chunk_output_bytes * target_chunks;
            let low_watermark_chunks =
                (AUDIO_LOW_WATERMARK_MILLISECONDS / AUDIO_CHUNK_MILLISECONDS).max(1);
            let low_watermark_bytes = chunk_output_bytes * low_watermark_chunks;

            let mut handle = HWAVEOUT::default();
            mm_check(
                unsafe {
                    waveOutOpen(Some(&mut handle), WAVE_MAPPER, &format, 0, 0, CALLBACK_NULL)
                },
                "waveOutOpen",
            )?;
            mm_check(unsafe { waveOutPause(handle) }, "waveOutPause")?;

            let buffers = (0..AUDIO_BUFFER_COUNT)
                .map(|_| WaveOutBuffer::new(chunk_output_bytes))
                .collect();

            println!(
                "[test_player audio] device_open rate={}Hz channels={} chunk_ms={} start_buffer_ms={}",
                meta.SampleRate,
                meta.Channels,
                AUDIO_CHUNK_MILLISECONDS,
                AUDIO_START_BUFFER_MILLISECONDS
            );

            Ok(Self {
                handle,
                sample_rate: meta.SampleRate,
                channels: meta.Channels,
                chunk_input_bytes,
                chunk_output_bytes,
                start_threshold_bytes,
                target_buffer_bytes,
                low_watermark_bytes,
                pending_pcm: std::collections::VecDeque::new(),
                float_scratch: Vec::new(),
                buffers,
                started: false,
                starving_since: None,
                underflow_logged: false,
                submitted_frames: 0,
                last_source_time: meta.Time,
                last_log: Instant::now(),
            })
        }

        fn same_format(&self, meta: &ExportedAudioMeta) -> bool {
            self.sample_rate == meta.SampleRate
                && self.channels == meta.Channels
                && meta.BytesPerSample == 4
                && meta.SampleFormat == AudioSampleFormat::F32 as i32
        }

        fn shutdown(&mut self) {
            let _ = self.reset_stream_state();
            let _ = unsafe { waveOutClose(self.handle) };
        }

        fn pump(&mut self, shared: &SharedExportedAudioState) -> Result<(), String> {
            self.reclaim_done_buffers()?;

            let desired_buffer_bytes = if self.started {
                self.target_buffer_bytes
            } else {
                self.start_threshold_bytes
            };
            let (meta, read_bytes) = self.fill_from_export(shared, desired_buffer_bytes)?;

            self.queue_pending_buffers()?;

            let queued_bytes = self.queued_bytes();
            let total_buffered_bytes = queued_bytes + self.pending_pcm.len();

            if !self.started && total_buffered_bytes >= self.start_threshold_bytes {
                mm_check(unsafe { waveOutRestart(self.handle) }, "waveOutRestart")?;
                self.started = true;
                self.starving_since = None;
                self.underflow_logged = false;
                println!(
                    "[test_player audio] start queued_ms={} pending_ms={} read_bytes={}",
                    self.bytes_to_ms(queued_bytes),
                    self.bytes_to_ms(self.pending_pcm.len()),
                    read_bytes
                );
            } else if self.started && total_buffered_bytes == 0 {
                match self.starving_since {
                    Some(since)
                        if !self.underflow_logged
                            && since.elapsed()
                                >= Duration::from_millis(AUDIO_UNDERFLOW_GRACE_MILLISECONDS) =>
                    {
                        self.underflow_logged = true;
                        println!("[test_player audio] underflow");
                    }
                    Some(_) => {}
                    None => {
                        self.starving_since = Some(Instant::now());
                    }
                }
            } else {
                if self.underflow_logged {
                    println!(
                        "[test_player audio] recovered queued_ms={} pending_ms={} read_bytes={}",
                        self.bytes_to_ms(queued_bytes),
                        self.bytes_to_ms(self.pending_pcm.len()),
                        read_bytes
                    );
                }
                self.starving_since = None;
                self.underflow_logged = false;
            }

            if self.last_log.elapsed() >= Duration::from_secs(1) {
                println!(
                    "[test_player audio] queued_ms={} pending_ms={} total_ms={} started={} has_audio={}",
                    self.bytes_to_ms(queued_bytes),
                    self.bytes_to_ms(self.pending_pcm.len()),
                    self.bytes_to_ms(total_buffered_bytes),
                    self.started,
                    meta.HasAudio
                );
                self.last_log = Instant::now();
            }

            Ok(())
        }

        fn buffered_delay_seconds(&self) -> f64 {
            let pending_frames = self.bytes_to_frames(self.pending_pcm.len()) as u64;
            let played_frames = self.played_frames().unwrap_or(0);
            let queued_frames = self.submitted_frames.saturating_sub(played_frames);
            (queued_frames + pending_frames) as f64 / self.sample_rate as f64
        }

        fn reclaim_done_buffers(&mut self) -> Result<(), String> {
            for buffer in self.buffers.iter_mut() {
                if !buffer.queued {
                    continue;
                }

                if (wavehdr_flags(&buffer.header) & WHDR_DONE) == 0 {
                    continue;
                }

                if buffer.prepared {
                    mm_check(
                        unsafe {
                            waveOutUnprepareHeader(
                                self.handle,
                                &mut buffer.header,
                                size_of::<WAVEHDR>() as u32,
                            )
                        },
                        "waveOutUnprepareHeader",
                    )?;
                }

                buffer.prepared = false;
                buffer.queued = false;
                set_wavehdr_layout(&mut buffer.header, buffer.data.as_mut_ptr(), 0);
            }

            Ok(())
        }

        fn queue_pending_buffers(&mut self) -> Result<(), String> {
            while self.pending_pcm.len() >= self.chunk_output_bytes {
                let Some(buffer) = self.buffers.iter_mut().find(|b| !b.queued) else {
                    break;
                };

                let bytes_to_queue = self.chunk_output_bytes;

                for byte in buffer.data.iter_mut().take(bytes_to_queue) {
                    *byte = self
                        .pending_pcm
                        .pop_front()
                        .expect("pending pcm should contain enough bytes");
                }

                set_wavehdr_layout(
                    &mut buffer.header,
                    buffer.data.as_mut_ptr(),
                    bytes_to_queue,
                );

                mm_check(
                    unsafe {
                        waveOutPrepareHeader(
                            self.handle,
                            &mut buffer.header,
                            size_of::<WAVEHDR>() as u32,
                        )
                    },
                    "waveOutPrepareHeader",
                )?;
                buffer.prepared = true;

                mm_check(
                    unsafe {
                        waveOutWrite(
                            self.handle,
                            &mut buffer.header,
                            size_of::<WAVEHDR>() as u32,
                        )
                    },
                    "waveOutWrite",
                )?;
                buffer.queued = true;
                self.submitted_frames = self
                    .submitted_frames
                    .saturating_add(self.bytes_to_frames(bytes_to_queue) as u64);
            }

            Ok(())
        }

        fn reset_stream_state(&mut self) -> Result<(), String> {
            mm_check(unsafe { waveOutReset(self.handle) }, "waveOutReset")?;
            self.pending_pcm.clear();
            self.started = false;
            self.starving_since = None;
            self.underflow_logged = false;
            self.submitted_frames = 0;

            for buffer in self.buffers.iter_mut() {
                if buffer.prepared {
                    mm_check(
                        unsafe {
                            waveOutUnprepareHeader(
                                self.handle,
                                &mut buffer.header,
                                size_of::<WAVEHDR>() as u32,
                            )
                        },
                        "waveOutUnprepareHeader",
                    )?;
                }

                buffer.prepared = false;
                buffer.queued = false;
                set_wavehdr_layout(&mut buffer.header, buffer.data.as_mut_ptr(), 0);
            }

            mm_check(unsafe { waveOutPause(self.handle) }, "waveOutPause after reset")?;
            Ok(())
        }

        fn append_converted_pcm16(
            pending_pcm: &mut std::collections::VecDeque<u8>,
            source: &[u8],
        ) {
            for chunk in source.chunks_exact(4) {
                let sample = f32::from_le_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]);
                let pcm = if sample >= 1.0 {
                    i16::MAX
                } else if sample <= -1.0 {
                    i16::MIN
                } else {
                    (sample * i16::MAX as f32).round() as i16
                };

                let bytes = pcm.to_le_bytes();
                pending_pcm.push_back(bytes[0]);
                pending_pcm.push_back(bytes[1]);
            }
        }

        fn queued_bytes(&self) -> usize {
            self.buffers
                .iter()
                .filter(|buffer| buffer.queued)
                .map(|buffer| buffer.header.dwBufferLength as usize)
                .sum()
        }

        fn bytes_to_ms(&self, bytes: usize) -> usize {
            (self.bytes_to_seconds(bytes) * 1000.0).round() as usize
        }

        fn bytes_to_seconds(&self, bytes: usize) -> f64 {
            let frames = self.bytes_to_frames(bytes);
            if self.sample_rate <= 0 {
                0.0
            } else {
                frames as f64 / self.sample_rate as f64
            }
        }

        fn bytes_to_frames(&self, bytes: usize) -> usize {
            let bytes_per_second = self.sample_rate as usize
                * self.channels as usize
                * std::mem::size_of::<i16>();
            let bytes_per_frame = self.channels as usize * std::mem::size_of::<i16>();
            if bytes_per_second == 0 || bytes_per_frame == 0 {
                0
            } else {
                bytes / bytes_per_frame
            }
        }

        fn played_frames(&self) -> Option<u64> {
            let mut position = MMTIME::default();
            position.wType = TIME_SAMPLES;

            let result = unsafe {
                waveOutGetPosition(self.handle, &mut position, size_of::<MMTIME>() as u32)
            };
            if result != 0 {
                return None;
            }

            match position.wType {
                TIME_SAMPLES => Some(unsafe { position.u.sample as u64 }),
                TIME_MS => Some(
                    (unsafe { position.u.ms as u64 })
                        .saturating_mul(self.sample_rate as u64)
                        / 1000,
                ),
                _ => None,
            }
        }

        fn fill_from_export(
            &mut self,
            shared: &SharedExportedAudioState,
            desired_buffer_bytes: usize,
        ) -> Result<(ExportedAudioMeta, usize), String> {
            let mut total_read_bytes = 0usize;
            let mut last_meta = ExportedAudioMeta {
                SampleRate: self.sample_rate,
                Channels: self.channels,
                BytesPerSample: 4,
                SampleFormat: AudioSampleFormat::F32 as i32,
                BufferedBytes: 0,
                Time: self.last_source_time,
                FrameIndex: 0,
                HasAudio: false,
            };

            loop {
                let total_buffered_bytes = self.queued_bytes() + self.pending_pcm.len();
                if self.started
                    && total_buffered_bytes > self.low_watermark_bytes
                    && total_read_bytes == 0
                {
                    break;
                }

                if total_buffered_bytes >= desired_buffer_bytes {
                    break;
                }

                let mut copied = 0usize;
                {
                    let mut export = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                    let meta = export.Meta();
                    last_meta = meta;

                    if !meta.HasAudio || meta.BufferedBytes <= 0 {
                        break;
                    }

                    if meta.Time + 0.250 < self.last_source_time {
                        println!(
                            "[test_player audio] timeline_reset old_pts={:.3} new_pts={:.3}",
                            self.last_source_time, meta.Time
                        );
                        self.reset_stream_state()?;
                    }

                    let need_output_bytes = desired_buffer_bytes.saturating_sub(total_buffered_bytes);
                    let mut target_read = meta.BufferedBytes as usize;
                    target_read = target_read.min(MAX_AUDIO_READ_BYTES);
                    target_read = target_read.min(need_output_bytes.saturating_mul(2).max(self.chunk_input_bytes));
                    target_read -= target_read % std::mem::size_of::<f32>();
                    if target_read < std::mem::size_of::<f32>() {
                        break;
                    }

                    if self.float_scratch.len() < target_read {
                        self.float_scratch.resize(target_read, 0);
                    }

                    let copied_now = export.CopyTo(&mut self.float_scratch[..target_read]);
                    if copied_now > 0 {
                        copied = copied_now as usize;
                    }
                }

                if copied == 0 {
                    break;
                }

                total_read_bytes += copied;
                Self::append_converted_pcm16(&mut self.pending_pcm, &self.float_scratch[..copied]);
                self.last_source_time = last_meta.Time;

                if copied < self.chunk_input_bytes {
                    break;
                }
            }

            Ok((last_meta, total_read_bytes))
        }
    }

    struct WaveOutAudioOutput {
        stop: Arc<AtomicBool>,
        thread: Option<thread::JoinHandle<()>>,
    }

    impl WaveOutAudioOutput {
        fn new(shared: SharedExportedAudioState, player: Arc<Mutex<Player>>) -> Self {
            let stop = Arc::new(AtomicBool::new(false));
            let thread_stop = stop.clone();

            let thread = thread::spawn(move || {
                let _timer_resolution = TimerResolutionGuard::new(1);
                let _ = unsafe { SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST) };
                let mut device: Option<WaveOutAudioDevice> = None;
                let mut last_open_error = Instant::now() - Duration::from_secs(5);

                while !thread_stop.load(Ordering::SeqCst) {
                    let meta = {
                        let export = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                        export.Meta()
                    };

                    if meta.HasAudio && meta.SampleRate > 0 && meta.Channels > 0 {
                        let need_reopen = match device.as_ref() {
                            Some(existing) => !existing.same_format(&meta),
                            None => true,
                        };

                        if need_reopen {
                            if let Some(mut existing) = device.take() {
                                existing.shutdown();
                            }

                            match WaveOutAudioDevice::open(meta) {
                                Ok(new_device) => {
                                    with_player_audio_sink_delay(
                                        &player,
                                        AUDIO_START_BUFFER_MILLISECONDS as f64 / 1000.0,
                                    );
                                    device = Some(new_device);
                                }
                                Err(err) => {
                                    if last_open_error.elapsed() >= Duration::from_secs(1) {
                                        eprintln!("[test_player audio] open_failed: {err}");
                                        last_open_error = Instant::now();
                                    }
                                }
                            }
                        }
                    }

                    if let Some(existing) = device.as_mut() {
                        if let Err(err) = existing.pump(&shared) {
                            eprintln!("[test_player audio] pump_failed: {err}");
                            existing.shutdown();
                            with_player_audio_sink_delay(&player, 0.0);
                            device = None;
                        } else {
                            with_player_audio_sink_delay(&player, existing.buffered_delay_seconds());
                        }
                    } else {
                        with_player_audio_sink_delay(&player, 0.0);
                    }

                    thread::sleep(Duration::from_millis(AUDIO_THREAD_POLL_MILLISECONDS));
                }

                if let Some(mut existing) = device {
                    existing.shutdown();
                }
                with_player_audio_sink_delay(&player, 0.0);
            });

            Self {
                stop,
                thread: Some(thread),
            }
        }
    }

    struct TimerResolutionGuard {
        period_ms: u32,
        active: bool,
    }

    impl TimerResolutionGuard {
        fn new(period_ms: u32) -> Self {
            let active = unsafe { timeBeginPeriod(period_ms) == TIMERR_NOERROR };
            Self { period_ms, active }
        }
    }

    impl Drop for TimerResolutionGuard {
        fn drop(&mut self) {
            if self.active {
                let _ = unsafe { timeEndPeriod(self.period_ms) };
            }
        }
    }

    impl Drop for WaveOutAudioOutput {
        fn drop(&mut self) {
            self.stop.store(true, Ordering::SeqCst);
            if let Some(thread) = self.thread.take() {
                let _ = thread.join();
            }
        }
    }

    fn mm_check(result: u32, context: &str) -> Result<(), String> {
        if result == 0 {
            Ok(())
        } else {
            Err(format!("{context} failed: MMRESULT={result}"))
        }
    }

    fn with_player_audio_sink_delay(player: &Arc<Mutex<Player>>, delay_sec: f64) {
        if let Ok(guard) = player.lock() {
            guard.SetAudioSinkDelay(delay_sec);
        }
    }

    fn set_wavehdr_layout(header: &mut WAVEHDR, data_ptr: *mut u8, length: usize) {
        unsafe {
            addr_of_mut!(header.lpData).write_unaligned(PSTR(data_ptr));
            addr_of_mut!(header.dwBufferLength).write_unaligned(length as u32);
            addr_of_mut!(header.dwBytesRecorded).write_unaligned(0);
            addr_of_mut!(header.dwUser).write_unaligned(0);
            addr_of_mut!(header.dwFlags).write_unaligned(0);
            addr_of_mut!(header.dwLoops).write_unaligned(0);
            addr_of_mut!(header.lpNext).write_unaligned(std::ptr::null_mut());
            addr_of_mut!(header.reserved).write_unaligned(0);
        }
    }

    fn wavehdr_flags(header: &WAVEHDR) -> u32 {
        unsafe { addr_of!(header.dwFlags).read_unaligned() }
    }

    fn parse_args() -> Result<Config, String> {
        let mut uri = sample_video_uri();
        let mut width = 1280;
        let mut height = 720;
        let mut max_seconds = None;
        let mut loop_player = false;

        for arg in std::env::args().skip(1) {
            if arg == "--help" || arg == "-h" {
                print_usage();
                std::process::exit(0);
            }

            if arg == "--loop" {
                loop_player = true;
                continue;
            }

            if let Some(v) = arg.strip_prefix("--uri=") {
                uri = v.to_string();
                continue;
            }

            if let Some(v) = arg.strip_prefix("--width=") {
                width = v
                    .parse::<i32>()
                    .map_err(|_| format!("无效的 --width 参数: {v}"))?;
                continue;
            }

            if let Some(v) = arg.strip_prefix("--height=") {
                height = v
                    .parse::<i32>()
                    .map_err(|_| format!("无效的 --height 参数: {v}"))?;
                continue;
            }

            if let Some(v) = arg.strip_prefix("--max-seconds=") {
                max_seconds = Some(
                    v.parse::<f64>()
                        .map_err(|_| format!("无效的 --max-seconds 参数: {v}"))?,
                );
                continue;
            }

            return Err(format!("未知参数: {arg}"));
        }

        if width <= 0 || height <= 0 {
            return Err("width 和 height 必须大于 0".to_string());
        }

        Ok(Config {
            uri,
            width,
            height,
            max_seconds,
            loop_player,
        })
    }

    fn print_usage() {
        println!("Usage: test_player [--uri=<uri>] [--width=<w>] [--height=<h>] [--max-seconds=<n>] [--loop]");
        println!("默认使用 TestFiles 里的 sample video；也可直接传 rtsp:// 或 rtmp://");
    }

    fn sample_video_uri() -> String {
        let candidates = [
            "../TestFiles/SampleVideo_1280x720_10mb.mp4",
            "TestFiles/SampleVideo_1280x720_10mb.mp4",
            "./TestFiles/SampleVideo_1280x720_10mb.mp4",
        ];

        for candidate in candidates {
            if std::path::Path::new(candidate).exists() {
                return candidate.to_string();
            }
        }

        candidates[0].to_string()
    }

    fn to_wstring(value: &str) -> Vec<u16> {
        value.encode_utf16().chain(Some(0)).collect()
    }

    fn pull_latest_frame(
        shared: &SharedExportedFrameState,
        viewer_state: &Arc<Mutex<ViewerState>>,
    ) -> bool {
        let shared = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
        let meta = shared.Meta();
        if !meta.HasFrame {
            return false;
        }

        let mut viewer = viewer_state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        if meta.FrameIndex == viewer.last_frame_index {
            return false;
        }

        if meta.Width <= 0 || meta.Height <= 0 || meta.DataLength <= 0 {
            return false;
        }

        let data_len = meta.DataLength as usize;
        if viewer.rgba_buffer.len() != data_len {
            viewer.rgba_buffer.resize(data_len, 0);
        }
        if viewer.bgra_buffer.len() != data_len {
            viewer.bgra_buffer.resize(data_len, 0);
        }

        let copied = shared.CopyTo(&mut viewer.rgba_buffer);
        if copied <= 0 {
            return false;
        }

        let pixel_count = data_len / 4;
        for index in 0..pixel_count {
            let offset = index * 4;
            viewer.bgra_buffer[offset] = viewer.rgba_buffer[offset + 2];
            viewer.bgra_buffer[offset + 1] = viewer.rgba_buffer[offset + 1];
            viewer.bgra_buffer[offset + 2] = viewer.rgba_buffer[offset];
            viewer.bgra_buffer[offset + 3] = viewer.rgba_buffer[offset + 3];
        }

        viewer.source_width = meta.Width;
        viewer.source_height = meta.Height;
        viewer.stride = meta.Stride;
        viewer.has_frame = true;
        viewer.last_frame_index = meta.FrameIndex;
        viewer.last_time_sec = meta.Time;
        true
    }

    unsafe extern "system" fn window_proc(
        hwnd: HWND,
        msg: u32,
        wparam: WPARAM,
        lparam: LPARAM,
    ) -> LRESULT {
        match msg {
            WM_PAINT => {
                let mut paint = PAINTSTRUCT::default();
                let hdc = BeginPaint(hwnd, &mut paint);

                if let Some(viewer_state) = VIEWER_STATE.get() {
                    let viewer = viewer_state
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());

                    if viewer.has_frame && !viewer.bgra_buffer.is_empty() {
                        let mut info = BITMAPINFO::default();
                        info.bmiHeader = BITMAPINFOHEADER {
                            biSize: size_of::<BITMAPINFOHEADER>() as u32,
                            biWidth: viewer.source_width,
                            biHeight: -viewer.source_height,
                            biPlanes: 1,
                            biBitCount: 32,
                            biCompression: BI_RGB.0,
                            ..Default::default()
                        };

                        let mut rect = RECT::default();
                        let _ = GetClientRect(hwnd, &mut rect);
                        let dest_width = rect.right - rect.left;
                        let dest_height = rect.bottom - rect.top;

                        let _ = StretchDIBits(
                            hdc,
                            0,
                            0,
                            dest_width,
                            dest_height,
                            0,
                            0,
                            viewer.source_width,
                            viewer.source_height,
                            Some(viewer.bgra_buffer.as_ptr() as *const c_void),
                            &info,
                            DIB_RGB_COLORS,
                            SRCCOPY,
                        );
                    }
                }

                let _ = EndPaint(hwnd, &paint);
                LRESULT(0)
            }
            WM_CLOSE => {
                let _ = DestroyWindow(hwnd);
                LRESULT(0)
            }
            WM_DESTROY => {
                PostQuitMessage(0);
                LRESULT(0)
            }
            _ => DefWindowProcW(hwnd, msg, wparam, lparam),
        }
    }

    unsafe fn create_window(title: &str, width: i32, height: i32) -> Result<HWND, String> {
        let instance = GetModuleHandleW(None).map_err(|e| format!("GetModuleHandleW failed: {e}"))?;
        let class_name = to_wstring("RustAVTestPlayerWindow");
        let title_w = to_wstring(title);

        let cursor = LoadCursorW(None, IDC_ARROW).map_err(|e| format!("LoadCursorW failed: {e}"))?;
        let window_class = WNDCLASSW {
            style: CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc: Some(window_proc),
            hInstance: instance.into(),
            lpszClassName: PCWSTR(class_name.as_ptr()),
            hCursor: cursor,
            hbrBackground: HBRUSH(0),
            ..Default::default()
        };

        if RegisterClassW(&window_class) == 0 {
            return Err("RegisterClassW failed".to_string());
        }

        let mut rect = RECT {
            left: 0,
            top: 0,
            right: width,
            bottom: height,
        };
        let _ = AdjustWindowRectEx(&mut rect, WS_OVERLAPPEDWINDOW, false, WINDOW_EX_STYLE(0));

        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE(0),
            PCWSTR(class_name.as_ptr()),
            PCWSTR(title_w.as_ptr()),
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            rect.right - rect.left,
            rect.bottom - rect.top,
            HWND(0),
            HMENU(0),
            instance,
            None,
        );

        if hwnd.0 == 0 {
            return Err("CreateWindowExW failed".to_string());
        }

        ShowWindow(hwnd, SW_SHOW);
        let _ = UpdateWindow(hwnd);
        Ok(hwnd)
    }

    fn update_window_title(hwnd: HWND, viewer_state: &Arc<Mutex<ViewerState>>) {
        let viewer = viewer_state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());

        let title = format!(
            "{} | {}x{} | frame={} | t={:.3}s",
            viewer.title,
            viewer.source_width,
            viewer.source_height,
            viewer.last_frame_index,
            viewer.last_time_sec
        );
        let title_w = to_wstring(&title);

        unsafe {
            let _ = windows::Win32::UI::WindowsAndMessaging::SetWindowTextW(
                hwnd,
                PCWSTR(title_w.as_ptr()),
            );
        }
    }

    pub fn run() -> Result<(), String> {
        let config = parse_args()?;
        let title = format!("RustAV Test Player - {}", config.uri);

        Initialize(false);

        let (player, shared, audio_shared) =
            Player::CreateWithFrameAndAudioExport(config.uri.clone(), config.width, config.height)
                .ok_or_else(|| format!("创建播放器失败: {}", config.uri))?;

        let player = Arc::new(Mutex::new(player));
        let audio_output = WaveOutAudioOutput::new(audio_shared, player.clone());

        if config.loop_player {
            if let Ok(guard) = player.lock() {
                guard.SetLoop(true);
            }
        }
        if let Ok(guard) = player.lock() {
            guard.Play();
        }

        let viewer_state = Arc::new(Mutex::new(ViewerState::new(
            config.width,
            config.height,
            title.clone(),
        )));
        let _ = VIEWER_STATE.set(viewer_state.clone());

        let hwnd = unsafe { create_window(&title, config.width, config.height)? };
        let start = Instant::now();
        let mut quit = false;
        let mut last_title_update = Instant::now();

        while !quit {
            unsafe {
                let mut msg = MSG::default();
                while PeekMessageW(&mut msg, HWND(0), 0, 0, PM_REMOVE).into() {
                    if msg.message == WM_QUIT {
                        quit = true;
                        break;
                    }

                    let _ = TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            }

            if quit {
                break;
            }

            if pull_latest_frame(&shared, &viewer_state) {
                unsafe {
                    let _ = InvalidateRect(hwnd, None, false);
                }

                if last_title_update.elapsed() >= Duration::from_millis(250) {
                    update_window_title(hwnd, &viewer_state);
                    last_title_update = Instant::now();
                }
            }

            if let Some(max_seconds) = config.max_seconds {
                if max_seconds > 0.0 && start.elapsed().as_secs_f64() >= max_seconds {
                    unsafe {
                        let _ = DestroyWindow(hwnd);
                    }
                }
            }

            thread::sleep(Duration::from_millis(15));
        }

        drop(audio_output);
        drop(player);
        Teardown();
        Ok(())
    }
}

#[cfg(windows)]
fn main() {
    if let Err(error) = win32_player::run() {
        eprintln!("{error}");
        std::process::exit(1);
    }
}
