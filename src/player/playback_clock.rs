use std::time::Instant;

pub struct PlaybackClock {
    external_position_sec: f64,
    last_tick: Instant,
    audio_enabled: bool,
    audio_anchor_external_sec: f64,
    audio_anchor_sec: f64,
    audio_buffered_until_sec: f64,
    audio_sink_delay_sec: f64,
}

impl PlaybackClock {
    const AUDIO_DISCONTINUITY_THRESHOLD_SEC: f64 = 0.250;
    const AUDIO_STALL_FALLBACK_SEC: f64 = 60.000;

    pub fn new() -> Self {
        Self {
            external_position_sec: 0.0,
            last_tick: Instant::now(),
            audio_enabled: false,
            audio_anchor_external_sec: 0.0,
            audio_anchor_sec: 0.0,
            audio_buffered_until_sec: 0.0,
            audio_sink_delay_sec: 0.0,
        }
    }

    pub fn tick_playing(&mut self) -> f64 {
        let now = Instant::now();
        let delta = now.duration_since(self.last_tick).as_secs_f64();
        self.last_tick = now;
        self.external_position_sec += delta;
        self.maybe_fallback_to_external_clock();
        self.master_time()
    }

    pub fn tick_paused(&mut self) {
        self.last_tick = Instant::now();
    }

    pub fn on_play(&mut self) {
        self.last_tick = Instant::now();
    }

    pub fn on_pause(&mut self) {
        self.last_tick = Instant::now();
    }

    pub fn seek(&mut self, position_sec: f64) {
        self.rebase_external_clock(position_sec);
        self.reset_audio_clock();
    }

    pub fn reset(&mut self) {
        self.rebase_external_clock(0.0);
        self.reset_audio_clock();
    }

    pub fn external_time(&self) -> f64 {
        self.external_position_sec
    }

    pub fn master_time(&self) -> f64 {
        self.audio_presented_time()
            .unwrap_or(self.external_position_sec)
    }

    pub fn audio_time(&self) -> Option<f64> {
        if !self.audio_enabled {
            return None;
        }

        let progressed = (self.external_position_sec - self.audio_anchor_external_sec).max(0.0);
        Some((self.audio_anchor_sec + progressed).min(self.audio_buffered_until_sec))
    }

    pub fn audio_presented_time(&self) -> Option<f64> {
        let logical = self.audio_time()?;
        Some((logical - self.audio_sink_delay_sec).max(0.0))
    }

    pub fn set_audio_sink_delay(&mut self, delay_sec: f64) {
        self.audio_sink_delay_sec = delay_sec.max(0.0);
    }

    pub fn audio_sink_delay(&self) -> f64 {
        self.audio_sink_delay_sec
    }

    pub fn observe_audio_frame(&mut self, frame_time_sec: f64, frame_duration_sec: f64) -> bool {
        if !frame_time_sec.is_finite()
            || !frame_duration_sec.is_finite()
            || frame_duration_sec <= 0.0
        {
            return false;
        }

        let frame_end_sec = frame_time_sec + frame_duration_sec;
        if !self.audio_enabled {
            if (self.external_position_sec - frame_time_sec).abs()
                > Self::AUDIO_DISCONTINUITY_THRESHOLD_SEC
            {
                // 音频时钟被 fallback 清空后，第一帧音频需要把外部时钟一起拉回源时间轴。
                self.rebase_external_clock(frame_time_sec);
            }
            self.audio_enabled = true;
            self.audio_anchor_external_sec = self.external_position_sec;
            self.audio_anchor_sec = frame_time_sec;
            self.audio_buffered_until_sec = frame_end_sec;
            return true;
        }

        let current_audio_time = self.audio_time().unwrap_or(self.external_position_sec);
        let discontinuity = frame_time_sec
            > self.audio_buffered_until_sec + Self::AUDIO_DISCONTINUITY_THRESHOLD_SEC
            || frame_end_sec + Self::AUDIO_DISCONTINUITY_THRESHOLD_SEC < current_audio_time;

        if discontinuity {
            // 源时间轴发生跳变时，外部时钟也必须一起重置，否则长跑后会把旧时间基线带回同步环。
            self.rebase_external_clock(frame_time_sec);
            self.audio_anchor_external_sec = self.external_position_sec;
            self.audio_anchor_sec = frame_time_sec;
            self.audio_buffered_until_sec = frame_end_sec;
            return true;
        }

        if frame_time_sec < self.audio_anchor_sec {
            self.audio_anchor_sec = frame_time_sec;
            self.audio_anchor_external_sec = self.external_position_sec;
        }

        if frame_end_sec > self.audio_buffered_until_sec {
            self.audio_buffered_until_sec = frame_end_sec;
        }

        false
    }

    pub fn has_audio_clock(&self) -> bool {
        self.audio_enabled
    }

    pub fn reset_audio_clock(&mut self) {
        self.audio_enabled = false;
        self.audio_anchor_external_sec = self.external_position_sec;
        self.audio_anchor_sec = 0.0;
        self.audio_buffered_until_sec = 0.0;
    }

    fn rebase_external_clock(&mut self, position_sec: f64) {
        self.external_position_sec = position_sec.max(0.0);
        self.last_tick = Instant::now();
    }

    fn maybe_fallback_to_external_clock(&mut self) {
        if self.audio_enabled
            && self.external_position_sec
                > self.audio_buffered_until_sec + Self::AUDIO_STALL_FALLBACK_SEC
        {
            self.reset_audio_clock();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::PlaybackClock;

    #[test]
    fn observe_audio_frame_rebases_external_clock_on_backward_discontinuity() {
        let mut clock = PlaybackClock::new();
        clock.audio_enabled = true;
        clock.external_position_sec = 370.0;
        clock.audio_anchor_external_sec = 360.0;
        clock.audio_anchor_sec = 360.0;
        clock.audio_buffered_until_sec = 360.5;

        assert!(clock.observe_audio_frame(309.8, 0.0213));
        assert!((clock.external_time() - 309.8).abs() < 0.0001);
        assert!(clock.has_audio_clock());
        assert!((clock.audio_time().unwrap_or(-1.0) - 309.8).abs() < 0.0001);
    }

    #[test]
    fn observe_audio_frame_rebases_external_clock_when_reenabling_audio_after_stall() {
        let mut clock = PlaybackClock::new();
        clock.external_position_sec = 403.705;
        clock.audio_anchor_external_sec = 343.0;
        clock.audio_anchor_sec = 343.0;
        clock.audio_buffered_until_sec = 343.4;
        clock.reset_audio_clock();

        assert!(clock.observe_audio_frame(343.445, 0.0213));
        assert!((clock.external_time() - 343.445).abs() < 0.0001);
        assert!(clock.has_audio_clock());
        assert!((clock.audio_time().unwrap_or(-1.0) - 343.445).abs() < 0.0001);
    }
}
