use crate::audio_export_state::SharedExportedAudioState;
use crate::av_lib_audio_decoder::AvLibAudioDecoder;
use crate::av_lib_decoder::AvLibDecoder;
use crate::av_lib_source::{AvLibSource, AvLibSourceConnectionState, AvLibStreamInfo};
use crate::frame_visitor::FrameVisitor;
use crate::logging::debug::Debug;
use crate::pixel_format::PixelFormat;
use crate::playback_clock::PlaybackClock;
use crate::video_client::VideoClient;
use crate::video_frame::VideoFrame;
use std::sync::{
    atomic::{AtomicBool, AtomicI32, AtomicU64, Ordering},
    Arc, Condvar, Mutex, Once,
};
use std::thread;
use std::time::{Duration, Instant};

static PROCESS_WIDE_INIT: Once = Once::new();

#[repr(i32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AvLibPlayerState {
    Idle = 0,
    Connecting = 1,
    Ready = 2,
    Playing = 3,
    Paused = 4,
    Shutdown = 5,
    Ended = 6,
}

#[repr(i32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AvLibPlayerPlaybackIntent {
    Stopped = 0,
    PlayRequested = 1,
}

#[repr(i32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AvLibPlayerStopReason {
    None = 0,
    UserStop = 1,
    EndOfStream = 2,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AvLibPlayerControlError {
    InvalidState,
    UnsupportedOperation,
}

#[derive(Clone, Copy, Debug)]
pub struct AvLibPlayerHealthSnapshot {
    pub state: i32,
    pub runtime_state: i32,
    pub playback_intent: i32,
    pub stop_reason: i32,
    pub source_connection_state: i32,
    pub is_connected: bool,
    pub is_playing: bool,
    pub is_realtime: bool,
    pub can_seek: bool,
    pub is_looping: bool,
    pub stream_count: i32,
    pub video_decoder_count: i32,
    pub has_audio_decoder: bool,
    pub duration_sec: f64,
    pub current_time_sec: f64,
    pub external_time_sec: f64,
    pub audio_time_sec: f64,
    pub audio_presented_time_sec: f64,
    pub audio_sink_delay_sec: f64,
    pub video_sync_compensation_sec: f64,
    pub connect_attempt_count: u64,
    pub video_decoder_recreate_count: u64,
    pub audio_decoder_recreate_count: u64,
    pub video_frame_drop_count: u64,
    pub audio_frame_drop_count: u64,
    pub source_packet_count: u64,
    pub source_timeout_count: u64,
    pub source_reconnect_count: u64,
    pub source_is_checking_connection: bool,
    pub source_last_activity_age_sec: f64,
}

pub struct AvLibPlayer {
    pub source: Arc<Mutex<Box<dyn AvLibSource + Send>>>,
    pub decoders: Arc<Mutex<Vec<Arc<AvLibDecoder>>>>,
    audio_decoder: Arc<Mutex<Option<Arc<AvLibAudioDecoder>>>>,
    audio_export: Option<SharedExportedAudioState>,
    video_client: Arc<Mutex<Box<dyn VideoClient + Send>>>,
    clock: Arc<Mutex<PlaybackClock>>,
    video_sync_compensation_sec: Arc<Mutex<f64>>,
    state: Arc<AtomicI32>,
    playing: Arc<AtomicBool>,
    stop_reason: Arc<AtomicI32>,
    looping: Arc<AtomicBool>,
    connect_attempt_count: Arc<AtomicU64>,
    video_decoder_recreate_count: Arc<AtomicU64>,
    audio_decoder_recreate_count: Arc<AtomicU64>,
    stay_alive: Arc<AtomicBool>,
    kill_mutex: Arc<Mutex<()>>,
    kill_condition: Arc<Condvar>,
    thread: Option<thread::JoinHandle<()>>,
}

impl AvLibPlayer {
    const CONNECT_RETRY_MILLISECONDS: u64 = 200;
    const REALTIME_POLL_MILLISECONDS: u64 = 5;
    const FILE_AUDIO_LEAD_SECONDS: f64 = 0.100;
    const REALTIME_AUDIO_LEAD_SECONDS: f64 = 0.750;
    const SYNC_LOG_INTERVAL_SECONDS: f64 = 1.0;
    const SYNC_WARN_THRESHOLD_SECONDS: f64 = 0.080;
    const REALTIME_SYNC_COMPENSATION_GAIN: f64 = 0.12;
    const REALTIME_SYNC_COMPENSATION_POSITIVE_MAX_SEC: f64 = 0.150;
    const REALTIME_SYNC_COMPENSATION_NEGATIVE_MAX_SEC: f64 = 0.300;
    const REALTIME_SYNC_COMPENSATION_SNAP_SEC: f64 = 0.120;
    const REALTIME_TIMELINE_RESET_THRESHOLD_SEC: f64 = 5.0;

