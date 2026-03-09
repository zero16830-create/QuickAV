use rustav_native::AudioExportState::ExportedAudioState;
use rustav_native::AudioFrame::AudioFrame;

fn make_audio_frame(sample_rate: i32, channels: i32, samples: i32, time_sec: f64) -> AudioFrame {
    let mut frame = AudioFrame::new(sample_rate, channels, samples);
    frame.SetTime(time_sec);
    frame.SetDuration(samples as f64 / sample_rate as f64);

    for (index, byte) in frame.BufferMut().iter_mut().enumerate() {
        *byte = (index % 251) as u8;
    }

    frame
}

#[test]
fn push_frame_updates_meta_and_copy_to_consumes_buffer() {
    let mut state = ExportedAudioState::new();
    let initial = state.Meta();
    assert!(!initial.HasAudio);
    assert_eq!(initial.BufferedBytes, 0);

    let frame = make_audio_frame(48_000, 2, 4, 1.25);
    let total_len = frame.DataLength();
    state.PushFrame(&frame);

    let meta = state.Meta();
    assert!(meta.HasAudio);
    assert_eq!(meta.SampleRate, 48_000);
    assert_eq!(meta.Channels, 2);
    assert_eq!(meta.BufferedBytes as usize, total_len);
    assert_eq!(meta.Time, 1.25);

    let mut destination = vec![0u8; AudioFrame::BYTES_PER_SAMPLE * 2];
    let copied = state.CopyTo(&mut destination);
    assert_eq!(copied as usize, destination.len());

    let after_copy = state.Meta();
    assert!(after_copy.HasAudio);
    assert_eq!(
        after_copy.BufferedBytes as usize,
        total_len - destination.len()
    );
    assert!(after_copy.Time > 1.25);
}

#[test]
fn push_frame_resets_buffer_when_audio_shape_changes() {
    let mut state = ExportedAudioState::new();
    let first = make_audio_frame(48_000, 2, 8, 0.5);
    let second = make_audio_frame(44_100, 1, 8, 1.0);

    state.PushFrame(&first);
    state.PushFrame(&second);

    let meta = state.Meta();
    assert!(meta.HasAudio);
    assert_eq!(meta.SampleRate, 44_100);
    assert_eq!(meta.Channels, 1);
    assert_eq!(meta.BufferedBytes as usize, second.DataLength());
    assert_eq!(meta.Time, 1.0);
}
