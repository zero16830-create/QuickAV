#![allow(non_snake_case)]

use crate::Frame::Frame;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AudioSampleFormat {
    Unknown = 0,
    F32 = 1,
}

pub struct AudioFrame {
    _frame: Frame,
    _sampleRate: i32,
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
            _sampleRate: 0,
            _channels: 0,
            _samples: 0,
            _time: 0.0,
            _duration: 0.0,
            _data: Vec::new(),
        };
        frame.EnsureLayout(sample_rate, channels, samples);
        frame
    }

    pub fn OnRecycle(&mut self) {
        self._frame.OnRecycle();
        self._time = 0.0;
        self._duration = 0.0;
    }

    pub fn EnsureLayout(&mut self, sample_rate: i32, channels: i32, samples: i32) {
        self._sampleRate = sample_rate.max(0);
        self._channels = channels.max(0);
        self._samples = samples.max(0);

        let length = self._channels as usize
            * self._samples as usize
            * Self::BYTES_PER_SAMPLE;
        if self._data.len() != length {
            self._data.resize(length, 0);
        }
    }

    pub fn SetAsEOF(&mut self) {
        self._frame.SetAsEOF();
        self._duration = 0.0;
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

    pub fn SetDuration(&mut self, duration: f64) {
        self._duration = duration.max(0.0);
    }

    pub fn Duration(&self) -> f64 {
        self._duration
    }

    pub fn EndTime(&self) -> f64 {
        self._time + self._duration
    }

    pub fn SampleRate(&self) -> i32 {
        self._sampleRate
    }

    pub fn Channels(&self) -> i32 {
        self._channels
    }

    pub fn Samples(&self) -> i32 {
        self._samples
    }

    pub fn DataLength(&self) -> usize {
        self._data.len()
    }

    pub fn SampleFormat(&self) -> AudioSampleFormat {
        Self::SAMPLE_FORMAT
    }

    pub fn Buffer(&self) -> &[u8] {
        self._data.as_slice()
    }

    pub fn BufferMut(&mut self) -> &mut [u8] {
        self._data.as_mut_slice()
    }
}
