#![allow(non_snake_case)]
use crate::PixelFormat::PixelFormat;
use crate::Rendering::TextureWriter::{TextureWriter, TextureWriterLike};

pub struct NullTextureWriter {
    pub(crate) base: TextureWriter,
}

impl NullTextureWriter {
    const K_DEFAULT_BPP: i32 = 4;

    pub fn new(width: i32, height: i32) -> Self {
        let mut base = TextureWriter::new();
        base.TargetWidth = width;
        base.TargetHeight = height;
        base.TargetFormat = PixelFormat::PIXEL_FORMAT_NONE;
        base.BufferSizes.push(width * height * Self::K_DEFAULT_BPP);
        base.BufferStrides.push(width * Self::K_DEFAULT_BPP);
        base.Buffers.push(vec![0u8; base.BufferSizes[0] as usize]);
        base.Ready.store(true, std::sync::atomic::Ordering::SeqCst);

        Self { base }
    }

    pub fn Write(&mut self, _force: bool) {
        // intentionally empty
    }
}

impl TextureWriterLike for NullTextureWriter {
    fn Format(&self) -> PixelFormat {
        self.base.Format()
    }

    fn Width(&self) -> i32 {
        self.base.Width()
    }

    fn Height(&self) -> i32 {
        self.base.Height()
    }

    fn ReadPlanes(&mut self, source: &[&[u8]]) {
        self.base.ReadPlanes(source);
    }

    fn Write(&mut self, force: bool) {
        NullTextureWriter::Write(self, force);
    }
}
