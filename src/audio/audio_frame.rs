use crate::frame::Frame;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AudioSampleFormat {
    Unknown = 0,
    F32 = 1,
}

pub struct AudioFrame {
    _frame: Frame,
    _sample_rate: i32,
    _channels: i32,
    _samples: i32,
    _time: f64,
    _duration: f64,
    _data: Vec<u8>,
}

impl AudioFrame {
    pub const BYTES_PER_SAMPLE: usize = std::mem::size_of::<f32>();
    pub const SAMPLE_FORMAT: AudioSampleFormat = AudioSampleFormat::F32;

    pub fn new(sample_rate: i32, channels: i32, samples: i32) -> Self {
        let mut frame = Self {
            _frame: Frame::new(),
            _sample_rate: 0,
            _channels: 0,
            _samples: 0,
            _time: 0.0,
            _duration: 0.0,
            _data: Vec::new(),
        };
        frame.ensure_layout(sample_rate, channels, samples);
        frame
    }

    pub fn on_recycle(&mut self) {
        self._frame.on_recycle();
        self._time = 0.0;
        self._duration = 0.0;
    }

    pub fn ensure_layout(&mut self, sample_rate: i32, channels: i32, samples: i32) {
        self._sample_rate = sample_rate.max(0);
        self._channels = channels.max(0);
        self._samples = samples.max(0);

        let length = self._channels as usize * self._samples as usize * Self::BYTES_PER_SAMPLE;
        if self._data.len() != length {
            self._data.resize(length, 0);
        }
    }

    pub fn set_eof(&mut self) {
        self._frame.set_eof();
        self._duration = 0.0;
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

    pub fn set_duration(&mut self, duration: f64) {
        self._duration = duration.max(0.0);
    }

    pub fn duration(&self) -> f64 {
        self._duration
    }

    pub fn end_time(&self) -> f64 {
        self._time + self._duration
    }

    pub fn sample_rate(&self) -> i32 {
        self._sample_rate
    }

    pub fn channels(&self) -> i32 {
        self._channels
    }

    pub fn samples(&self) -> i32 {
        self._samples
    }

    pub fn data_len(&self) -> usize {
        self._data.len()
    }

    pub fn sample_format(&self) -> AudioSampleFormat {
        Self::SAMPLE_FORMAT
    }

    pub fn buffer(&self) -> &[u8] {
        self._data.as_slice()
    }

    pub fn buffer_mut(&mut self) -> &mut [u8] {
        self._data.as_mut_slice()
    }
}
