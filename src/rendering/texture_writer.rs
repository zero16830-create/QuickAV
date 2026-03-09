use crate::dllmain::{unity_interfaces_ready, unity_renderer};
use crate::pixel_format::PixelFormat;
use std::os::raw::c_void;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;

#[cfg(windows)]
use crate::rendering::d3d11_texture_writer::D3D11TextureWriter;

#[cfg(windows)]
const K_UNITY_GFX_RENDERER_D3D11: i32 = 2;

pub trait TextureWriterLike: Send {
    fn format(&self) -> PixelFormat;
    fn width(&self) -> i32;
    fn height(&self) -> i32;
    fn read_planes(&mut self, source: &[&[u8]]);
    fn write(&mut self, force: bool);
}

pub struct TextureWriter {
    pub(crate) ready: AtomicBool,
    pub(crate) changed: AtomicBool,
    pub(crate) buffer_mutex: Mutex<()>,
    pub(crate) target_width: i32,
    pub(crate) target_height: i32,
    pub(crate) target_format: PixelFormat,
    pub(crate) buffer_sizes: Vec<i32>,
    pub(crate) buffer_strides: Vec<i32>,
    pub(crate) buffers: Vec<Vec<u8>>,
}

impl TextureWriter {
    pub fn create(target_texture: *mut c_void) -> Option<Box<dyn TextureWriterLike>> {
        if target_texture.is_null() {
            return None;
        }

        if !unity_interfaces_ready() {
            return None;
        }

        #[cfg(windows)]
        match unity_renderer() {
            K_UNITY_GFX_RENDERER_D3D11 => unsafe {
                D3D11TextureWriter::new(target_texture)
                    .map(|w| Box::new(w) as Box<dyn TextureWriterLike>)
            },
            _ => None,
        }

        #[cfg(not(windows))]
        {
            let _ = target_texture;
            None
        }
    }

    pub fn new() -> Self {
        Self {
            ready: AtomicBool::new(false),
            changed: AtomicBool::new(false),
            buffer_mutex: Mutex::new(()),
            target_width: 0,
            target_height: 0,
            target_format: PixelFormat::Unknown,
            buffer_sizes: Vec::new(),
            buffer_strides: Vec::new(),
            buffers: Vec::new(),
        }
    }

    pub fn read(&mut self, source: &[u8]) {
        self.read_planes(&[source]);
    }

    pub fn read_planes(&mut self, source: &[&[u8]]) {
        if !self.ready.load(Ordering::SeqCst) {
            return;
        }

        if self.buffers.is_empty() || source.len() < self.buffers.len() {
            return;
        }

        for i in 0..self.buffers.len() {
            let target_len = if i < self.buffer_sizes.len() && self.buffer_sizes[i] > 0 {
                self.buffer_sizes[i] as usize
            } else {
                self.buffers[i].len()
            };
            if source[i].len() < target_len || self.buffers[i].len() < target_len {
                return;
            }
        }

        let _lock = self.buffer_mutex.lock().unwrap();
        for i in 0..self.buffers.len() {
            let target_len = if i < self.buffer_sizes.len() && self.buffer_sizes[i] > 0 {
                self.buffer_sizes[i] as usize
            } else {
                self.buffers[i].len()
            };
            self.buffers[i][..target_len].copy_from_slice(&source[i][..target_len]);
        }
        self.changed.store(true, Ordering::SeqCst);
    }

    pub fn format(&self) -> PixelFormat {
        self.target_format
    }

    pub fn width(&self) -> i32 {
        self.target_width
    }

    pub fn height(&self) -> i32 {
        self.target_height
    }

    pub fn buffer_count(&self) -> i32 {
        self.buffers.len() as i32
    }

    pub fn buffer_size(&self, index: i32) -> i32 {
        if index < 0 || index as usize >= self.buffer_sizes.len() {
            return -1;
        }

        self.buffer_sizes[index as usize]
    }

    pub fn buffer_stride(&self, index: i32) -> i32 {
        if index < 0 || index as usize >= self.buffer_strides.len() {
            return -1;
        }

        self.buffer_strides[index as usize]
    }
}
