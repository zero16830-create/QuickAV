use crate::av_lib_audio_decoder::AvLibAudioDecoder;
use crate::av_lib_decoder_visitor::AvLibDecoderVisitor;
use crate::av_lib_source::AvLibSource;
use crate::av_lib_video_decoder::AvLibVideoDecoder;
use crate::video_description::VideoDescription;
use crate::video_frame::VideoFrame;
use ffmpeg_next::ffi::AVMediaType;
use std::sync::{Arc, Mutex};

pub trait AvLibDecoderTrait {
    fn accept(&self, visitor: &mut dyn crate::av_lib_decoder_visitor::AvLibDecoderVisitor);
    fn time_base(&self) -> f64;
    fn frame_rate(&self) -> f64;
    fn frame_duration(&self) -> f64;
}

pub struct AvLibDecoder {
    decoder: Arc<Mutex<AvLibVideoDecoder>>,
    pub time_base: f64,
    pub frame_rate: f64,
    pub frame_duration: f64,
    pub stream_index: i32,
}

impl AvLibDecoder {
    pub fn create_video(
        source: Arc<Mutex<Box<dyn AvLibSource + Send>>>,
        required_video_desc: &dyn VideoDescription,
    ) -> Vec<Arc<AvLibDecoder>> {
        let stream_indices = if let Ok(s) = source.lock() {
            let mut found = Vec::new();
            let count = s.stream_count();
            for i in 0..count {
                if s.stream_type(i) == AVMediaType::AVMEDIA_TYPE_VIDEO {
                    found.push(i);
                }
            }
            found
        } else {
            Vec::new()
        };

        let mut decoders = Vec::new();
        for stream_index in stream_indices {
            let (time_base, frame_rate, frame_duration, decoder_opt) = if let Ok(s) = source.lock()
            {
                (
                    s.time_base(stream_index),
                    s.frame_rate(stream_index),
                    s.frame_duration(stream_index),
                    s.create_video_decoder(stream_index),
                )
            } else {
                continue;
            };

            let Some(video_decoder) = decoder_opt else {
                continue;
            };

            let decoder = Arc::new(Mutex::new(AvLibVideoDecoder::new(
                source.clone(),
                stream_index,
                required_video_desc,
                video_decoder,
                time_base,
            )));

            decoders.push(Arc::new(Self {
                decoder,
                time_base,
                frame_rate,
                frame_duration,
                stream_index,
            }));
        }

        decoders
    }

    pub fn create(
        source: Arc<Mutex<Box<dyn AvLibSource + Send>>>,
        required_video_desc: &dyn VideoDescription,
    ) -> Vec<Arc<AvLibDecoder>> {
        Self::create_video(source, required_video_desc)
    }

    pub fn create_audio(
        source: Arc<Mutex<Box<dyn AvLibSource + Send>>>,
    ) -> Option<Arc<AvLibAudioDecoder>> {
        let stream_index = if let Ok(s) = source.lock() {
            let mut found: Option<i32> = None;
            let count = s.stream_count();
            for i in 0..count {
                if s.stream_type(i) == AVMediaType::AVMEDIA_TYPE_AUDIO {
                    found = Some(i);
                    break;
                }
            }
            found
        } else {
            None
        }?;

        let (time_base, decoder_opt) = if let Ok(s) = source.lock() {
            (
                s.time_base(stream_index),
                s.create_audio_decoder(stream_index),
            )
        } else {
            return None;
        };

        let audio_decoder = decoder_opt?;
        Some(Arc::new(AvLibAudioDecoder::new(
            source,
            stream_index,
            audio_decoder,
            time_base,
        )))
    }

    pub fn try_get_next(&self, time: f64) -> Option<VideoFrame> {
        if let Ok(decoder) = self.decoder.lock() {
            decoder.try_get_next(time)
        } else {
            None
        }
    }

    pub fn recycle(&self, frame: VideoFrame) {
        if let Ok(decoder) = self.decoder.lock() {
            decoder.recycle(frame);
        }
    }

    pub fn needs_recreate(&self) -> bool {
        if let Ok(decoder) = self.decoder.lock() {
            decoder.needs_recreate()
        } else {
            false
        }
    }

    pub fn flush_realtime_frames(&self) {
        if let Ok(decoder) = self.decoder.lock() {
            decoder.flush_realtime_frames();
        }
    }

    pub fn flush_frames(&self) {
        if let Ok(decoder) = self.decoder.lock() {
            decoder.flush_frames();
        }
    }

    pub fn dropped_frame_count(&self) -> u64 {
        if let Ok(decoder) = self.decoder.lock() {
            decoder.dropped_frame_count()
        } else {
            0
        }
    }

    pub fn actual_source_video_size(&self) -> Option<(i32, i32)> {
        if let Ok(decoder) = self.decoder.lock() {
            decoder.actual_source_video_size()
        } else {
            None
        }
    }

    pub fn time_base(&self) -> f64 {
        self.time_base
    }

    pub fn frame_rate(&self) -> f64 {
        self.frame_rate
    }

    pub fn frame_duration(&self) -> f64 {
        self.frame_duration
    }
}

impl AvLibDecoderTrait for AvLibDecoder {
    fn accept(&self, visitor: &mut dyn AvLibDecoderVisitor) {
        if let Ok(mut decoder) = self.decoder.lock() {
            visitor.visit(&mut decoder);
        }
    }

    fn time_base(&self) -> f64 {
        self.time_base()
    }

    fn frame_rate(&self) -> f64 {
        self.frame_rate()
    }

    fn frame_duration(&self) -> f64 {
        self.frame_duration()
    }
}
