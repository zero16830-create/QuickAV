#[repr(i32)]
#[derive(Clone, Copy, PartialEq, Eq)]
pub enum PixelFormat {
    Unknown = -1,
    Yuv420p = 0,
    Rgba32 = 1,
}
