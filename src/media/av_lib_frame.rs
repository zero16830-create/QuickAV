use ffmpeg_next::util::frame::Video as AVFrame;

pub struct AvLibFrame {
    pub frame: AVFrame,
}

impl AvLibFrame {
    pub fn new() -> Self {
        Self {
            frame: AVFrame::empty(),
        }
    }

    pub fn frame(&mut self) -> &mut AVFrame {
        &mut self.frame
    }

    pub fn clean(&mut self) {
        unsafe {
            ffmpeg_next::ffi::av_frame_unref(self.frame.as_mut_ptr());
        }
    }
}
