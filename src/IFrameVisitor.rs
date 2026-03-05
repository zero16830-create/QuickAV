#![allow(non_snake_case)]
use crate::VideoFrame::VideoFrame;

pub trait IFrameVisitor {
    fn Visit(&mut self, frame: &mut VideoFrame);
}
