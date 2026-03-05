#![allow(non_snake_case)]

use crate::Logging::Debug::Debug;
use crate::PixelFormat::PixelFormat;
use crate::Rendering::TextureWriter::{TextureWriter, TextureWriterLike};
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
            Debug::LogError("D3D11TextureWriter::new - target texture was null");
            return None;
        }

        let texture = match ID3D11Texture2D::from_raw_borrowed(&target_texture) {
            Some(t) => t.clone(),
            None => {
                Debug::LogError("D3D11TextureWriter::new - could not resolve ID3D11Texture2D");
                return None;
            }
        };

        let mut desc = D3D11_TEXTURE2D_DESC::default();
        texture.GetDesc(&mut desc);

        let device = match texture.GetDevice() {
            Ok(d) => d,
            Err(_) => {
                Debug::LogError("D3D11TextureWriter::new - failed to get device");
                return None;
            }
        };

        if desc.Format != DXGI_FORMAT_R8G8B8A8_UNORM {
            Debug::LogError("D3D11TextureWriter::new - texture format is not RGBA32");
            return None;
        }

        let mut base = TextureWriter::new();
        base.TargetWidth = desc.Width as i32;
        base.TargetHeight = desc.Height as i32;
        base.TargetFormat = PixelFormat::PIXEL_FORMAT_RGBA32;
        base.BufferStrides.push(base.TargetWidth * 4);
        base.BufferSizes
            .push(base.BufferStrides[0] * base.TargetHeight);
        base.Buffers.push(vec![0u8; base.BufferSizes[0] as usize]);
        base.Ready.store(true, Ordering::SeqCst);
        base.Changed.store(false, Ordering::SeqCst);

        Some(Self {
            base,
            texture,
            device,
        })
    }

    pub fn Read(&mut self, data: &[u8]) {
        self.base.Read(data);
    }

    pub fn ReadPlanes(&mut self, planes: &[&[u8]]) {
        self.base.ReadPlanes(planes);
    }

    pub fn Write(&mut self, force: bool) {
        if !self.base.Ready.load(Ordering::SeqCst) {
            return;
        }

        if force || self.base.Changed.load(Ordering::SeqCst) {
            if self.base.Buffers.is_empty() || self.base.BufferStrides.is_empty() {
                return;
            }

            let _lock = self.base.BufferMutex.lock().unwrap();
            match unsafe { self.device.GetImmediateContext() } {
                Ok(context) => unsafe {
                    context.UpdateSubresource(
                        &self.texture,
                        0,
                        None,
                        self.base.Buffers[0].as_ptr() as *const c_void,
                        self.base.BufferStrides[0] as u32,
                        0,
                    );
                },
                Err(_) => {
                    Debug::LogError("D3D11TextureWriter::Write - failed to get immediate context");
                    return;
                }
            }

            self.base.Changed.store(false, Ordering::SeqCst);
        }
    }

    pub fn Width(&self) -> i32 {
        self.base.Width()
    }

    pub fn Height(&self) -> i32 {
        self.base.Height()
    }
}

impl TextureWriterLike for D3D11TextureWriter {
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
        D3D11TextureWriter::ReadPlanes(self, source);
    }

    fn Write(&mut self, force: bool) {
        D3D11TextureWriter::Write(self, force);
    }
}

unsafe impl Send for D3D11TextureWriter {}
unsafe impl Sync for D3D11TextureWriter {}
