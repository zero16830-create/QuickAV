use crate::logging::debug::Debug;
use crate::pixel_format::PixelFormat;
use crate::rendering::texture_writer::{TextureWriter, TextureWriterLike};
use std::os::raw::c_void;
use std::sync::atomic::Ordering;
use windows::core::Interface;
use windows::Win32::Graphics::Direct3D11::{ID3D11Device, ID3D11Texture2D, D3D11_TEXTURE2D_DESC};
use windows::Win32::Graphics::Dxgi::Common::DXGI_FORMAT_R8G8B8A8_UNORM;

pub struct D3D11TextureWriter {
    pub(crate) base: TextureWriter,
    pub(crate) texture: ID3D11Texture2D,
    pub(crate) device: ID3D11Device,
}

impl D3D11TextureWriter {
    pub unsafe fn new(target_texture: *mut c_void) -> Option<Self> {
        if target_texture.is_null() {
            Debug::log_error("D3D11TextureWriter::new - target texture was null");
            return None;
        }

        let texture = match ID3D11Texture2D::from_raw_borrowed(&target_texture) {
            Some(t) => t.clone(),
            None => {
                Debug::log_error("D3D11TextureWriter::new - could not resolve ID3D11Texture2D");
                return None;
            }
        };

        let mut desc = D3D11_TEXTURE2D_DESC::default();
        texture.GetDesc(&mut desc);

        let device = match texture.GetDevice() {
            Ok(d) => d,
            Err(_) => {
                Debug::log_error("D3D11TextureWriter::new - failed to get device");
                return None;
            }
        };

        if desc.Format != DXGI_FORMAT_R8G8B8A8_UNORM {
            Debug::log_error("D3D11TextureWriter::new - texture format is not RGBA32");
            return None;
        }

        let mut base = TextureWriter::new();
        base.target_width = desc.Width as i32;
        base.target_height = desc.Height as i32;
        base.target_format = PixelFormat::Rgba32;
        base.buffer_strides.push(base.target_width * 4);
        base.buffer_sizes
            .push(base.buffer_strides[0] * base.target_height);
        base.buffers.push(vec![0u8; base.buffer_sizes[0] as usize]);
        base.ready.store(true, Ordering::SeqCst);
        base.changed.store(false, Ordering::SeqCst);

        Some(Self {
            base,
            texture,
            device,
        })
    }

    pub fn read(&mut self, data: &[u8]) {
        self.base.read(data);
    }

    pub fn read_planes(&mut self, planes: &[&[u8]]) {
        self.base.read_planes(planes);
    }

    pub fn write(&mut self, force: bool) {
        if !self.base.ready.load(Ordering::SeqCst) {
            return;
        }

        if force || self.base.changed.load(Ordering::SeqCst) {
            if self.base.buffers.is_empty() || self.base.buffer_strides.is_empty() {
                return;
            }

            let _lock = self.base.buffer_mutex.lock().unwrap();
            match unsafe { self.device.GetImmediateContext() } {
                Ok(context) => unsafe {
                    context.UpdateSubresource(
                        &self.texture,
                        0,
                        None,
                        self.base.buffers[0].as_ptr() as *const c_void,
                        self.base.buffer_strides[0] as u32,
                        0,
                    );
                },
                Err(_) => {
                    Debug::log_error("D3D11TextureWriter::write - failed to get immediate context");
                    return;
                }
            }

            self.base.changed.store(false, Ordering::SeqCst);
        }
    }

    pub fn width(&self) -> i32 {
        self.base.width()
    }

    pub fn height(&self) -> i32 {
        self.base.height()
    }
}

impl TextureWriterLike for D3D11TextureWriter {
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
        D3D11TextureWriter::read_planes(self, source);
    }

    fn write(&mut self, force: bool) {
        D3D11TextureWriter::write(self, force);
    }
}

unsafe impl Send for D3D11TextureWriter {}
unsafe impl Sync for D3D11TextureWriter {}
