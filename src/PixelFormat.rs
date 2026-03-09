#![allow(non_snake_case)]

#[repr(i32)]
#[allow(non_camel_case_types)]
#[derive(Clone, Copy, PartialEq, Eq)]
pub enum PixelFormat {
    PIXEL_FORMAT_NONE = -1,
    PIXEL_FORMAT_YUV420P = 0,
    PIXEL_FORMAT_RGBA32 = 1,
}
