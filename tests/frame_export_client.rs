use rustav_native::FrameExportClient::FrameExportClient;
use rustav_native::IVideoClient::IVideoClient;
use rustav_native::PixelFormat::PixelFormat;
use rustav_native::VideoFrame::VideoFrame;

#[test]
fn exported_frame_state_returns_zero_when_no_frame_is_available() {
    let (_, shared) = FrameExportClient::new(4, 2, PixelFormat::PIXEL_FORMAT_RGBA32);
    let shared = shared
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let meta = shared.Meta();

    assert!(!meta.HasFrame);
    assert_eq!(meta.Width, 4);
    assert_eq!(meta.Height, 2);
    assert_eq!(meta.Stride, 16);
    assert_eq!(meta.DataLength, 32);

    let mut buffer = vec![0_u8; meta.DataLength as usize];
    assert_eq!(shared.CopyTo(&mut buffer), 0);
}

#[test]
fn frame_export_client_copies_rgba_frame_and_updates_meta() {
    let (mut client, shared) = FrameExportClient::new(2, 1, PixelFormat::PIXEL_FORMAT_RGBA32);
    let mut frame = VideoFrame::new(2, 1, PixelFormat::PIXEL_FORMAT_RGBA32);
    frame.SetTime(1.25);
    frame
        .BufferMut(0)
        .expect("rgba frame must expose primary buffer")
        .copy_from_slice(&[1, 2, 3, 4, 5, 6, 7, 8]);

    client.OnFrameReady(&mut frame);

    let shared = shared
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let meta = shared.Meta();
    assert!(meta.HasFrame);
    assert_eq!(meta.Width, 2);
    assert_eq!(meta.Height, 1);
    assert_eq!(meta.Stride, 8);
    assert_eq!(meta.DataLength, 8);
    assert_eq!(meta.FrameIndex, 1);
    assert!((meta.Time - 1.25).abs() < f64::EPSILON);

    let mut buffer = vec![0_u8; 8];
    assert_eq!(shared.CopyTo(&mut buffer), 8);
    assert_eq!(buffer, vec![1, 2, 3, 4, 5, 6, 7, 8]);
}
