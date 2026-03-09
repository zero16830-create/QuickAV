use crate::pixel_format::PixelFormat;
use crate::video_client::VideoClient;
use crate::video_description::VideoDescription;
use crate::video_frame::VideoFrame;
use std::sync::{Arc, Mutex};

#[derive(Clone, Copy)]
pub struct ExportedFrameMeta {
    pub width: i32,
    pub height: i32,
    pub stride: i32,
    pub data_len: i32,
    pub format: PixelFormat,
    pub time: f64,
    pub frame_index: i64,
    pub has_frame: bool,
}

pub struct ExportedFrameState {
    width: i32,
    height: i32,
    stride: i32,
    format: PixelFormat,
    time: f64,
    frame_index: i64,
    has_frame: bool,
    data: Vec<u8>,
}

pub type SharedExportedFrameState = Arc<Mutex<ExportedFrameState>>;

impl ExportedFrameState {
    pub fn new(width: i32, height: i32, format: PixelFormat) -> Self {
        let stride = if format == PixelFormat::Rgba32 {
            width.saturating_mul(4)
        } else {
            0
        };
        let data_length = stride.saturating_mul(height).max(0) as usize;

        Self {
            width,
            height,
            stride,
            format,
            time: 0.0,
            frame_index: 0,
            has_frame: false,
            data: vec![0; data_length],
        }
    }

    pub fn meta(&self) -> ExportedFrameMeta {
        ExportedFrameMeta {
            width: self.width,
            height: self.height,
            stride: self.stride,
            data_len: self.data.len() as i32,
            format: self.format,
            time: self.time,
            frame_index: self.frame_index,
            has_frame: self.has_frame,
        }
    }

    pub fn copy_to(&self, destination: &mut [u8]) -> i32 {
        if !self.has_frame || destination.len() < self.data.len() {
            return 0;
        }

        destination[..self.data.len()].copy_from_slice(&self.data);
        self.data.len() as i32
    }
}

pub struct FrameExportClient {
    width: i32,
    height: i32,
    format: PixelFormat,
    shared: SharedExportedFrameState,
}

impl FrameExportClient {
    pub fn new(width: i32, height: i32, format: PixelFormat) -> (Self, SharedExportedFrameState) {
        let shared = Arc::new(Mutex::new(ExportedFrameState::new(width, height, format)));
        (
            Self {
                width,
                height,
                format,
                shared: shared.clone(),
            },
            shared,
        )
    }
}

impl VideoDescription for FrameExportClient {
    fn width(&self) -> i32 {
        self.width
    }

    fn height(&self) -> i32 {
        self.height
    }

    fn format(&self) -> PixelFormat {
        self.format
    }
}

impl VideoClient for FrameExportClient {
    fn on_frame_ready(&mut self, frame: &mut VideoFrame) {
        if self.format != PixelFormat::Rgba32 {
            return;
        }

        let Some(source) = frame.buffer(0) else {
            return;
        };

        let mut shared = self
            .shared
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        if shared.data.len() != source.len() {
            shared.data.resize(source.len(), 0);
        }

        shared.data.copy_from_slice(source);
        shared.time = frame.time();
        shared.frame_index = shared.frame_index.saturating_add(1);
        shared.has_frame = true;
    }

    fn write(&mut self) {}
}
