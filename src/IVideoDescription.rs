#![allow(non_snake_case)]
use crate::PixelFormat::PixelFormat;

pub trait IVideoDescription {
    fn Width(&self) -> i32;
    fn Height(&self) -> i32;
    fn Format(&self) -> PixelFormat;
}

pub fn Compatible(a: &dyn IVideoDescription, b: &dyn IVideoDescription) -> bool {
    a.Width() == b.Width() && a.Height() == b.Height() && a.Format() == b.Format()
}

impl dyn IVideoDescription {
    pub fn Compatible(a: &dyn IVideoDescription, b: &dyn IVideoDescription) -> bool {
        Compatible(a, b)
    }
}
