#![allow(non_snake_case)]
use crate::Logging::Debug::Debug;
use crate::PixelFormat::PixelFormat;
use crate::Rendering::TextureWriter::{TextureWriter, TextureWriterLike};
use crate::SDLWindow::SDLWindow;
use sdl2_sys as sdl;
use std::ptr;

pub struct SDLWindowWriter {
    pub(crate) base: TextureWriter,
    _window_id: u32,
    _renderer: *mut sdl::SDL_Renderer,
    _texture: *mut sdl::SDL_Texture,
}

impl SDLWindowWriter {
    const K_DEFAULT_BPP: i32 = 4;

    pub fn new(window: &SDLWindow) -> Self {
        let mut base = TextureWriter::new();
        base.TargetWidth = window.Width();
        base.TargetHeight = window.Height();
        base.TargetFormat = PixelFormat::PIXEL_FORMAT_RGBA32;
        base.BufferStrides
            .push(base.TargetWidth * Self::K_DEFAULT_BPP);
        base.BufferSizes
            .push(base.BufferStrides[0] * base.TargetHeight);
        base.Buffers.push(vec![0u8; base.BufferSizes[0] as usize]);
        base.Ready.store(true, std::sync::atomic::Ordering::SeqCst);

        let renderer = window.Renderer();
        let texture = if renderer.is_null() {
            ptr::null_mut()
        } else {
            unsafe {
                sdl::SDL_CreateTexture(
                    renderer,
                    sdl::SDL_PixelFormatEnum::SDL_PIXELFORMAT_RGBA32 as u32,
                    sdl::SDL_TextureAccess::SDL_TEXTUREACCESS_STREAMING as i32,
                    base.TargetWidth,
                    base.TargetHeight,
                )
            }
        };

        if texture.is_null() {
            Debug::LogError("SDLWindowWriter: failed to create SDL texture");
        }

        Self {
            base,
            _window_id: window.WindowId(),
            _renderer: renderer,
            _texture: texture,
        }
    }

    pub fn Write(&mut self, force: bool) {
        if !(force || self.base.Changed.load(std::sync::atomic::Ordering::SeqCst)) {
            return;
        }

        if self._renderer.is_null() || self._texture.is_null() || self.base.Buffers.is_empty() {
            return;
        }

        let _lock = self.base.BufferMutex.lock().unwrap();
        unsafe {
            sdl::SDL_RenderClear(self._renderer);
            let update_result = sdl::SDL_UpdateTexture(
                self._texture,
                ptr::null(),
                self.base.Buffers[0].as_ptr() as *const _,
                self.base.BufferStrides[0],
            );

            if update_result != 0 {
                let err = sdl::SDL_GetError();
                if !err.is_null() {
                    let msg = std::ffi::CStr::from_ptr(err).to_string_lossy();
                    Debug::LogError(&format!(
                        "SDLWindowWriter: SDL_UpdateTexture failed: {}",
                        msg
                    ));
                } else {
                    Debug::LogError("SDLWindowWriter: SDL_UpdateTexture failed with unknown error");
                }
            }

            let copy_result =
                sdl::SDL_RenderCopy(self._renderer, self._texture, ptr::null(), ptr::null());
            if copy_result != 0 {
                let err = sdl::SDL_GetError();
                if !err.is_null() {
                    let msg = std::ffi::CStr::from_ptr(err).to_string_lossy();
                    Debug::LogError(&format!("SDLWindowWriter: SDL_RenderCopy failed: {}", msg));
                } else {
                    Debug::LogError("SDLWindowWriter: SDL_RenderCopy failed with unknown error");
                }
            }

            sdl::SDL_RenderPresent(self._renderer);
        }
    }
}

impl TextureWriterLike for SDLWindowWriter {
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
        SDLWindowWriter::Write(self, force);
    }
}

impl Drop for SDLWindowWriter {
    fn drop(&mut self) {
        unsafe {
            if !self._texture.is_null() {
                sdl::SDL_DestroyTexture(self._texture);
                self._texture = ptr::null_mut();
            }
        }
    }
}

unsafe impl Send for SDLWindowWriter {}
unsafe impl Sync for SDLWindowWriter {}
