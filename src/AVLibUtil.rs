#![allow(non_snake_case)]
use crate::PixelFormat::PixelFormat;
use ffmpeg_next::ffi::AVMediaType;
use ffmpeg_next::format::{context::Input, Pixel};
use ffmpeg_next::media::Type;
use lazy_static::lazy_static;
use std::collections::HashMap;
use std::sync::Mutex;

pub const SECOND_TO_MICROSECOND: f64 = 1_000_000.0;
pub const MICROSECOND_TO_SECOND: f64 = 1.0 / SECOND_TO_MICROSECOND;

pub fn SecondsToMicroseconds(seconds: f64) -> i64 {
    (seconds * SECOND_TO_MICROSECOND) as i64
}

pub fn MicrosecondsToSeconds(microseconds: i64) -> f64 {
    microseconds as f64 * MICROSECOND_TO_SECOND
}

pub fn PixelFormatFromFFmpeg(pixel_format: Pixel) -> PixelFormat {
    match pixel_format {
        Pixel::YUV420P => PixelFormat::PIXEL_FORMAT_YUV420P,
        Pixel::RGBA => PixelFormat::PIXEL_FORMAT_RGBA32,
        _ => PixelFormat::PIXEL_FORMAT_NONE,
    }
}

pub fn PixelFormatToFFmpeg(pixel_format: PixelFormat) -> Pixel {
    match pixel_format {
        PixelFormat::PIXEL_FORMAT_YUV420P => Pixel::YUV420P,
        PixelFormat::PIXEL_FORMAT_RGBA32 => Pixel::RGBA,
        _ => Pixel::None,
    }
}

pub fn ToAVPixelFormat(pixel_format: PixelFormat) -> Pixel {
    PixelFormatToFFmpeg(pixel_format)
}

pub fn ToPixelFormat(pixel_format: Pixel) -> PixelFormat {
    PixelFormatFromFFmpeg(pixel_format)
}

fn TypeToAVMediaType(media_type: Type) -> AVMediaType {
    match media_type {
        Type::Video => AVMediaType::AVMEDIA_TYPE_VIDEO,
        Type::Audio => AVMediaType::AVMEDIA_TYPE_AUDIO,
        Type::Data => AVMediaType::AVMEDIA_TYPE_DATA,
        Type::Subtitle => AVMediaType::AVMEDIA_TYPE_SUBTITLE,
        Type::Attachment => AVMediaType::AVMEDIA_TYPE_ATTACHMENT,
        _ => AVMediaType::AVMEDIA_TYPE_UNKNOWN,
    }
}

fn AVMediaTypeToType(media_type: AVMediaType) -> Type {
    match media_type {
        AVMediaType::AVMEDIA_TYPE_VIDEO => Type::Video,
        AVMediaType::AVMEDIA_TYPE_AUDIO => Type::Audio,
        AVMediaType::AVMEDIA_TYPE_DATA => Type::Data,
        AVMediaType::AVMEDIA_TYPE_SUBTITLE => Type::Subtitle,
        AVMediaType::AVMEDIA_TYPE_ATTACHMENT => Type::Attachment,
        _ => Type::Unknown,
    }
}

pub fn BestStreamIndexEx(
    input: &Input,
    media_type: AVMediaType,
    desired_stream: i32,
    video_stream: i32,
    audio_stream: i32,
) -> i32 {
    let related_stream = match media_type {
        AVMediaType::AVMEDIA_TYPE_VIDEO => video_stream,
        AVMediaType::AVMEDIA_TYPE_AUDIO => video_stream,
        AVMediaType::AVMEDIA_TYPE_SUBTITLE => audio_stream,
        _ => -1,
    };

    let result = unsafe {
        ffmpeg_next::ffi::av_find_best_stream(
            input.as_ptr() as *mut ffmpeg_next::ffi::AVFormatContext,
            media_type,
            desired_stream,
            related_stream,
            std::ptr::null_mut(),
            0,
        )
    };

    if result >= 0 {
        result
    } else {
        -1
    }
}

pub fn BestStreamIndex(input: &Input, media_type: Type) -> i32 {
    BestStreamIndexEx(input, TypeToAVMediaType(media_type), -1, -1, -1)
}

pub fn BestVideoStreamIndex(input: &Input) -> i32 {
    BestStreamIndex(input, Type::Video)
}

pub fn BestAudioStreamIndex(input: &Input) -> i32 {
    BestStreamIndex(input, Type::Audio)
}

pub fn BestStreamIndices(input: &Input) -> HashMap<AVMediaType, i32> {
    let mut stream_count_for_media: HashMap<AVMediaType, i32> = HashMap::new();
    let mut stream_for_type: HashMap<AVMediaType, i32> = HashMap::new();

    for stream in input.streams() {
        let media_type = TypeToAVMediaType(stream.parameters().medium());
        let count = stream_count_for_media.entry(media_type).or_insert(0);
        *count += 1;
        stream_for_type.insert(media_type, stream.index() as i32);
    }

    let video_stream = BestVideoStreamIndex(input);
    let audio_stream = BestAudioStreamIndex(input);

    let mut result = HashMap::new();
    for (media_type, count) in stream_count_for_media.iter() {
        let desired_stream = if *count == 1 {
            *stream_for_type.get(media_type).unwrap_or(&-1)
        } else {
            -1
        };

        let best = BestStreamIndexEx(
            input,
            *media_type,
            desired_stream,
            video_stream,
            audio_stream,
        );
        result.insert(*media_type, best);
    }

    result
}

lazy_static! {
    pub static ref FFMPEG_OPEN_LOCK: Mutex<()> = Mutex::new(());
}
