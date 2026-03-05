#![allow(non_snake_case)]
use ffmpeg_next::util::frame::Video as AVFrame;

pub struct AVLibFrame {
    pub _frame: AVFrame,
}

impl AVLibFrame {
    pub fn new() -> Self {
        Self {
            _frame: AVFrame::empty(),
        }
    }

    pub fn Frame(&mut self) -> &mut AVFrame {
        &mut self._frame
    }

    pub fn Clean(&mut self) {
        unsafe {
            ffmpeg_next::ffi::av_frame_unref(self._frame.as_mut_ptr());
        }
    }
}
