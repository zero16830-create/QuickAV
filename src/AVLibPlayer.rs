#![allow(non_snake_case)]

use crate::AudioExportState::SharedExportedAudioState;
use crate::AVLibAudioDecoder::AVLibAudioDecoder;
use crate::AVLibDecoder::AVLibDecoder;
use crate::IAVLibSource::{AVLibSourceConnectionState, AVLibStreamInfo, IAVLibSource};
use crate::IFrameVisitor::IFrameVisitor;
use crate::IVideoClient::IVideoClient;
use crate::Logging::Debug::Debug;
use crate::PixelFormat::PixelFormat;
use crate::PlaybackClock::PlaybackClock;
use crate::VideoFrame::VideoFrame;
use std::sync::{
    atomic::{AtomicBool, AtomicI32, AtomicU64, Ordering},
    Arc, Condvar, Mutex, Once,
};
use std::thread;
use std::time::{Duration, Instant};

static PROCESS_WIDE_INIT: Once = Once::new();

#[repr(i32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AVLibPlayerState {
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
pub enum AVLibPlayerPlaybackIntent {
    Stopped = 0,
    PlayRequested = 1,
}

#[repr(i32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AVLibPlayerStopReason {
    None = 0,
    UserStop = 1,
    EndOfStream = 2,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AVLibPlayerControlError {
    InvalidState,
    UnsupportedOperation,
}

#[derive(Clone, Copy, Debug)]
pub struct AVLibPlayerHealthSnapshot {
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

pub struct AVLibPlayer {
    pub _source: Arc<Mutex<Box<dyn IAVLibSource + Send>>>,
    pub _decoders: Arc<Mutex<Vec<Arc<AVLibDecoder>>>>,
    _audio_decoder: Arc<Mutex<Option<Arc<AVLibAudioDecoder>>>>,
    _audio_export: Option<SharedExportedAudioState>,
    _video_client: Arc<Mutex<Box<dyn IVideoClient + Send>>>,
    _clock: Arc<Mutex<PlaybackClock>>,
    _videoSyncCompensationSec: Arc<Mutex<f64>>,
    _state: Arc<AtomicI32>,
    _playing: Arc<AtomicBool>,
    _stopReason: Arc<AtomicI32>,
    _looping: Arc<AtomicBool>,
    _connectAttemptCount: Arc<AtomicU64>,
    _videoDecoderRecreateCount: Arc<AtomicU64>,
    _audioDecoderRecreateCount: Arc<AtomicU64>,
    _stayAlive: Arc<AtomicBool>,
    _killMutex: Arc<Mutex<()>>,
    _killCondition: Arc<Condvar>,
    _thread: Option<thread::JoinHandle<()>>,
}

impl AVLibPlayer {
    const CONNECT_RETRY_MILLISECONDS: u64 = 200;
    const REALTIME_POLL_MILLISECONDS: u64 = 5;
    const FILE_AUDIO_LEAD_SECONDS: f64 = 0.100;
    const REALTIME_AUDIO_LEAD_SECONDS: f64 = 0.750;
    const SYNC_LOG_INTERVAL_SECONDS: f64 = 1.0;
    const SYNC_WARN_THRESHOLD_SECONDS: f64 = 0.080;
    const REALTIME_SYNC_COMPENSATION_GAIN: f64 = 0.12;
    const REALTIME_SYNC_COMPENSATION_MAX_SEC: f64 = 0.150;
    const REALTIME_SYNC_COMPENSATION_SNAP_SEC: f64 = 0.120;

    fn DecodeState(raw: i32) -> AVLibPlayerState {
        match raw {
            x if x == AVLibPlayerState::Connecting as i32 => AVLibPlayerState::Connecting,
            x if x == AVLibPlayerState::Ready as i32 => AVLibPlayerState::Ready,
            x if x == AVLibPlayerState::Playing as i32 => AVLibPlayerState::Playing,
            x if x == AVLibPlayerState::Paused as i32 => AVLibPlayerState::Paused,
            x if x == AVLibPlayerState::Shutdown as i32 => AVLibPlayerState::Shutdown,
            x if x == AVLibPlayerState::Ended as i32 => AVLibPlayerState::Ended,
            _ => AVLibPlayerState::Idle,
        }
    }