    fn decode_state(raw: i32) -> AvLibPlayerState {
        match raw {
            x if x == AvLibPlayerState::Connecting as i32 => AvLibPlayerState::Connecting,
            x if x == AvLibPlayerState::Ready as i32 => AvLibPlayerState::Ready,
            x if x == AvLibPlayerState::Playing as i32 => AvLibPlayerState::Playing,
            x if x == AvLibPlayerState::Paused as i32 => AvLibPlayerState::Paused,
            x if x == AvLibPlayerState::Shutdown as i32 => AvLibPlayerState::Shutdown,
            x if x == AvLibPlayerState::Ended as i32 => AvLibPlayerState::Ended,
            _ => AvLibPlayerState::Idle,
        }
    }

    fn load_state(state: &Arc<AtomicI32>) -> AvLibPlayerState {
        Self::decode_state(state.load(Ordering::SeqCst))
    }

    fn store_state(state: &Arc<AtomicI32>, next: AvLibPlayerState) {
        if Self::load_state(state) != AvLibPlayerState::Shutdown {
            state.store(next as i32, Ordering::SeqCst);
        }
    }

    fn load_playback_intent(playing: &Arc<AtomicBool>) -> AvLibPlayerPlaybackIntent {
        if playing.load(Ordering::SeqCst) {
            AvLibPlayerPlaybackIntent::PlayRequested
        } else {
            AvLibPlayerPlaybackIntent::Stopped
        }
    }

    fn load_stop_reason(stop_reason: &Arc<AtomicI32>) -> AvLibPlayerStopReason {
        match stop_reason.load(Ordering::SeqCst) {
            x if x == AvLibPlayerStopReason::UserStop as i32 => AvLibPlayerStopReason::UserStop,
            x if x == AvLibPlayerStopReason::EndOfStream as i32 => {
                AvLibPlayerStopReason::EndOfStream
            }
            _ => AvLibPlayerStopReason::None,
        }
    }

    fn store_stop_reason(
        stop_reason: &Arc<AtomicI32>,
        next: AvLibPlayerStopReason,
        state: &Arc<AtomicI32>,
    ) {
        if Self::load_state(state) != AvLibPlayerState::Shutdown {
            stop_reason.store(next as i32, Ordering::SeqCst);
        }
    }

    fn resolve_public_state(
        runtime_state: AvLibPlayerState,
        playback_intent: AvLibPlayerPlaybackIntent,
        stop_reason: AvLibPlayerStopReason,
    ) -> AvLibPlayerState {
        match runtime_state {
            AvLibPlayerState::Shutdown => AvLibPlayerState::Shutdown,
            AvLibPlayerState::Connecting => AvLibPlayerState::Connecting,
            AvLibPlayerState::Idle => AvLibPlayerState::Idle,
            AvLibPlayerState::Ready | AvLibPlayerState::Paused | AvLibPlayerState::Playing => {
                if playback_intent == AvLibPlayerPlaybackIntent::PlayRequested {
                    AvLibPlayerState::Playing
                } else if stop_reason == AvLibPlayerStopReason::EndOfStream {
                    AvLibPlayerState::Ended
                } else if stop_reason == AvLibPlayerStopReason::UserStop {
                    AvLibPlayerState::Paused
                } else {
                    AvLibPlayerState::Ready
                }
            }
            AvLibPlayerState::Ended => AvLibPlayerState::Ended,
        }
    }

    fn can_accept_seek(runtime_state: AvLibPlayerState, can_seek: bool) -> bool {
        can_seek
            && matches!(
                runtime_state,
                AvLibPlayerState::Ready
                    | AvLibPlayerState::Paused
                    | AvLibPlayerState::Playing
                    | AvLibPlayerState::Ended
            )
    }

    fn reset_realtime_sync_compensation(compensation: &Arc<Mutex<f64>>) {
        if let Ok(mut guard) = compensation.lock() {
            *guard = 0.0;
        }
    }

    fn update_realtime_sync_compensation(
        compensation: &Arc<Mutex<f64>>,
        sync_error_sec: f64,
    ) -> f64 {
        let mut guard = compensation
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());

        let clamped_error = Self::clamp_realtime_sync_compensation(sync_error_sec);

        if clamped_error.abs() >= Self::REALTIME_SYNC_COMPENSATION_SNAP_SEC {
            *guard = Self::clamp_realtime_sync_compensation(*guard + clamped_error * 0.5);
        } else {
            *guard = Self::clamp_realtime_sync_compensation(
                *guard + clamped_error * Self::REALTIME_SYNC_COMPENSATION_GAIN,
            );
        }

