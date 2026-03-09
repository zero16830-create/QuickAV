use crate::video_description::VideoDescription;
use crate::video_frame::VideoFrame;

pub trait VideoClient: VideoDescription {
    fn on_frame_ready(&mut self, frame: &mut VideoFrame);
    fn write(&mut self);
}
