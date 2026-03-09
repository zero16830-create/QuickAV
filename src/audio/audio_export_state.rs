use crate::audio_frame::{AudioFrame, AudioSampleFormat};
use std::collections::VecDeque;
use std::sync::{Arc, Mutex};

#[derive(Clone, Copy)]
pub struct ExportedAudioMeta {
    pub sample_rate: i32,
    pub channels: i32,
    pub bytes_per_sample: i32,
    pub sample_format: i32,
    pub buffered_bytes: i32,
    pub time: f64,
    pub frame_index: i64,
    pub has_audio: bool,
}

struct QueuedAudioChunk {
    time_sec: f64,
    data: Vec<u8>,
    offset: usize,
}

pub struct ExportedAudioState {
    sample_rate: i32,
    channels: i32,
    bytes_per_sample: i32,
    sample_format: i32,
    buffered_bytes: usize,
    time_sec: f64,
    frame_index: i64,
    has_audio: bool,
    chunks: VecDeque<QueuedAudioChunk>,
}

pub type SharedExportedAudioState = Arc<Mutex<ExportedAudioState>>;

impl ExportedAudioState {
    const MAX_BUFFERED_BYTES: usize = 2 * 1024 * 1024;

    pub fn new() -> Self {
        Self {
            sample_rate: 0,
            channels: 0,
            bytes_per_sample: AudioFrame::BYTES_PER_SAMPLE as i32,
            sample_format: AudioSampleFormat::F32 as i32,
            buffered_bytes: 0,
            time_sec: 0.0,
            frame_index: 0,
            has_audio: false,
            chunks: VecDeque::new(),
        }
    }

    pub fn shared() -> SharedExportedAudioState {
        Arc::new(Mutex::new(Self::new()))
    }

    pub fn meta(&self) -> ExportedAudioMeta {
        ExportedAudioMeta {
            sample_rate: self.sample_rate,
            channels: self.channels,
            bytes_per_sample: self.bytes_per_sample,
            sample_format: self.sample_format,
            buffered_bytes: self.buffered_bytes as i32,
            time: self
                .chunks
                .front()
                .map(|chunk| {
                    Self::chunk_time(
                        chunk,
                        self.sample_rate,
                        self.channels,
                        self.bytes_per_sample,
                    )
                })
                .unwrap_or(self.time_sec),
            frame_index: self.frame_index,
            has_audio: self.has_audio,
        }
    }

    pub fn flush(&mut self) {
        self.buffered_bytes = 0;
        self.has_audio = false;
        self.chunks.clear();
    }

    pub fn push_frame(&mut self, frame: &AudioFrame) {
        if frame.is_eof()
            || frame.sample_rate() <= 0
            || frame.channels() <= 0
            || frame.data_len() == 0
        {
            return;
        }

        let frame_sample_format = frame.sample_format() as i32;
        if self.sample_rate != frame.sample_rate()
            || self.channels != frame.channels()
            || self.sample_format != frame_sample_format
        {
            self.sample_rate = frame.sample_rate();
            self.channels = frame.channels();
            self.sample_format = frame_sample_format;
            self.buffered_bytes = 0;
            self.chunks.clear();
        }

        let data = frame.buffer();
        self.chunks.push_back(QueuedAudioChunk {
            time_sec: frame.time(),
            data: data.to_vec(),
            offset: 0,
        });
        self.buffered_bytes += data.len();
        self.time_sec = frame.time();
        self.frame_index = self.frame_index.saturating_add(1);
        self.has_audio = true;

        while self.buffered_bytes > Self::MAX_BUFFERED_BYTES {
            let Some(chunk) = self.chunks.pop_front() else {
                break;
            };

            let remaining = chunk.data.len().saturating_sub(chunk.offset);
            self.buffered_bytes = self.buffered_bytes.saturating_sub(remaining);
        }

        self.has_audio = !self.chunks.is_empty();
    }

    pub fn copy_to(&mut self, destination: &mut [u8]) -> i32 {
        if destination.is_empty() || self.chunks.is_empty() {
            self.has_audio = !self.chunks.is_empty();
            return 0;
        }

        let mut written = 0usize;
        while written < destination.len() {
            let Some(chunk) = self.chunks.front_mut() else {
                break;
            };

            let remaining = chunk.data.len().saturating_sub(chunk.offset);
            if remaining == 0 {
                let _ = self.chunks.pop_front();
                continue;
            }

            let to_copy = (destination.len() - written).min(remaining);
            destination[written..written + to_copy]
                .copy_from_slice(&chunk.data[chunk.offset..chunk.offset + to_copy]);

            chunk.offset += to_copy;
            written += to_copy;
            self.buffered_bytes = self.buffered_bytes.saturating_sub(to_copy);
            self.time_sec = Self::chunk_time(
                chunk,
                self.sample_rate,
                self.channels,
                self.bytes_per_sample,
            );

            if chunk.offset >= chunk.data.len() {
                self.time_sec = chunk.time_sec
                    + Self::bytes_to_seconds(
                        chunk.data.len(),
                        self.sample_rate,
                        self.channels,
                        self.bytes_per_sample,
                    );
                let _ = self.chunks.pop_front();
            } else {
                break;
            }
        }

        self.has_audio = !self.chunks.is_empty();
        written as i32
    }

    fn chunk_time(
        chunk: &QueuedAudioChunk,
        sample_rate: i32,
        channels: i32,
        bytes_per_sample: i32,
    ) -> f64 {
        chunk.time_sec
            + Self::bytes_to_seconds(chunk.offset, sample_rate, channels, bytes_per_sample)
    }

    fn bytes_to_seconds(
        bytes: usize,
        sample_rate: i32,
        channels: i32,
        bytes_per_sample: i32,
    ) -> f64 {
        let bytes_per_second = sample_rate as f64 * channels as f64 * bytes_per_sample as f64;
        if bytes_per_second <= 0.0 {
            0.0
        } else {
            bytes as f64 / bytes_per_second
        }
    }
}