        *guard
    }

    fn clamp_realtime_sync_compensation(value: f64) -> f64 {
        value.clamp(
            -Self::REALTIME_SYNC_COMPENSATION_NEGATIVE_MAX_SEC,
            Self::REALTIME_SYNC_COMPENSATION_POSITIVE_MAX_SEC,
        )
    }

    fn process_wide_initialize() {
        PROCESS_WIDE_INIT.call_once(|| {
            let _ = ffmpeg_next::init();
            unsafe {
                ffmpeg_next::ffi::av_log_set_level(ffmpeg_next::ffi::AV_LOG_VERBOSE);
            }
        });
    }

    fn sleep_time_from_decoders(decoders: &[Arc<AvLibDecoder>]) -> Duration {
        let mut min_frame_duration = f64::MAX;
        for decoder in decoders.iter() {
            let d = decoder.frame_duration();
            if d > 0.0 && d < min_frame_duration {
                min_frame_duration = d;
            }
        }

        if min_frame_duration.is_finite() && min_frame_duration > 0.0 {
            Duration::from_secs_f64((min_frame_duration * 0.5).max(0.001))
        } else {
            Duration::from_micros(50_000)
        }
    }

    fn realtime_sleep_time_from_decoders(decoders: &[Arc<AvLibDecoder>]) -> Duration {
        let mut min_frame_duration = f64::MAX;
        for decoder in decoders.iter() {
            let d = decoder.frame_duration();
            if d > 0.0 && d < min_frame_duration {
                min_frame_duration = d;
            }
        }

        if min_frame_duration.is_finite() && min_frame_duration > 0.0 {
            Duration::from_secs_f64((min_frame_duration * 0.25).clamp(0.001, 0.005))
        } else {
            Duration::from_millis(Self::REALTIME_POLL_MILLISECONDS)
        }
    }

    fn ensure_connection(
        source: &Arc<Mutex<Box<dyn AvLibSource + Send>>>,
        state: &Arc<AtomicI32>,
        connect_attempt_count: &Arc<AtomicU64>,
        stay_alive: &Arc<AtomicBool>,
        kill_mutex: &Arc<Mutex<()>>,
        kill_condition: &Arc<Condvar>,
    ) -> bool {
        while stay_alive.load(Ordering::SeqCst) {
            let connection_state = if let Ok(mut s) = source.lock() {
                let current_state = s.connection_state();
                if !current_state.is_connected() {
                    Self::store_state(state, AvLibPlayerState::Connecting);
                    if !matches!(
                        current_state,
                        AvLibSourceConnectionState::Connecting
                            | AvLibSourceConnectionState::Reconnecting
                    ) {
                        connect_attempt_count.fetch_add(1, Ordering::SeqCst);
                    }
                    s.connect();
                }
                s.connection_state()
            } else {
                AvLibSourceConnectionState::Disconnected
            };

            if connection_state.is_connected() {
                Self::store_state(state, AvLibPlayerState::Ready);
                return true;
            }

            if !Self::wait_or_interrupted(
                stay_alive,
                kill_mutex,
                kill_condition,
                Duration::from_millis(Self::CONNECT_RETRY_MILLISECONDS),
            ) {
                return false;
            }
        }

        false
    }

    fn wait_or_interrupted(
        stay_alive: &Arc<AtomicBool>,
        kill_mutex: &Arc<Mutex<()>>,
        kill_condition: &Arc<Condvar>,
        timeout: Duration,
    ) -> bool {
        if !stay_alive.load(Ordering::SeqCst) {
            return false;
        }

        let guard = match kill_mutex.lock() {
            Ok(g) => g,
            Err(poisoned) => poisoned.into_inner(),
        };
        let _ = kill_condition.wait_timeout(guard, timeout);

        stay_alive.load(Ordering::SeqCst)
    }

    fn pump_audio_frames(
        audio_decoder: &Arc<AvLibAudioDecoder>,
        clock: &Arc<Mutex<PlaybackClock>>,
        due_time_sec: f64,
        audio_export: &Option<SharedExportedAudioState>,
    ) {
        loop {
            let frame_opt = audio_decoder.try_get_next(due_time_sec);
            let Some(frame) = frame_opt else {
                break;
            };

            if frame.is_eof() {
                audio_decoder.recycle(frame);
                break;
            }

            let discontinuity = {
                let mut guard = clock
                    .lock()
                    .unwrap_or_else(|poisoned| poisoned.into_inner());
                guard.observe_audio_frame(frame.time(), frame.duration())
            };

            if discontinuity {
                Debug::log(&format!(
                    "[AvLibPlayer] audio_clock_reset pts={:.3} duration_ms={:.1}",
                    frame.time(),
                    frame.duration() * 1000.0
                ));
            }

            if let Some(shared) = audio_export {
                let mut export = shared
                    .lock()
                    .unwrap_or_else(|poisoned| poisoned.into_inner());
                export.push_frame(&frame);
            }

            audio_decoder.recycle(frame);
        }
    }

    pub fn new(
        source_box: Box<dyn AvLibSource + Send>,
        target_width: i32,
        target_height: i32,
        target_format: PixelFormat,
        video_client: Box<dyn VideoClient + Send>,
        audio_export: Option<SharedExportedAudioState>,
    ) -> Option<Self> {
        Self::process_wide_initialize();

        let source = Arc::new(Mutex::new(source_box));
        if let Ok(mut s) = source.lock() {
            s.connect();
        }
        let initial_state = if let Ok(s) = source.lock() {
            if s.is_connected() {
                AvLibPlayerState::Ready
            } else {
                AvLibPlayerState::Connecting
            }
        } else {
            AvLibPlayerState::Idle
        };

        let mut player = Self {
            source: source.clone(),
            decoders: Arc::new(Mutex::new(Vec::new())),
            audio_decoder: Arc::new(Mutex::new(None)),
            audio_export: audio_export.clone(),
            video_client: Arc::new(Mutex::new(video_client)),
            clock: Arc::new(Mutex::new(PlaybackClock::new())),
            video_sync_compensation_sec: Arc::new(Mutex::new(0.0)),
            state: Arc::new(AtomicI32::new(initial_state as i32)),
            playing: Arc::new(AtomicBool::new(false)),
            stop_reason: Arc::new(AtomicI32::new(AvLibPlayerStopReason::None as i32)),
            looping: Arc::new(AtomicBool::new(false)),
            connect_attempt_count: Arc::new(AtomicU64::new(0)),
            video_decoder_recreate_count: Arc::new(AtomicU64::new(0)),
            audio_decoder_recreate_count: Arc::new(AtomicU64::new(0)),
            stay_alive: Arc::new(AtomicBool::new(true)),
            kill_mutex: Arc::new(Mutex::new(())),
            kill_condition: Arc::new(Condvar::new()),
            thread: None,
        };

        struct TargetVideoDescription {
            width: i32,
            height: i32,
            format: PixelFormat,
        }

        impl crate::video_description::VideoDescription for TargetVideoDescription {
            fn width(&self) -> i32 {
                self.width
            }
            fn height(&self) -> i32 {
                self.height
            }
            fn format(&self) -> crate::pixel_format::PixelFormat {
                self.format
            }
        }

        let target_desc = TargetVideoDescription {
            width: target_width,
            height: target_height,
            format: target_format,
        };

        let t_source = player.source.clone();
        let t_stay_alive = player.stay_alive.clone();
        let t_playing = player.playing.clone();
        let t_clock = player.clock.clone();
        let t_decoders = player.decoders.clone();
        let t_audio_decoder = player.audio_decoder.clone();
        let t_audio_export = player.audio_export.clone();
        let t_video_sync_compensation = player.video_sync_compensation_sec.clone();
        let t_state = player.state.clone();
        let t_looping = player.looping.clone();
        let t_stop_reason = player.stop_reason.clone();
        let t_connect_attempt_count = player.connect_attempt_count.clone();
        let t_video_decoder_recreate_count = player.video_decoder_recreate_count.clone();
        let t_audio_decoder_recreate_count = player.audio_decoder_recreate_count.clone();
        let t_video_client = player.video_client.clone();
        let t_kill_mutex = player.kill_mutex.clone();
        let t_kill_condition = player.kill_condition.clone();
        let t_target_width = target_desc.width;
        let t_target_height = target_desc.height;
        let t_target_format = target_desc.format;

        player.thread = Some(thread::spawn(move || {
            struct TargetVideoDescription {
                width: i32,
                height: i32,
                format: PixelFormat,
            }

            impl crate::video_description::VideoDescription for TargetVideoDescription {
                fn width(&self) -> i32 {
                    self.width
                }
                fn height(&self) -> i32 {
                    self.height
                }
                fn format(&self) -> crate::pixel_format::PixelFormat {
                    self.format
                }
            }

            let target_desc = TargetVideoDescription {
                width: t_target_width,
                height: t_target_height,
                format: t_target_format,
            };

            let mut sleep_duration = Duration::from_millis(Self::REALTIME_POLL_MILLISECONDS);
            let mut last_sync_log = Instant::now();

            while t_stay_alive.load(Ordering::SeqCst) {
                let connected = Self::ensure_connection(
                    &t_source,
                    &t_state,
                    &t_connect_attempt_count,
                    &t_stay_alive,
                    &t_kill_mutex,
                    &t_kill_condition,
                );
                if !connected {
                    break;
                }

                let mut decoders_snapshot = {
                    if let Ok(decoders) = t_decoders.lock() {
                        decoders.clone()
                    } else {
                        Vec::new()
                    }
                };

                let should_recreate_video = decoders_snapshot
                    .iter()
                    .any(|decoder| decoder.needs_recreate());

                if decoders_snapshot.is_empty() || should_recreate_video {
                    if should_recreate_video {
                        t_video_decoder_recreate_count.fetch_add(1, Ordering::SeqCst);
                        Debug::log(
                            "[AvLibPlayer] recreate_video_decoders source stream shape changed",
                        );
                        for decoder in decoders_snapshot.iter() {
                            decoder.flush_frames();
                        }
                        Self::reset_realtime_sync_compensation(&t_video_sync_compensation);
                    }

                    let created = AvLibDecoder::create_video(t_source.clone(), &target_desc);
                    if created.is_empty() {
                        if !Self::wait_or_interrupted(
                            &t_stay_alive,
                            &t_kill_mutex,
                            &t_kill_condition,
                            Duration::from_millis(Self::CONNECT_RETRY_MILLISECONDS),
                        ) {
                            break;
                        }
                        continue;
                    }

                    if let Ok(mut decoders) = t_decoders.lock() {
                        *decoders = created;
                        decoders_snapshot = decoders.clone();
                    } else {
                        decoders_snapshot = created;
                    }

                    let is_realtime = if let Ok(s) = t_source.lock() {
                        s.is_realtime()
                    } else {
                        false
                    };
                    sleep_duration = if is_realtime {
                        Self::realtime_sleep_time_from_decoders(&decoders_snapshot)
                    } else {
                        Self::sleep_time_from_decoders(&decoders_snapshot)
                    };
                }

                let audio_decoder_snapshot = {
                    let mut guard = t_audio_decoder
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let should_recreate = guard
                        .as_ref()
                        .map(|decoder| decoder.needs_recreate())
                        .unwrap_or(false);

                    if should_recreate {
                        t_audio_decoder_recreate_count.fetch_add(1, Ordering::SeqCst);
                        Debug::log(
                            "[AvLibPlayer] recreate_audio_decoder source stream shape changed",
                        );
                        if let Some(audio_export) = t_audio_export.as_ref() {
                            let mut export = audio_export
                                .lock()
                                .unwrap_or_else(|poisoned| poisoned.into_inner());
                            export.flush();
                        }
                        if let Ok(mut clock) = t_clock.lock() {
                            clock.reset_audio_clock();
                        }
                        Self::reset_realtime_sync_compensation(&t_video_sync_compensation);
                    }

                    if guard.is_none() || should_recreate {
                        *guard = AvLibDecoder::create_audio(t_source.clone());
                    }
                    guard.clone()
                };

                if t_playing.load(Ordering::SeqCst) {
                    let (external_time, is_realtime) = {
                        let mut clock = t_clock
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner());
                        let _ = clock.tick_playing();
                        let realtime = if let Ok(s) = t_source.lock() {
                            s.is_realtime()
                        } else {
                            false
                        };
                        (clock.external_time(), realtime)
                    };

                    if let Some(audio_decoder) = audio_decoder_snapshot.as_ref() {
                        let audio_due_time = external_time
                            + if is_realtime {
                                Self::REALTIME_AUDIO_LEAD_SECONDS
                            } else {
                                Self::FILE_AUDIO_LEAD_SECONDS
                            };
                        Self::pump_audio_frames(
                            audio_decoder,
                            &t_clock,
                            audio_due_time,
                            &t_audio_export,
                        );
                    }

                    let current_time = {
                        let clock = t_clock
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner());
                        clock.master_time()
                    };
                    let mut effective_time = if is_realtime {
                        let compensation = t_video_sync_compensation
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner());
                        current_time + *compensation
                    } else {
                        current_time
                    };

                    let mut eof_hit = false;
                    for decoder in decoders_snapshot.iter() {
                        if let Some(mut frame) = decoder.try_get_next(effective_time) {
                            if frame.is_eof() {
                                eof_hit = true;
                            } else {
                                let mut sync_error_sec = frame.time() - effective_time;
                                if is_realtime
                                    && sync_error_sec.abs()
                                        >= Self::REALTIME_TIMELINE_RESET_THRESHOLD_SEC
                                {
                                    let previous_time = effective_time;
                                    if let Ok(mut clock) = t_clock.lock() {
                                        clock.seek(frame.time());
                                    }
                                    Self::reset_realtime_sync_compensation(
                                        &t_video_sync_compensation,
                                    );
                                    effective_time = frame.time();
                                    sync_error_sec = 0.0;
                                    Debug::log(&format!(
                                        "[AvLibPlayer] realtime_timeline_reset current={:.3} video_pts={:.3}",
                                        previous_time,
                                        frame.time()
                                    ));
                                }
                                let should_log_sync = last_sync_log.elapsed().as_secs_f64()
                                    >= Self::SYNC_LOG_INTERVAL_SECONDS;
                                let compensation_sec = if is_realtime {
                                    Self::update_realtime_sync_compensation(
                                        &t_video_sync_compensation,
                                        sync_error_sec,
                                    )
                                } else {
                                    0.0
                                };
                                if should_log_sync
                                    || sync_error_sec.abs() >= Self::SYNC_WARN_THRESHOLD_SECONDS
                                {
                                    Debug::log(&format!(
                                        "[AvLibPlayer] av_sync current={:.3} video_pts={:.3} delta_ms={:.1} comp_ms={:.1}",
                                        effective_time,
                                        frame.time(),
                                        sync_error_sec * 1000.0,
                                        compensation_sec * 1000.0
                                    ));
                                    last_sync_log = Instant::now();
                                }

                                struct VideoClientVisitor<'a> {
                                    client: &'a mut dyn VideoClient,
                                }

                                impl<'a> FrameVisitor for VideoClientVisitor<'a> {
                                    fn visit(&mut self, frame: &mut VideoFrame) {
                                        self.client.on_frame_ready(frame);
                                    }
                                }

                                match t_video_client.lock() {
                                    Ok(mut client) => {
                                        let client_ref: &mut dyn VideoClient = client.as_mut();
                                        let mut visitor = VideoClientVisitor { client: client_ref };
                                        frame.accept(&mut visitor);
                                    }
                                    Err(poisoned) => {
                                        let mut client = poisoned.into_inner();
                                        let client_ref: &mut dyn VideoClient = client.as_mut();
                                        let mut visitor = VideoClientVisitor { client: client_ref };
                                        frame.accept(&mut visitor);
                                    }
                                }
                                decoder.recycle(frame);
                            }
                        }
                    }

                    if eof_hit {
                        if t_looping.load(Ordering::SeqCst) {
                            let from = {
                                let clock = t_clock
                                    .lock()
                                    .unwrap_or_else(|poisoned| poisoned.into_inner());
                                clock.master_time()
                            };

                            if let Ok(mut s) = t_source.lock() {
                                s.seek(from, 0.0);
                            }

                            if let Ok(mut clock) = t_clock.lock() {
                                clock.seek(0.0);
                            }
                        } else {
                            t_playing.store(false, Ordering::SeqCst);
                            Self::store_stop_reason(
                                &t_stop_reason,
                                AvLibPlayerStopReason::EndOfStream,
                                &t_state,
                            );
                            if let Ok(mut clock) = t_clock.lock() {
                                clock.on_pause();
                            }
                        }
                    }
                } else if let Ok(mut clock) = t_clock.lock() {
                    clock.tick_paused();
                }

                if !Self::wait_or_interrupted(
                    &t_stay_alive,
                    &t_kill_mutex,
                    &t_kill_condition,
                    sleep_duration,
                ) {
                    break;
                }
            }
        }));

        Some(player)
    }

    pub fn write(&self) {
        match self.video_client.lock() {
            Ok(mut client) => client.write(),
            Err(poisoned) => poisoned.into_inner().write(),
        }
    }

    pub fn play(&self) {
        Self::store_stop_reason(&self.stop_reason, AvLibPlayerStopReason::None, &self.state);
        let was_playing = self.playing.swap(true, Ordering::SeqCst);
        if !was_playing {
            if self.is_realtime() {
                let decoders = if let Ok(decoders) = self.decoders.lock() {
                    decoders.clone()
                } else {
                    Vec::new()
                };

                for decoder in decoders.iter() {
                    decoder.flush_realtime_frames();
                }

                if let Ok(audio_guard) = self.audio_decoder.lock() {
                    if let Some(audio_decoder) = audio_guard.as_ref() {
                        audio_decoder.flush_frames();
                    }
                }

                if let Some(audio_export) = self.audio_export.as_ref() {
                    let mut guard = audio_export
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    guard.flush();
                }

                if let Ok(mut clock) = self.clock.lock() {
                    clock.reset_audio_clock();
                }
                Self::reset_realtime_sync_compensation(&self.video_sync_compensation_sec);
            }

            if let Ok(mut clock) = self.clock.lock() {
                clock.on_play();
            }
        }
    }

    pub fn stop(&self) {
        self.playing.store(false, Ordering::SeqCst);
        Self::store_stop_reason(
            &self.stop_reason,
            AvLibPlayerStopReason::UserStop,
            &self.state,
        );
        if let Ok(mut clock) = self.clock.lock() {
            clock.on_pause();
        }
    }

    pub fn can_seek(&self) -> bool {
        if let Ok(s) = self.source.lock() {
            s.can_seek()
        } else {
            false
        }
    }

    pub fn seek(&self, mut to: f64) -> Result<(), AvLibPlayerControlError> {
        let can_seek = self.can_seek();
        let runtime_state = Self::load_state(&self.state);
        if !Self::can_accept_seek(runtime_state, can_seek) {
            return if can_seek {
                Err(AvLibPlayerControlError::InvalidState)
            } else {
                Err(AvLibPlayerControlError::UnsupportedOperation)
            };
        }

        let duration = self.duration();
        if duration >= 0.0 && to > duration {
            to = duration;
        } else if to < 0.0 {
            to = 0.0;
        }

        let from = self.current_time();
        if let Ok(mut s) = self.source.lock() {
            s.seek(from, to);
        }

        if let Ok(mut clock) = self.clock.lock() {
            clock.seek(to);
        }
        Self::store_stop_reason(&self.stop_reason, AvLibPlayerStopReason::None, &self.state);
        Self::reset_realtime_sync_compensation(&self.video_sync_compensation_sec);

        if let Ok(decoders) = self.decoders.lock() {
            for decoder in decoders.iter() {
                decoder.flush_frames();
            }
        }

        if let Ok(audio_guard) = self.audio_decoder.lock() {
            if let Some(audio_decoder) = audio_guard.as_ref() {
                audio_decoder.flush_frames();
            }
        }

        if let Some(audio_export) = self.audio_export.as_ref() {
            let mut guard = audio_export
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            guard.flush();
        }

        Ok(())
    }

    pub fn can_loop(&self) -> bool {
        self.can_seek()
    }

    pub fn set_loop(&self, loop_value: bool) {
        if !self.can_loop() {
            return;
        }
        self.looping.store(loop_value, Ordering::SeqCst);
    }

    pub fn is_looping(&self) -> bool {
        self.looping.load(Ordering::SeqCst)
    }

    pub fn duration(&self) -> f64 {
        if let Ok(s) = self.source.lock() {
            s.duration()
        } else {
            -1.0
        }
    }

    pub fn current_time(&self) -> f64 {
        if let Ok(clock) = self.clock.lock() {
            clock.master_time()
        } else {
            0.0
        }
    }

    pub fn set_audio_sink_delay(&self, delay_sec: f64) {
        if let Ok(mut clock) = self.clock.lock() {
            clock.set_audio_sink_delay(delay_sec);
        }
    }

    pub fn health_snapshot(&self) -> AvLibPlayerHealthSnapshot {
        let (
            is_connected,
            stream_count,
            is_realtime,
            can_seek,
            duration_sec,
            source_packet_count,
            source_timeout_count,
            source_reconnect_count,
            source_is_checking_connection,
            source_connection_state,
            source_last_activity_age_sec,
        ) = if let Ok(source) = self.source.lock() {
            let source_stats = source.runtime_stats();
            (
                source_stats.connection_state.is_connected(),
                source.stream_count(),
                source.is_realtime(),
                source.can_seek(),
                source.duration(),
                source_stats.packet_count,
                source_stats.timeout_count,
                source_stats.reconnect_count,
                source_stats.is_checking_connection,
                source_stats.connection_state as i32,
                source_stats.last_activity_age_sec,
            )
        } else {
            (
                false,
                0,
                false,
                false,
                -1.0,
                0,
                0,
                0,
                false,
                AvLibSourceConnectionState::Disconnected as i32,
                -1.0,
            )
        };

        let video_decoder_count = self
            .decoders
            .lock()
            .map(|decoders| decoders.len() as i32)
            .unwrap_or(0);

        let has_audio_decoder = self
            .audio_decoder
            .lock()
            .map(|decoder| decoder.is_some())
            .unwrap_or(false);
        let audio_frame_drop_count = self
            .audio_decoder
            .lock()
            .map(|decoder| {
                decoder
                    .as_ref()
                    .map(|audio_decoder| audio_decoder.dropped_frame_count())
                    .unwrap_or(0)
            })
            .unwrap_or(0);
        let video_frame_drop_count = self
            .decoders
            .lock()
            .map(|decoders| {
                decoders
                    .iter()
                    .map(|decoder| decoder.dropped_frame_count())
                    .sum()
            })
            .unwrap_or(0);

        let (
            current_time_sec,
            external_time_sec,
            audio_time_sec,
            audio_presented_time_sec,
            audio_sink_delay_sec,
        ) = if let Ok(clock) = self.clock.lock() {
            (
                clock.master_time(),
                clock.external_time(),
                clock.audio_time().unwrap_or(-1.0),
                clock.audio_presented_time().unwrap_or(-1.0),
                clock.audio_sink_delay(),
            )
        } else {
            (0.0, 0.0, -1.0, -1.0, 0.0)
        };

        let video_sync_compensation_sec = self
            .video_sync_compensation_sec
            .lock()
            .map(|value| *value)
            .unwrap_or(0.0);

        let runtime_state = Self::load_state(&self.state);
        let playback_intent = Self::load_playback_intent(&self.playing);
        let stop_reason = Self::load_stop_reason(&self.stop_reason);
        let public_state = Self::resolve_public_state(runtime_state, playback_intent, stop_reason);

        AvLibPlayerHealthSnapshot {
            state: public_state as i32,
            runtime_state: runtime_state as i32,
            playback_intent: playback_intent as i32,
            stop_reason: stop_reason as i32,
            source_connection_state,
            is_connected,
            is_playing: self.is_playing(),
            is_realtime,
            can_seek,
            is_looping: self.is_looping(),
            stream_count,
            video_decoder_count,
            has_audio_decoder,
            duration_sec,
            current_time_sec,
            external_time_sec,
            audio_time_sec,
            audio_presented_time_sec,
            audio_sink_delay_sec,
            video_sync_compensation_sec,
            connect_attempt_count: self.connect_attempt_count.load(Ordering::SeqCst),
            video_decoder_recreate_count: self.video_decoder_recreate_count.load(Ordering::SeqCst),
            audio_decoder_recreate_count: self.audio_decoder_recreate_count.load(Ordering::SeqCst),
            video_frame_drop_count,
            audio_frame_drop_count,
            source_packet_count,
            source_timeout_count,
            source_reconnect_count,
            source_is_checking_connection,
            source_last_activity_age_sec,
        }
    }

    pub fn stream_info(&self, stream_index: i32) -> Option<AvLibStreamInfo> {
        let Ok(source) = self.source.lock() else {
            return None;
        };

        if stream_index < 0 || stream_index >= source.stream_count() {
            return None;
        }

        let mut info = source.stream(stream_index);
        drop(source);

        if info.codec_type == ffmpeg_next::ffi::AVMediaType::AVMEDIA_TYPE_VIDEO
            && (info.width <= 0 || info.height <= 0)
        {
            if let Ok(decoders) = self.decoders.lock() {
                for decoder in decoders.iter() {
                    if decoder.stream_index == stream_index {
                        if let Some((width, height)) = decoder.actual_source_video_size() {
                            info.width = width;
                            info.height = height;
                            break;
                        }
                    }
                }
            }
        }

        Some(info)
    }

    pub fn is_playing(&self) -> bool {
        self.playing.load(Ordering::SeqCst)
    }

    pub fn is_realtime(&self) -> bool {
        if let Ok(s) = self.source.lock() {
            s.is_realtime()
        } else {
            false
        }
    }
}

