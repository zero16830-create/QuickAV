#![allow(non_snake_case)]

use crate::AudioFrame::{AudioFrame, AudioSampleFormat};
use std::collections::VecDeque;
use std::sync::{Arc, Mutex};

#[derive(Clone, Copy)]
pub struct ExportedAudioMeta {
    pub SampleRate: i32,
    pub Channels: i32,
    pub BytesPerSample: i32,
    pub SampleFormat: i32,
    pub BufferedBytes: i32,
    pub Time: f64,
    pub FrameIndex: i64,
    pub HasAudio: bool,
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

    pub fn Shared() -> SharedExportedAudioState {
        Arc::new(Mutex::new(Self::new()))
    }

    pub fn Meta(&self) -> ExportedAudioMeta {
        ExportedAudioMeta {
            SampleRate: self.sample_rate,
            Channels: self.channels,
            BytesPerSample: self.bytes_per_sample,
            SampleFormat: self.sample_format,
            BufferedBytes: self.buffered_bytes as i32,
            Time: self
                .chunks
                .front()
                .map(|chunk| {
                    Self::ChunkTime(chunk, self.sample_rate, self.channels, self.bytes_per_sample)
                })
                .unwrap_or(self.time_sec),
            FrameIndex: self.frame_index,
            HasAudio: self.has_audio,
        }
    }

    pub fn Flush(&mut self) {
        self.buffered_bytes = 0;
        self.has_audio = false;
        self.chunks.clear();
    }

    pub fn PushFrame(&mut self, frame: &AudioFrame) {
        if frame.IsEOF()
            || frame.SampleRate() <= 0
            || frame.Channels() <= 0
            || frame.DataLength() == 0
        {
            return;
        }

        let frame_sample_format = frame.SampleFormat() as i32;
        if self.sample_rate != frame.SampleRate()
            || self.channels != frame.Channels()
            || self.sample_format != frame_sample_format
        {
            self.sample_rate = frame.SampleRate();
            self.channels = frame.Channels();
            self.sample_format = frame_sample_format;
            self.buffered_bytes = 0;
            self.chunks.clear();
        }

        let data = frame.Buffer();
        self.chunks.push_back(QueuedAudioChunk {
            time_sec: frame.Time(),
            data: data.to_vec(),
            offset: 0,
        });
        self.buffered_bytes += data.len();
        self.time_sec = frame.Time();
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

    pub fn CopyTo(&mut self, destination: &mut [u8]) -> i32 {
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
            self.time_sec = Self::ChunkTime(
                chunk,
                self.sample_rate,
                self.channels,
                self.bytes_per_sample,
            );

            if chunk.offset >= chunk.data.len() {
                self.time_sec = chunk.time_sec
                    + Self::BytesToSeconds(
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

    fn ChunkTime(
        chunk: &QueuedAudioChunk,
        sample_rate: i32,
        channels: i32,
        bytes_per_sample: i32,
    ) -> f64 {
        chunk.time_sec + Self::BytesToSeconds(chunk.offset, sample_rate, channels, bytes_per_sample)
    }

    fn BytesToSeconds(
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
