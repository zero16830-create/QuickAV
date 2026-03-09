use crate::pixel_format::PixelFormat;

pub trait VideoDescription {
    fn width(&self) -> i32;
    fn height(&self) -> i32;
    fn format(&self) -> PixelFormat;
}

pub fn compatible(a: &dyn VideoDescription, b: &dyn VideoDescription) -> bool {
    a.width() == b.width() && a.height() == b.height() && a.format() == b.format()
}

impl dyn VideoDescription {
    pub fn compatible(a: &dyn VideoDescription, b: &dyn VideoDescription) -> bool {
        compatible(a, b)
    }
}
