#![allow(non_snake_case)]

use sdl2_sys as sdl;
use std::ffi::CString;

pub struct SDLWindow {
    _window: *mut sdl::SDL_Window,
    _renderer: *mut sdl::SDL_Renderer,
    _surface: *mut sdl::SDL_Surface,
    _window_id: u32,
}

impl SDLWindow {
    pub fn new(title: String, x: i32, y: i32, w: i32, h: i32) -> Option<Self> {
        let c_title = CString::new(title).ok()?;

        let window = unsafe {
            sdl::SDL_CreateWindow(
                c_title.as_ptr(),
                x,
                y,
                w,
                h,
                sdl::SDL_WindowFlags::SDL_WINDOW_SHOWN as u32,
            )
        };

        if window.is_null() {
            return None;
        }

        let renderer = unsafe { sdl::SDL_CreateRenderer(window, -1, 0) };
        if renderer.is_null() {
            unsafe {
                sdl::SDL_DestroyWindow(window);
            }
            return None;
        }

        let surface = unsafe { sdl::SDL_GetWindowSurface(window) };
        if surface.is_null() {
            unsafe {
                sdl::SDL_DestroyRenderer(renderer);
                sdl::SDL_DestroyWindow(window);
            }
            return None;
        }

        let window_id = unsafe { sdl::SDL_GetWindowID(window) };

        Some(Self {
            _window: window,
            _renderer: renderer,
            _surface: surface,
            _window_id: window_id,
        })
    }

    pub fn WindowId(&self) -> u32 {
        self._window_id
    }

    pub fn HandleEvent(&mut self, _event: sdl::SDL_Event) {
        // parity placeholder
    }

    pub fn Window(&self) -> *mut sdl::SDL_Window {
        self._window
    }

    pub fn Renderer(&self) -> *mut sdl::SDL_Renderer {
        self._renderer
    }

    pub fn Surface(&self) -> *mut sdl::SDL_Surface {
        self._surface
    }

    pub fn Width(&self) -> i32 {
        unsafe {
            if self._surface.is_null() {
                0
            } else {
                (*self._surface).w
            }
        }
    }

    pub fn Height(&self) -> i32 {
        unsafe {
            if self._surface.is_null() {
                0
            } else {
                (*self._surface).h
            }
        }
    }
}

impl Drop for SDLWindow {
    fn drop(&mut self) {
        unsafe {
            if !self._window.is_null() {
                sdl::SDL_DestroyWindow(self._window);
                self._window = std::ptr::null_mut();
            }

            if !self._renderer.is_null() {
                sdl::SDL_DestroyRenderer(self._renderer);
                self._renderer = std::ptr::null_mut();
            }
        }

        self._surface = std::ptr::null_mut();
    }
}
