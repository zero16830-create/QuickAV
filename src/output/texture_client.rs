use crate::pixel_format::PixelFormat;
use crate::rendering::null_texture_writer::NullTextureWriter;
use crate::rendering::texture_writer::{TextureWriter, TextureWriterLike};
use crate::video_client::VideoClient;
use crate::video_description::VideoDescription;
use crate::video_frame::VideoFrame;
use std::os::raw::c_void;

#[cfg(windows)]
use crate::rendering::d3d11_texture_writer::D3D11TextureWriter;

pub struct TextureClient {
    writer: Box<dyn TextureWriterLike>,
}

impl TextureClient {
    pub fn new(target_texture: *mut c_void) -> Option<Self> {
        let writer = TextureWriter::create(target_texture)?;
        Some(Self { writer })
    }

    pub fn from_null_writer(width: i32, height: i32) -> Self {
        Self {
            writer: Box::new(NullTextureWriter::new(width, height)),
        }
    }

    #[cfg(windows)]
    pub fn from_d3d11_writer(writer: D3D11TextureWriter) -> Self {
        Self {
            writer: Box::new(writer),
        }
    }
}

impl VideoDescription for TextureClient {
    fn width(&self) -> i32 {
        self.writer.width()
    }

    fn height(&self) -> i32 {
        self.writer.height()
    }

    fn format(&self) -> PixelFormat {
        self.writer.format()
    }
}

impl VideoClient for TextureClient {
    fn on_frame_ready(&mut self, frame: &mut VideoFrame) {
        let p0 = frame.buffer(0).unwrap_or(&[]);
        let p1 = frame.buffer(1).unwrap_or(&[]);
        let p2 = frame.buffer(2).unwrap_or(&[]);
        let p3 = frame.buffer(3).unwrap_or(&[]);
        let all = [p0, p1, p2, p3];
        let count = frame.buffer_count().clamp(0, all.len() as i32) as usize;
        self.writer.read_planes(&all[..count]);
    }

    fn write(&mut self) {
        self.writer.write(false);
    }
}
