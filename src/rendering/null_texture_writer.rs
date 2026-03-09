use crate::pixel_format::PixelFormat;
use crate::rendering::texture_writer::{TextureWriter, TextureWriterLike};

pub struct NullTextureWriter {
    pub(crate) base: TextureWriter,
}

impl NullTextureWriter {
    const K_DEFAULT_BPP: i32 = 4;

    pub fn new(width: i32, height: i32) -> Self {
        let mut base = TextureWriter::new();
        base.target_width = width;
        base.target_height = height;
        base.target_format = PixelFormat::Unknown;
        base.buffer_sizes.push(width * height * Self::K_DEFAULT_BPP);
        base.buffer_strides.push(width * Self::K_DEFAULT_BPP);
        base.buffers.push(vec![0u8; base.buffer_sizes[0] as usize]);
        base.ready.store(true, std::sync::atomic::Ordering::SeqCst);

        Self { base }
    }

    pub fn write(&mut self, _force: bool) {
        // intentionally empty
    }
}

impl TextureWriterLike for NullTextureWriter {
    fn format(&self) -> PixelFormat {
        self.base.format()
    }

    fn width(&self) -> i32 {
        self.base.width()
    }

    fn height(&self) -> i32 {
        self.base.height()
    }

    fn read_planes(&mut self, source: &[&[u8]]) {
        self.base.read_planes(source);
    }

    fn write(&mut self, force: bool) {
        NullTextureWriter::write(self, force);
    }
}
