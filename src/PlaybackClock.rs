#![allow(non_snake_case)]

use std::time::Instant;

pub struct PlaybackClock {
    _externalPositionSec: f64,
    _lastTick: Instant,
    _audioEnabled: bool,
    _audioAnchorExternalSec: f64,
    _audioAnchorSec: f64,
    _audioBufferedUntilSec: f64,
    _audioSinkDelaySec: f64,
}

impl PlaybackClock {
    const AUDIO_DISCONTINUITY_THRESHOLD_SEC: f64 = 0.250;
    const AUDIO_STALL_FALLBACK_SEC: f64 = 60.000;

    pub fn new() -> Self {
        Self {
            _externalPositionSec: 0.0,
            _lastTick: Instant::now(),
            _audioEnabled: false,
            _audioAnchorExternalSec: 0.0,
            _audioAnchorSec: 0.0,
            _audioBufferedUntilSec: 0.0,
            _audioSinkDelaySec: 0.0,
        }
    }

    pub fn TickPlaying(&mut self) -> f64 {
        let now = Instant::now();
        let delta = now.duration_since(self._lastTick).as_secs_f64();
        self._lastTick = now;
        self._externalPositionSec += delta;
        self.MaybeFallbackToExternalClock();
        self.MasterTime()
    }

    pub fn TickPaused(&mut self) {
        self._lastTick = Instant::now();
    }

    pub fn OnPlay(&mut self) {
        self._lastTick = Instant::now();
    }

    pub fn OnPause(&mut self) {
        self._lastTick = Instant::now();
    }

    pub fn Seek(&mut self, position_sec: f64) {
        self._externalPositionSec = position_sec.max(0.0);
        self._lastTick = Instant::now();
        self.ResetAudioClock();
    }

    pub fn Reset(&mut self) {
        self._externalPositionSec = 0.0;
        self._lastTick = Instant::now();
        self.ResetAudioClock();
    }

    pub fn ExternalTime(&self) -> f64 {
        self._externalPositionSec
    }

    pub fn MasterTime(&self) -> f64 {
        self.AudioPresentedTime().unwrap_or(self._externalPositionSec)
    }

    pub fn AudioTime(&self) -> Option<f64> {
        if !self._audioEnabled {
            return None;
        }

        let progressed = (self._externalPositionSec - self._audioAnchorExternalSec).max(0.0);
        Some((self._audioAnchorSec + progressed).min(self._audioBufferedUntilSec))
    }

    pub fn AudioPresentedTime(&self) -> Option<f64> {
        let logical = self.AudioTime()?;
        Some((logical - self._audioSinkDelaySec).max(0.0))
    }

    pub fn SetAudioSinkDelay(&mut self, delay_sec: f64) {
        self._audioSinkDelaySec = delay_sec.max(0.0);
    }

    pub fn AudioSinkDelay(&self) -> f64 {
        self._audioSinkDelaySec
    }

    pub fn ObserveAudioFrame(&mut self, frame_time_sec: f64, frame_duration_sec: f64) -> bool {
        if !frame_time_sec.is_finite()
            || !frame_duration_sec.is_finite()
            || frame_duration_sec <= 0.0
        {
            return false;
        }

        let frame_end_sec = frame_time_sec + frame_duration_sec;
        if !self._audioEnabled {
            self._audioEnabled = true;
            self._audioAnchorExternalSec = self._externalPositionSec;
            self._audioAnchorSec = frame_time_sec;
            self._audioBufferedUntilSec = frame_end_sec;
            return true;
        }

        let current_audio_time = self.AudioTime().unwrap_or(self._externalPositionSec);
        let discontinuity = frame_time_sec
            > self._audioBufferedUntilSec + Self::AUDIO_DISCONTINUITY_THRESHOLD_SEC
            || frame_end_sec + Self::AUDIO_DISCONTINUITY_THRESHOLD_SEC < current_audio_time;

        if discontinuity {
            self._audioAnchorExternalSec = self._externalPositionSec;
            self._audioAnchorSec = frame_time_sec;
            self._audioBufferedUntilSec = frame_end_sec;
            return true;
        }

        if frame_time_sec < self._audioAnchorSec {
            self._audioAnchorSec = frame_time_sec;
            self._audioAnchorExternalSec = self._externalPositionSec;
        }

        if frame_end_sec > self._audioBufferedUntilSec {
            self._audioBufferedUntilSec = frame_end_sec;
        }

        false
    }

    pub fn HasAudioClock(&self) -> bool {
        self._audioEnabled
    }

    pub fn ResetAudioClock(&mut self) {
        self._audioEnabled = false;
        self._audioAnchorExternalSec = self._externalPositionSec;
        self._audioAnchorSec = 0.0;
        self._audioBufferedUntilSec = 0.0;
    }

    fn MaybeFallbackToExternalClock(&mut self) {
        if self._audioEnabled
            && self._externalPositionSec
                > self._audioBufferedUntilSec + Self::AUDIO_STALL_FALLBACK_SEC
        {
            self.ResetAudioClock();
        }
    }
}
