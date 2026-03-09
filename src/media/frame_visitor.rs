use crate::video_frame::VideoFrame;

pub trait FrameVisitor {
    fn visit(&mut self, frame: &mut VideoFrame);
}