    fn LoadState(state: &Arc<AtomicI32>) -> AVLibPlayerState {
        Self::DecodeState(state.load(Ordering::SeqCst))
    }

    fn StoreState(state: &Arc<AtomicI32>, next: AVLibPlayerState) {
        if Self::LoadState(state) != AVLibPlayerState::Shutdown {
            state.store(next as i32, Ordering::SeqCst);
        }
    }

    fn LoadPlaybackIntent(playing: &Arc<AtomicBool>) -> AVLibPlayerPlaybackIntent {
        if playing.load(Ordering::SeqCst) {
            AVLibPlayerPlaybackIntent::PlayRequested
        } else {
            AVLibPlayerPlaybackIntent::Stopped
        }
    }

    fn LoadStopReason(stop_reason: &Arc<AtomicI32>) -> AVLibPlayerStopReason {
        match stop_reason.load(Ordering::SeqCst) {
            x if x == AVLibPlayerStopReason::UserStop as i32 => AVLibPlayerStopReason::UserStop,
            x if x == AVLibPlayerStopReason::EndOfStream as i32 => {
                AVLibPlayerStopReason::EndOfStream
            }
            _ => AVLibPlayerStopReason::None,
        }
    }

    fn StoreStopReason(
        stop_reason: &Arc<AtomicI32>,
        next: AVLibPlayerStopReason,
        state: &Arc<AtomicI32>,
    ) {
        if Self::LoadState(state) != AVLibPlayerState::Shutdown {
            stop_reason.store(next as i32, Ordering::SeqCst);
        }
    }

    fn ResolvePublicState(
        runtime_state: AVLibPlayerState,
        playback_intent: AVLibPlayerPlaybackIntent,
        stop_reason: AVLibPlayerStopReason,
    ) -> AVLibPlayerState {
        match runtime_state {
            AVLibPlayerState::Shutdown => AVLibPlayerState::Shutdown,
            AVLibPlayerState::Connecting => AVLibPlayerState::Connecting,
            AVLibPlayerState::Idle => AVLibPlayerState::Idle,
            AVLibPlayerState::Ready | AVLibPlayerState::Paused | AVLibPlayerState::Playing => {
                if playback_intent == AVLibPlayerPlaybackIntent::PlayRequested {
                    AVLibPlayerState::Playing
                } else if stop_reason == AVLibPlayerStopReason::EndOfStream {
                    AVLibPlayerState::Ended
                } else if stop_reason == AVLibPlayerStopReason::UserStop {
                    AVLibPlayerState::Paused
                } else {
                    AVLibPlayerState::Ready
                }
            }
            AVLibPlayerState::Ended => AVLibPlayerState::Ended,
        }
    }

    fn CanAcceptSeek(runtime_state: AVLibPlayerState, can_seek: bool) -> bool {
        can_seek && matches!(runtime_state, AVLibPlayerState::Ready | AVLibPlayerState::Paused | AVLibPlayerState::Playing | AVLibPlayerState::Ended)
    }

    fn ResetRealtimeSyncCompensation(compensation: &Arc<Mutex<f64>>) {
        if let Ok(mut guard) = compensation.lock() {
            *guard = 0.0;
        }
    }

    fn UpdateRealtimeSyncCompensation(
        compensation: &Arc<Mutex<f64>>,
        sync_error_sec: f64,
    ) -> f64 {
        let mut guard = compensation
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());

        let clamped_error = sync_error_sec.clamp(
            -Self::REALTIME_SYNC_COMPENSATION_MAX_SEC,
            Self::REALTIME_SYNC_COMPENSATION_MAX_SEC,
        );

        if clamped_error.abs() >= Self::REALTIME_SYNC_COMPENSATION_SNAP_SEC {
            *guard = (*guard + clamped_error * 0.5).clamp(
                -Self::REALTIME_SYNC_COMPENSATION_MAX_SEC,
                Self::REALTIME_SYNC_COMPENSATION_MAX_SEC,
            );
        } else {
            *guard = (*guard + clamped_error * Self::REALTIME_SYNC_COMPENSATION_GAIN).clamp(
                -Self::REALTIME_SYNC_COMPENSATION_MAX_SEC,
                Self::REALTIME_SYNC_COMPENSATION_MAX_SEC,
            );
        }

