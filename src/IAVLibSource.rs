#![allow(non_snake_case)]

use crate::AVLibPacket::AVLibPacket;
use ffmpeg_next::ffi::AVMediaType;
use std::any::Any;

#[derive(Clone, Copy, Debug)]
pub struct AVLibStreamInfo {
    pub index: i32,
    pub codec_type: AVMediaType,
    pub width: i32,
    pub height: i32,
    pub sample_rate: i32,
    pub channels: i32,
}

impl AVLibStreamInfo {
    pub fn empty() -> Self {
        Self {
            index: -1,
            codec_type: AVMediaType::AVMEDIA_TYPE_UNKNOWN,
            width: 0,
            height: 0,
            sample_rate: 0,
            channels: 0,
        }
    }
}

pub trait IAVLibSource: Send + Any {
    fn Connect(&mut self);
    fn IsConnected(&self) -> bool;
    fn Duration(&self) -> f64;

    fn StreamCount(&self) -> i32;
    fn StreamType(&self, streamIndex: i32) -> AVMediaType;
    fn Stream(&self, streamIndex: i32) -> AVLibStreamInfo;
    fn TimeBase(&self, streamIndex: i32) -> f64;
    fn FrameRate(&self, streamIndex: i32) -> f64;
    fn FrameDuration(&self, streamIndex: i32) -> f64;

    fn IsRealtime(&self) -> bool;
    fn CanSeek(&self) -> bool;
    fn Seek(&mut self, from: f64, to: f64);
    fn TryGetNext(&mut self, streamIndex: i32) -> Option<AVLibPacket>;
    fn Recycle(&mut self, packet: AVLibPacket);
}
