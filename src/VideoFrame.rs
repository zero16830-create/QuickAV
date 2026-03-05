#![allow(non_snake_case)]
use crate::Frame::Frame;
use crate::IFrameVisitor::IFrameVisitor;
use crate::IVideoDescription::IVideoDescription;
use crate::PixelFormat::PixelFormat;

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
    pub fn OnRecycle(&mut self) {
        self._frame.OnRecycle();
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
            PixelFormat::PIXEL_FORMAT_YUV420P => {
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
            PixelFormat::PIXEL_FORMAT_RGBA32 => {
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

    pub fn SetAsEOF(&mut self) {
        self._frame.SetAsEOF();
    }

    pub fn ClearEOF(&mut self) {
        self._frame.OnRecycle();
    }

    pub fn IsEOF(&self) -> bool {
        self._frame.IsEOF()
    }

    pub fn SetTime(&mut self, time: f64) {
        self._time = time;
    }

    pub fn Time(&self) -> f64 {
        self._time
    }

    pub fn Width(&self) -> i32 {
        self._width
    }

    pub fn Height(&self) -> i32 {
        self._height
    }

    pub fn Format(&self) -> PixelFormat {
        self._format
    }

    pub fn BufferCount(&self) -> i32 {
        self._sizes.len() as i32
    }

    pub fn Size(&self, index: i32) -> i32 {
        if index < 0 || index as usize >= self._sizes.len() {
            return -1;
        }
        self._sizes[index as usize]
    }

    pub fn Stride(&self, index: i32) -> i32 {
        if index < 0 || index as usize >= self._strides.len() {
            return -1;
        }
        self._strides[index as usize]
    }

    pub fn Strides(&self) -> &[i32] {
        &self._strides
    }

    pub fn Buffers(&self) -> Vec<&[u8]> {
        let mut result = Vec::new();
        if !self._data.is_empty() {
            result.push(self._data.as_slice());
        }
        for b in self._extra_buffers.iter() {
            result.push(b.as_slice());
        }
        result
    }

    pub fn Buffer(&self, index: usize) -> Option<&[u8]> {
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

    pub fn BufferMut(&mut self, index: usize) -> Option<&mut [u8]> {
        if index == 0 {
            return Some(self._data.as_mut_slice());
        }

        let extra = index - 1;
        self._extra_buffers.get_mut(extra).map(|v| v.as_mut_slice())
    }

    pub fn Accept(&mut self, visitor: &mut dyn IFrameVisitor) {
        visitor.Visit(self);
    }
}

impl IVideoDescription for VideoFrame {
    fn Width(&self) -> i32 {
        VideoFrame::Width(self)
    }

    fn Height(&self) -> i32 {
        VideoFrame::Height(self)
    }

    fn Format(&self) -> PixelFormat {
        VideoFrame::Format(self)
    }
}
