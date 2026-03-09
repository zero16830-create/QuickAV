use crate::frame::Frame;
use crate::frame_visitor::FrameVisitor;
use crate::pixel_format::PixelFormat;
use crate::video_description::VideoDescription;

pub struct VideoFrame {
    _frame: Frame,
    _width: i32,
    _height: i32,
    _format: PixelFormat,
    _data: Vec<u8>,
    _extra_buffers: Vec<Vec<u8>>,
    _time: f64,
    _sizes: Vec<i32>,
    _strides: Vec<i32>,
}

impl VideoFrame {
    pub fn on_recycle(&mut self) {
        self._frame.on_recycle();
    }
}

impl VideoFrame {
    pub fn new(width: i32, height: i32, format: PixelFormat) -> Self {
        let mut frame = Self {
            _frame: Frame::new(),
            _width: width,
            _height: height,
            _format: format,
            _data: Vec::new(),
            _extra_buffers: Vec::new(),
            _time: 0.0,
            _sizes: Vec::new(),
            _strides: Vec::new(),
        };

        match format {
            PixelFormat::Yuv420p => {
                let y_stride = width;
                let y_size = y_stride * height;
                frame._data = vec![0; y_size as usize];
                frame._sizes.push(y_size);
                frame._strides.push(y_stride);

                let uv_width = width / 2;
                let uv_stride = uv_width;
                let uv_size = uv_stride * height;
                frame._extra_buffers.push(vec![0; uv_size as usize]);
                frame._sizes.push(uv_size);
                frame._strides.push(uv_stride);

                frame._extra_buffers.push(vec![0; uv_size as usize]);
                frame._sizes.push(uv_size);
                frame._strides.push(uv_stride);
            }
            PixelFormat::Rgba32 => {
                let stride = width * 4;
                let size = stride * height;
                frame._data = vec![0; size as usize];
                frame._sizes.push(size);
                frame._strides.push(stride);
            }
            _ => {}
        }

        frame
    }

    pub fn set_eof(&mut self) {
        self._frame.set_eof();
    }

    pub fn clear_eof(&mut self) {
        self._frame.on_recycle();
    }

    pub fn is_eof(&self) -> bool {
        self._frame.is_eof()
    }

    pub fn set_time(&mut self, time: f64) {
        self._time = time;
    }

    pub fn time(&self) -> f64 {
        self._time
    }

    pub fn width(&self) -> i32 {
        self._width
    }

    pub fn height(&self) -> i32 {
        self._height
    }

    pub fn format(&self) -> PixelFormat {
        self._format
    }

    pub fn buffer_count(&self) -> i32 {
        self._sizes.len() as i32
    }

    pub fn size(&self, index: i32) -> i32 {
        if index < 0 || index as usize >= self._sizes.len() {
            return -1;
        }
        self._sizes[index as usize]
    }

    pub fn stride(&self, index: i32) -> i32 {
        if index < 0 || index as usize >= self._strides.len() {
            return -1;
        }
        self._strides[index as usize]
    }

    pub fn strides(&self) -> &[i32] {
        &self._strides
    }

    pub fn buffers(&self) -> Vec<&[u8]> {
        let mut result = Vec::new();
        if !self._data.is_empty() {
            result.push(self._data.as_slice());
        }
        for b in self._extra_buffers.iter() {
            result.push(b.as_slice());
        }
        result
    }

    pub fn buffer(&self, index: usize) -> Option<&[u8]> {
        if index == 0 {
            if self._data.is_empty() {
                None
            } else {
                Some(self._data.as_slice())
            }
        } else {
            self._extra_buffers.get(index - 1).map(|v| v.as_slice())
        }
    }

    pub fn buffer_mut(&mut self, index: usize) -> Option<&mut [u8]> {
        if index == 0 {
            return Some(self._data.as_mut_slice());
        }

        let extra = index - 1;
        self._extra_buffers.get_mut(extra).map(|v| v.as_mut_slice())
    }

    pub fn accept(&mut self, visitor: &mut dyn FrameVisitor) {
        visitor.visit(self);
    }
}

impl VideoDescription for VideoFrame {
    fn width(&self) -> i32 {
        VideoFrame::width(self)
    }

    fn height(&self) -> i32 {
        VideoFrame::height(self)
    }

    fn format(&self) -> PixelFormat {
        VideoFrame::format(self)
    }
}
