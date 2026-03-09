use rustav_native::PlaybackClock::PlaybackClock;

#[test]
fn observe_audio_frame_enables_audio_clock_and_presented_time_respects_sink_delay() {
    let mut clock = PlaybackClock::new();

    assert!(clock.ObserveAudioFrame(1.0, 0.5));
    assert!(clock.HasAudioClock());
    assert_eq!(clock.AudioTime(), Some(1.0));

    clock.SetAudioSinkDelay(0.1);
    assert_eq!(clock.AudioPresentedTime(), Some(0.9));
}

#[test]
fn observe_audio_frame_rejects_invalid_input() {
    let mut clock = PlaybackClock::new();

    assert!(!clock.ObserveAudioFrame(f64::NAN, 0.5));
    assert!(!clock.ObserveAudioFrame(0.0, -1.0));
    assert!(!clock.HasAudioClock());
}

#[test]
fn seek_resets_audio_clock_and_clamps_to_zero() {
    let mut clock = PlaybackClock::new();
    assert!(clock.ObserveAudioFrame(2.0, 0.25));

    clock.Seek(-1.0);

    assert_eq!(clock.ExternalTime(), 0.0);
    assert!(!clock.HasAudioClock());
    assert_eq!(clock.AudioTime(), None);
}