        *guard
    }

    fn ProcessWideInitialize() {
        PROCESS_WIDE_INIT.call_once(|| {
            let _ = ffmpeg_next::init();
            unsafe {
                ffmpeg_next::ffi::av_log_set_level(ffmpeg_next::ffi::AV_LOG_VERBOSE);
            }
        });
    }

    fn SleepTimeFromDecoders(decoders: &[Arc<AVLibDecoder>]) -> Duration {
        let mut min_frame_duration = f64::MAX;
        for decoder in decoders.iter() {
            let d = decoder.GetFrameDuration();
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

    fn RealtimeSleepTimeFromDecoders(decoders: &[Arc<AVLibDecoder>]) -> Duration {
        let mut min_frame_duration = f64::MAX;
        for decoder in decoders.iter() {
            let d = decoder.GetFrameDuration();
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

    fn EnsureConnection(
        source: &Arc<Mutex<Box<dyn IAVLibSource + Send>>>,
        state: &Arc<AtomicI32>,
        connect_attempt_count: &Arc<AtomicU64>,
        stay_alive: &Arc<AtomicBool>,
        kill_mutex: &Arc<Mutex<()>>,
        kill_condition: &Arc<Condvar>,
    ) -> bool {
        while stay_alive.load(Ordering::SeqCst) {
            let connection_state = if let Ok(mut s) = source.lock() {
                let current_state = s.ConnectionState();
                if !current_state.IsConnected() {
                    Self::StoreState(state, AVLibPlayerState::Connecting);
                    if !matches!(
                        current_state,
                        AVLibSourceConnectionState::Connecting
                            | AVLibSourceConnectionState::Reconnecting
                    ) {
                        connect_attempt_count.fetch_add(1, Ordering::SeqCst);
                    }
                    s.Connect();
                }
                s.ConnectionState()
            } else {
                AVLibSourceConnectionState::Disconnected
            };

            if connection_state.IsConnected() {
                Self::StoreState(state, AVLibPlayerState::Ready);
                return true;
            }

            if !Self::WaitOrInterrupted(
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

    fn WaitOrInterrupted(
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

    fn PumpAudioFrames(
        audio_decoder: &Arc<AVLibAudioDecoder>,
        clock: &Arc<Mutex<PlaybackClock>>,
        due_time_sec: f64,
        audio_export: &Option<SharedExportedAudioState>,
    ) {
        loop {
            let frame_opt = audio_decoder.TryGetNext(due_time_sec);
            let Some(frame) = frame_opt else {
                break;
            };

            if frame.IsEOF() {
                audio_decoder.Recycle(frame);
                break;
            }

            let discontinuity = {
                let mut guard = clock.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                guard.ObserveAudioFrame(frame.Time(), frame.Duration())
            };

            if discontinuity {
                Debug::Log(&format!(
                    "[AVLibPlayer] audio_clock_reset pts={:.3} duration_ms={:.1}",
                    frame.Time(),
                    frame.Duration() * 1000.0
                ));
            }

            if let Some(shared) = audio_export {
                let mut export = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                export.PushFrame(&frame);
            }

            audio_decoder.Recycle(frame);
        }
    }

    pub fn new(
        source_box: Box<dyn IAVLibSource + Send>,
        target_width: i32,
        target_height: i32,
        target_format: PixelFormat,
        video_client: Box<dyn IVideoClient + Send>,
        audio_export: Option<SharedExportedAudioState>,
    ) -> Option<Self> {
        Self::ProcessWideInitialize();

        let source = Arc::new(Mutex::new(source_box));
        if let Ok(mut s) = source.lock() {
            s.Connect();
        }
        let initial_state = if let Ok(s) = source.lock() {
            if s.IsConnected() {
                AVLibPlayerState::Ready
            } else {
                AVLibPlayerState::Connecting
            }
        } else {
            AVLibPlayerState::Idle
        };

        let mut player = Self {
            _source: source.clone(),
            _decoders: Arc::new(Mutex::new(Vec::new())),
            _audio_decoder: Arc::new(Mutex::new(None)),
            _audio_export: audio_export.clone(),
            _video_client: Arc::new(Mutex::new(video_client)),
            _clock: Arc::new(Mutex::new(PlaybackClock::new())),
            _videoSyncCompensationSec: Arc::new(Mutex::new(0.0)),
            _state: Arc::new(AtomicI32::new(initial_state as i32)),
            _playing: Arc::new(AtomicBool::new(false)),
            _stopReason: Arc::new(AtomicI32::new(AVLibPlayerStopReason::None as i32)),
            _looping: Arc::new(AtomicBool::new(false)),
            _connectAttemptCount: Arc::new(AtomicU64::new(0)),
            _videoDecoderRecreateCount: Arc::new(AtomicU64::new(0)),
            _audioDecoderRecreateCount: Arc::new(AtomicU64::new(0)),
            _stayAlive: Arc::new(AtomicBool::new(true)),
            _killMutex: Arc::new(Mutex::new(())),
            _killCondition: Arc::new(Condvar::new()),
            _thread: None,
        };

        struct TargetVideoDescription {
            width: i32,
            height: i32,
            format: PixelFormat,
        }

        impl crate::IVideoDescription::IVideoDescription for TargetVideoDescription {
            fn Width(&self) -> i32 {
                self.width
            }
            fn Height(&self) -> i32 {
                self.height
            }
            fn Format(&self) -> crate::PixelFormat::PixelFormat {
                self.format
            }
        }

        let target_desc = TargetVideoDescription {
            width: target_width,
            height: target_height,
            format: target_format,
        };

        let t_source = player._source.clone();
        let t_stay_alive = player._stayAlive.clone();
        let t_playing = player._playing.clone();
        let t_clock = player._clock.clone();
        let t_decoders = player._decoders.clone();
        let t_audio_decoder = player._audio_decoder.clone();
        let t_audio_export = player._audio_export.clone();
        let t_video_sync_compensation = player._videoSyncCompensationSec.clone();
        let t_state = player._state.clone();
        let t_looping = player._looping.clone();
        let t_stop_reason = player._stopReason.clone();
        let t_connect_attempt_count = player._connectAttemptCount.clone();
        let t_video_decoder_recreate_count = player._videoDecoderRecreateCount.clone();
        let t_audio_decoder_recreate_count = player._audioDecoderRecreateCount.clone();
        let t_video_client = player._video_client.clone();
        let t_kill_mutex = player._killMutex.clone();
        let t_kill_condition = player._killCondition.clone();
        let t_target_width = target_desc.width;
        let t_target_height = target_desc.height;
        let t_target_format = target_desc.format;

        player._thread = Some(thread::spawn(move || {
            struct TargetVideoDescription {
                width: i32,
                height: i32,
                format: PixelFormat,
            }

            impl crate::IVideoDescription::IVideoDescription for TargetVideoDescription {
                fn Width(&self) -> i32 {
                    self.width
                }
                fn Height(&self) -> i32 {
                    self.height
                }
                fn Format(&self) -> crate::PixelFormat::PixelFormat {
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
                let connected = Self::EnsureConnection(
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

                let should_recreate_video =
                    decoders_snapshot.iter().any(|decoder| decoder.NeedsRecreate());

                if decoders_snapshot.is_empty() || should_recreate_video {
                    if should_recreate_video {
                        t_video_decoder_recreate_count.fetch_add(1, Ordering::SeqCst);
                        Debug::Log(
                            "[AVLibPlayer] recreate_video_decoders source stream shape changed",
                        );
                        for decoder in decoders_snapshot.iter() {
                            decoder.FlushFrames();
                        }
                        Self::ResetRealtimeSyncCompensation(&t_video_sync_compensation);
                    }

                    let created = AVLibDecoder::CreateVideo(t_source.clone(), &target_desc);
                    if created.is_empty() {
                        if !Self::WaitOrInterrupted(
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
                        s.IsRealtime()
                    } else {
                        false
                    };
                    sleep_duration = if is_realtime {
                        Self::RealtimeSleepTimeFromDecoders(&decoders_snapshot)
                    } else {
                        Self::SleepTimeFromDecoders(&decoders_snapshot)
                    };
                }

                let audio_decoder_snapshot = {
                    let mut guard = t_audio_decoder
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let should_recreate = guard
                        .as_ref()
                        .map(|decoder| decoder.NeedsRecreate())
                        .unwrap_or(false);

                    if should_recreate {
                        t_audio_decoder_recreate_count.fetch_add(1, Ordering::SeqCst);
                        Debug::Log(
                            "[AVLibPlayer] recreate_audio_decoder source stream shape changed",
                        );
                        if let Some(audio_export) = t_audio_export.as_ref() {
                            let mut export = audio_export
                                .lock()
                                .unwrap_or_else(|poisoned| poisoned.into_inner());
                            export.Flush();
                        }
                        if let Ok(mut clock) = t_clock.lock() {
                            clock.ResetAudioClock();
                        }
                        Self::ResetRealtimeSyncCompensation(&t_video_sync_compensation);
                    }

                    if guard.is_none() || should_recreate {
                        *guard = AVLibDecoder::CreateAudio(t_source.clone());
                    }
                    guard.clone()
                };

                if t_playing.load(Ordering::SeqCst) {
                    let (external_time, is_realtime) = {
                        let mut clock = t_clock.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                        let _ = clock.TickPlaying();
                        let realtime = if let Ok(s) = t_source.lock() {
                            s.IsRealtime()
                        } else {
                            false
                        };
                        (clock.ExternalTime(), realtime)
                    };

                    if let Some(audio_decoder) = audio_decoder_snapshot.as_ref() {
                        let audio_due_time = external_time
                            + if is_realtime {
                                Self::REALTIME_AUDIO_LEAD_SECONDS
                            } else {
                                Self::FILE_AUDIO_LEAD_SECONDS
                            };
                        Self::PumpAudioFrames(
                            audio_decoder,
                            &t_clock,
                            audio_due_time,
                            &t_audio_export,
                        );
                    }

                    let current_time = {
                        let clock = t_clock.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                        clock.MasterTime()
                    };
                    let effective_time = if is_realtime {
                        let compensation = t_video_sync_compensation
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner());
                        current_time + *compensation
                    } else {
                        current_time
                    };

                    let mut eof_hit = false;
                    for decoder in decoders_snapshot.iter() {
                        if let Some(mut frame) = decoder.TryGetNext(effective_time) {
                            if frame.IsEOF() {
                                eof_hit = true;
                            } else {
                                let should_log_sync = last_sync_log.elapsed().as_secs_f64()
                                    >= Self::SYNC_LOG_INTERVAL_SECONDS;
                                let sync_error_sec = frame.Time() - effective_time;
                                let compensation_sec = if is_realtime {
                                    Self::UpdateRealtimeSyncCompensation(
                                        &t_video_sync_compensation,
                                        sync_error_sec,
                                    )
                                } else {
                                    0.0
                                };
                                if should_log_sync
                                    || sync_error_sec.abs() >= Self::SYNC_WARN_THRESHOLD_SECONDS
                                {
                                    Debug::Log(&format!(
                                        "[AVLibPlayer] av_sync current={:.3} video_pts={:.3} delta_ms={:.1} comp_ms={:.1}",
                                        effective_time,
                                        frame.Time(),
                                        sync_error_sec * 1000.0,
                                        compensation_sec * 1000.0
                                    ));
                                    last_sync_log = Instant::now();
                                }

                                struct VideoClientVisitor<'a> {
                                    client: &'a mut dyn IVideoClient,
                                }

                                impl<'a> IFrameVisitor for VideoClientVisitor<'a> {
                                    fn Visit(&mut self, frame: &mut VideoFrame) {
                                        self.client.OnFrameReady(frame);
                                    }
                                }

                                match t_video_client.lock() {
                                    Ok(mut client) => {
                                        let client_ref: &mut dyn IVideoClient = client.as_mut();
                                        let mut visitor = VideoClientVisitor { client: client_ref };
                                        frame.Accept(&mut visitor);
                                    }
                                    Err(poisoned) => {
                                        let mut client = poisoned.into_inner();
                                        let client_ref: &mut dyn IVideoClient = client.as_mut();
                                        let mut visitor = VideoClientVisitor { client: client_ref };
                                        frame.Accept(&mut visitor);
                                    }
                                }
                                decoder.Recycle(frame);
                            }
                        }
                    }

                    if eof_hit {
                        if t_looping.load(Ordering::SeqCst) {
                            let from = {
                                let clock = t_clock.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                                clock.MasterTime()
                            };

                            if let Ok(mut s) = t_source.lock() {
                                s.Seek(from, 0.0);
                            }

                            if let Ok(mut clock) = t_clock.lock() {
                                clock.Seek(0.0);
                            }
                        } else {
                            t_playing.store(false, Ordering::SeqCst);
                            Self::StoreStopReason(
                                &t_stop_reason,
                                AVLibPlayerStopReason::EndOfStream,
                                &t_state,
                            );
                            if let Ok(mut clock) = t_clock.lock() {
                                clock.OnPause();
                            }
                        }
                    }
                } else if let Ok(mut clock) = t_clock.lock() {
                    clock.TickPaused();
                }

                if !Self::WaitOrInterrupted(
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

    pub fn Write(&self) {
        match self._video_client.lock() {
            Ok(mut client) => client.Write(),
            Err(poisoned) => poisoned.into_inner().Write(),
        }
    }

    pub fn Play(&self) {
        Self::StoreStopReason(
            &self._stopReason,
            AVLibPlayerStopReason::None,
            &self._state,
        );
        let was_playing = self._playing.swap(true, Ordering::SeqCst);
        if !was_playing {
            if self.IsRealtime() {
                let decoders = if let Ok(decoders) = self._decoders.lock() {
                    decoders.clone()
                } else {
                    Vec::new()
                };

                for decoder in decoders.iter() {
                    decoder.FlushRealtimeFrames();
                }

                if let Ok(audio_guard) = self._audio_decoder.lock() {
                    if let Some(audio_decoder) = audio_guard.as_ref() {
                        audio_decoder.FlushFrames();
                    }
                }

                if let Some(audio_export) = self._audio_export.as_ref() {
                    let mut guard = audio_export
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    guard.Flush();
                }

                if let Ok(mut clock) = self._clock.lock() {
                    clock.ResetAudioClock();
                }
                Self::ResetRealtimeSyncCompensation(&self._videoSyncCompensationSec);
            }

            if let Ok(mut clock) = self._clock.lock() {
                clock.OnPlay();
            }
        }
    }

    pub fn Stop(&self) {
        self._playing.store(false, Ordering::SeqCst);
        Self::StoreStopReason(
            &self._stopReason,
            AVLibPlayerStopReason::UserStop,
            &self._state,
        );
        if let Ok(mut clock) = self._clock.lock() {
            clock.OnPause();
        }
    }

    pub fn CanSeek(&self) -> bool {
        if let Ok(s) = self._source.lock() {
            s.CanSeek()
        } else {
            false
        }
    }

    pub fn Seek(&self, mut to: f64) -> Result<(), AVLibPlayerControlError> {
        let can_seek = self.CanSeek();
        let runtime_state = Self::LoadState(&self._state);
        if !Self::CanAcceptSeek(runtime_state, can_seek) {
            return if can_seek {
                Err(AVLibPlayerControlError::InvalidState)
            } else {
                Err(AVLibPlayerControlError::UnsupportedOperation)
            };
        }

        let duration = self.Duration();
        if duration >= 0.0 && to > duration {
            to = duration;
        } else if to < 0.0 {
            to = 0.0;
        }

        let from = self.CurrentTime();
        if let Ok(mut s) = self._source.lock() {
            s.Seek(from, to);
        }

        if let Ok(mut clock) = self._clock.lock() {
            clock.Seek(to);
        }
        Self::StoreStopReason(
            &self._stopReason,
            AVLibPlayerStopReason::None,
            &self._state,
        );
        Self::ResetRealtimeSyncCompensation(&self._videoSyncCompensationSec);

        if let Ok(decoders) = self._decoders.lock() {
            for decoder in decoders.iter() {
                decoder.FlushFrames();
            }
        }

        if let Ok(audio_guard) = self._audio_decoder.lock() {
            if let Some(audio_decoder) = audio_guard.as_ref() {
                audio_decoder.FlushFrames();
            }
        }

        if let Some(audio_export) = self._audio_export.as_ref() {
            let mut guard = audio_export
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            guard.Flush();
        }

        Ok(())
    }

    pub fn CanLoop(&self) -> bool {
        self.CanSeek()
    }

    pub fn SetLoop(&self, loop_value: bool) {
        if !self.CanLoop() {
            return;
        }
        self._looping.store(loop_value, Ordering::SeqCst);
    }

    pub fn IsLooping(&self) -> bool {
        self._looping.load(Ordering::SeqCst)
    }

    pub fn Duration(&self) -> f64 {
        if let Ok(s) = self._source.lock() {
            s.Duration()
        } else {
            -1.0
        }
    }

    pub fn CurrentTime(&self) -> f64 {
        if let Ok(clock) = self._clock.lock() {
            clock.MasterTime()
        } else {
            0.0
        }
    }

    pub fn SetAudioSinkDelay(&self, delay_sec: f64) {
        if let Ok(mut clock) = self._clock.lock() {
            clock.SetAudioSinkDelay(delay_sec);
        }
    }

    pub fn HealthSnapshot(&self) -> AVLibPlayerHealthSnapshot {
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
        ) =
            if let Ok(source) = self._source.lock() {
                let source_stats = source.RuntimeStats();
                (
                    source_stats.connection_state.IsConnected(),
                    source.StreamCount(),
                    source.IsRealtime(),
                    source.CanSeek(),
                    source.Duration(),
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
                    AVLibSourceConnectionState::Disconnected as i32,
                    -1.0,
                )
            };

        let video_decoder_count = self
            ._decoders
            .lock()
            .map(|decoders| decoders.len() as i32)
            .unwrap_or(0);

        let has_audio_decoder = self
            ._audio_decoder
            .lock()
            .map(|decoder| decoder.is_some())
            .unwrap_or(false);
        let audio_frame_drop_count = self
            ._audio_decoder
            .lock()
            .map(|decoder| {
                decoder
                    .as_ref()
                    .map(|audio_decoder| audio_decoder.DroppedFrameCount())
                    .unwrap_or(0)
            })
            .unwrap_or(0);
        let video_frame_drop_count = self
            ._decoders
            .lock()
            .map(|decoders| decoders.iter().map(|decoder| decoder.DroppedFrameCount()).sum())
            .unwrap_or(0);

        let (
            current_time_sec,
            external_time_sec,
            audio_time_sec,
            audio_presented_time_sec,
            audio_sink_delay_sec,
        ) = if let Ok(clock) = self._clock.lock() {
            (
                clock.MasterTime(),
                clock.ExternalTime(),
                clock.AudioTime().unwrap_or(-1.0),
                clock.AudioPresentedTime().unwrap_or(-1.0),
                clock.AudioSinkDelay(),
            )
        } else {
            (0.0, 0.0, -1.0, -1.0, 0.0)
        };

        let video_sync_compensation_sec = self
            ._videoSyncCompensationSec
            .lock()
            .map(|value| *value)
            .unwrap_or(0.0);

        let runtime_state = Self::LoadState(&self._state);
        let playback_intent = Self::LoadPlaybackIntent(&self._playing);
        let stop_reason = Self::LoadStopReason(&self._stopReason);
        let public_state = Self::ResolvePublicState(runtime_state, playback_intent, stop_reason);

        AVLibPlayerHealthSnapshot {
            state: public_state as i32,
            runtime_state: runtime_state as i32,
            playback_intent: playback_intent as i32,
            stop_reason: stop_reason as i32,
            source_connection_state,
            is_connected,
            is_playing: self.IsPlaying(),
            is_realtime,
            can_seek,
            is_looping: self.IsLooping(),
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
            connect_attempt_count: self._connectAttemptCount.load(Ordering::SeqCst),
            video_decoder_recreate_count: self._videoDecoderRecreateCount.load(Ordering::SeqCst),
            audio_decoder_recreate_count: self._audioDecoderRecreateCount.load(Ordering::SeqCst),
            video_frame_drop_count,
            audio_frame_drop_count,
            source_packet_count,
            source_timeout_count,
            source_reconnect_count,
            source_is_checking_connection,
            source_last_activity_age_sec,
        }
    }

    pub fn StreamInfo(&self, stream_index: i32) -> Option<AVLibStreamInfo> {
        let Ok(source) = self._source.lock() else {
            return None;
        };

        if stream_index < 0 || stream_index >= source.StreamCount() {
            return None;
        }

        let mut info = source.Stream(stream_index);
        drop(source);

        if info.codec_type == ffmpeg_next::ffi::AVMediaType::AVMEDIA_TYPE_VIDEO
            && (info.width <= 0 || info.height <= 0)
        {
            if let Ok(decoders) = self._decoders.lock() {
                for decoder in decoders.iter() {
                    if decoder._streamIndex == stream_index {
                        if let Some((width, height)) = decoder.ActualSourceVideoSize() {
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

    pub fn IsPlaying(&self) -> bool {
        self._playing.load(Ordering::SeqCst)
    }

    pub fn IsRealtime(&self) -> bool {
        if let Ok(s) = self._source.lock() {
            s.IsRealtime()
        } else {
            false
        }
    }
}

impl Drop for AVLibPlayer {
    fn drop(&mut self) {
        self._state
            .store(AVLibPlayerState::Shutdown as i32, Ordering::SeqCst);
        self._stayAlive.store(false, Ordering::SeqCst);
        self._killCondition.notify_all();
        if let Some(t) = self._thread.take() {
            let _ = t.join();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{
        AVLibPlayer, AVLibPlayerPlaybackIntent, AVLibPlayerState, AVLibPlayerStopReason,
    };
    use std::sync::{
        atomic::{AtomicI32, Ordering},
        Arc,
    };

    #[test]
    fn StoreState_does_not_override_shutdown() {
        let state = Arc::new(AtomicI32::new(AVLibPlayerState::Shutdown as i32));
        AVLibPlayer::StoreState(&state, AVLibPlayerState::Ready);
        assert_eq!(
            state.load(Ordering::SeqCst),
            AVLibPlayerState::Shutdown as i32
        );
    }

    #[test]
    fn ResolvePublicState_returns_ended_after_eof() {
        let resolved = AVLibPlayer::ResolvePublicState(
            AVLibPlayerState::Ready,
            AVLibPlayerPlaybackIntent::Stopped,
            AVLibPlayerStopReason::EndOfStream,
        );
        assert_eq!(resolved, AVLibPlayerState::Ended);
    }

    #[test]
    fn ResolvePublicState_returns_paused_after_user_stop() {
        let resolved = AVLibPlayer::ResolvePublicState(
            AVLibPlayerState::Ready,
            AVLibPlayerPlaybackIntent::Stopped,
            AVLibPlayerStopReason::UserStop,
        );
        assert_eq!(resolved, AVLibPlayerState::Paused);
    }

    #[test]
    fn ResolvePublicState_returns_playing_while_play_requested() {
        let resolved = AVLibPlayer::ResolvePublicState(
            AVLibPlayerState::Ready,
            AVLibPlayerPlaybackIntent::PlayRequested,
            AVLibPlayerStopReason::None,
        );
        assert_eq!(resolved, AVLibPlayerState::Playing);
    }

    #[test]
    fn CanAcceptSeek_allows_ready_and_ended_states() {
        assert!(AVLibPlayer::CanAcceptSeek(AVLibPlayerState::Ready, true));
        assert!(AVLibPlayer::CanAcceptSeek(AVLibPlayerState::Ended, true));
        assert!(!AVLibPlayer::CanAcceptSeek(AVLibPlayerState::Connecting, true));
        assert!(!AVLibPlayer::CanAcceptSeek(AVLibPlayerState::Ready, false));
    }
}
