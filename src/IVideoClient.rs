#![allow(non_snake_case)]

use crate::IVideoDescription::IVideoDescription;
use crate::VideoFrame::VideoFrame;

pub trait IVideoClient: IVideoDescription {
    fn OnFrameReady(&mut self, frame: &mut VideoFrame);
    fn Write(&mut self);
}
