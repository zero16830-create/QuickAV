use crate::pixel_format::PixelFormat;
use ffmpeg_next::ffi::AVMediaType;
use ffmpeg_next::format::{context::Input, Pixel};
use ffmpeg_next::media::Type;
use lazy_static::lazy_static;
use std::collections::HashMap;
use std::sync::Mutex;

pub const SECOND_TO_MICROSECOND: f64 = 1_000_000.0;
pub const MICROSECOND_TO_SECOND: f64 = 1.0 / SECOND_TO_MICROSECOND;

pub fn seconds_to_microseconds(seconds: f64) -> i64 {
    (seconds * SECOND_TO_MICROSECOND) as i64
}

pub fn microseconds_to_seconds(microseconds: i64) -> f64 {
    microseconds as f64 * MICROSECOND_TO_SECOND
}

pub fn pixel_format_from_ffmpeg(pixel_format: Pixel) -> PixelFormat {
    match pixel_format {
        Pixel::YUV420P => PixelFormat::Yuv420p,
        Pixel::RGBA => PixelFormat::Rgba32,
        _ => PixelFormat::Unknown,
    }
}

pub fn pixel_format_to_ffmpeg(pixel_format: PixelFormat) -> Pixel {
    match pixel_format {
        PixelFormat::Yuv420p => Pixel::YUV420P,
        PixelFormat::Rgba32 => Pixel::RGBA,
        _ => Pixel::None,
    }
}

pub fn to_av_pixel_format(pixel_format: PixelFormat) -> Pixel {
    pixel_format_to_ffmpeg(pixel_format)
}

pub fn to_pixel_format(pixel_format: Pixel) -> PixelFormat {
    pixel_format_from_ffmpeg(pixel_format)
}

fn type_to_av_media_type(media_type: Type) -> AVMediaType {
    match media_type {
        Type::Video => AVMediaType::AVMEDIA_TYPE_VIDEO,
        Type::Audio => AVMediaType::AVMEDIA_TYPE_AUDIO,
        Type::Data => AVMediaType::AVMEDIA_TYPE_DATA,
        Type::Subtitle => AVMediaType::AVMEDIA_TYPE_SUBTITLE,
        Type::Attachment => AVMediaType::AVMEDIA_TYPE_ATTACHMENT,
        _ => AVMediaType::AVMEDIA_TYPE_UNKNOWN,
    }
}

pub fn best_stream_index_ex(
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

pub fn best_stream_index(input: &Input, media_type: Type) -> i32 {
    best_stream_index_ex(input, type_to_av_media_type(media_type), -1, -1, -1)
}

pub fn best_video_stream_index(input: &Input) -> i32 {
    best_stream_index(input, Type::Video)
}

pub fn best_audio_stream_index(input: &Input) -> i32 {
    best_stream_index(input, Type::Audio)
}

pub fn best_stream_indices(input: &Input) -> HashMap<AVMediaType, i32> {
    let mut stream_count_for_media: HashMap<AVMediaType, i32> = HashMap::new();
    let mut stream_for_type: HashMap<AVMediaType, i32> = HashMap::new();

    for stream in input.streams() {
        let media_type = type_to_av_media_type(stream.parameters().medium());
        let count = stream_count_for_media.entry(media_type).or_insert(0);
        *count += 1;
        stream_for_type.insert(media_type, stream.index() as i32);
    }

    let video_stream = best_video_stream_index(input);
    let audio_stream = best_audio_stream_index(input);

    let mut result = HashMap::new();
    for (media_type, count) in stream_count_for_media.iter() {
        let desired_stream = if *count == 1 {
            *stream_for_type.get(media_type).unwrap_or(&-1)
        } else {
            -1
        };

        let best = best_stream_index_ex(
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