impl Drop for AvLibPlayer {
    fn drop(&mut self) {
        self.state
            .store(AvLibPlayerState::Shutdown as i32, Ordering::SeqCst);
        self.stay_alive.store(false, Ordering::SeqCst);
        self.kill_condition.notify_all();
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{AvLibPlayer, AvLibPlayerPlaybackIntent, AvLibPlayerState, AvLibPlayerStopReason};
    use std::sync::{
        atomic::{AtomicI32, Ordering},
        Arc, Mutex,
    };

    #[test]
    fn store_state_does_not_override_shutdown() {
        let state = Arc::new(AtomicI32::new(AvLibPlayerState::Shutdown as i32));
        AvLibPlayer::store_state(&state, AvLibPlayerState::Ready);
        assert_eq!(
            state.load(Ordering::SeqCst),
            AvLibPlayerState::Shutdown as i32
        );
    }

    #[test]
    fn resolve_public_state_returns_ended_after_eof() {
        let resolved = AvLibPlayer::resolve_public_state(
            AvLibPlayerState::Ready,
            AvLibPlayerPlaybackIntent::Stopped,
            AvLibPlayerStopReason::EndOfStream,
        );
        assert_eq!(resolved, AvLibPlayerState::Ended);
    }

    #[test]
    fn resolve_public_state_returns_paused_after_user_stop() {
        let resolved = AvLibPlayer::resolve_public_state(
            AvLibPlayerState::Ready,
            AvLibPlayerPlaybackIntent::Stopped,
            AvLibPlayerStopReason::UserStop,
        );
        assert_eq!(resolved, AvLibPlayerState::Paused);
    }

    #[test]
    fn resolve_public_state_returns_playing_while_play_requested() {
        let resolved = AvLibPlayer::resolve_public_state(
            AvLibPlayerState::Ready,
            AvLibPlayerPlaybackIntent::PlayRequested,
            AvLibPlayerStopReason::None,
        );
        assert_eq!(resolved, AvLibPlayerState::Playing);
    }

    #[test]
    fn can_accept_seek_allows_ready_and_ended_states() {
        assert!(AvLibPlayer::can_accept_seek(AvLibPlayerState::Ready, true));
        assert!(AvLibPlayer::can_accept_seek(AvLibPlayerState::Ended, true));
        assert!(!AvLibPlayer::can_accept_seek(
            AvLibPlayerState::Connecting,
            true
        ));
        assert!(!AvLibPlayer::can_accept_seek(
            AvLibPlayerState::Ready,
            false
        ));
    }

    #[test]
    fn update_realtime_sync_compensation_allows_more_negative_headroom_for_realtime_audio_lead() {
        let compensation = Arc::new(Mutex::new(0.0));
        for _ in 0..8 {
            AvLibPlayer::update_realtime_sync_compensation(&compensation, -0.300);
        }

        let value = *compensation
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        assert!((value + 0.300).abs() < 0.0001);
    }

    #[test]
    fn update_realtime_sync_compensation_keeps_positive_headroom_conservative() {
        let compensation = Arc::new(Mutex::new(0.0));
        for _ in 0..8 {
            AvLibPlayer::update_realtime_sync_compensation(&compensation, 0.300);
        }

        let value = *compensation
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        assert!((value - 0.150).abs() < 0.0001);
    }
}
