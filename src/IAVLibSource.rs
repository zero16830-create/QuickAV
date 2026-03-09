#![allow(non_snake_case)]

use crate::AVLibPacket::AVLibPacket;
use ffmpeg_next::decoder::{Audio as AVLibAudioDecoderHandle, Video as AVLibVideoDecoderHandle};
use ffmpeg_next::ffi::AVMediaType;

#[repr(i32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AVLibSourceConnectionState {
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Checking = 4,
}

impl AVLibSourceConnectionState {
    pub fn IsConnected(self) -> bool {
        matches!(
            self,
            AVLibSourceConnectionState::Connected | AVLibSourceConnectionState::Checking
        )
    }
}

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

#[derive(Clone, Copy, Debug)]
pub struct AVLibSourceRuntimeStats {
    pub connection_state: AVLibSourceConnectionState,
    pub packet_count: u64,
    pub timeout_count: u64,
    pub reconnect_count: u64,
    pub is_checking_connection: bool,
    pub last_activity_age_sec: f64,
}

impl AVLibSourceRuntimeStats {
    pub fn empty() -> Self {
        Self {
            connection_state: AVLibSourceConnectionState::Disconnected,
            packet_count: 0,
            timeout_count: 0,
            reconnect_count: 0,
            is_checking_connection: false,
            last_activity_age_sec: -1.0,
        }
    }
}

pub trait IAVLibSource: Send {
    fn Connect(&mut self);
    fn ConnectionState(&self) -> AVLibSourceConnectionState;
    fn IsConnected(&self) -> bool {
        self.ConnectionState().IsConnected()
    }
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
    fn CreateVideoDecoder(&self, streamIndex: i32) -> Option<AVLibVideoDecoderHandle>;
    fn CreateAudioDecoder(&self, streamIndex: i32) -> Option<AVLibAudioDecoderHandle>;
    fn RuntimeStats(&self) -> AVLibSourceRuntimeStats;
}

#[cfg(test)]
mod tests {
    use super::AVLibSourceConnectionState;

    #[test]
    fn connection_state_connected_variants_report_connected() {
        assert!(AVLibSourceConnectionState::Connected.IsConnected());
        assert!(AVLibSourceConnectionState::Checking.IsConnected());
    }

    #[test]
    fn connection_state_disconnected_variants_report_not_connected() {
        assert!(!AVLibSourceConnectionState::Disconnected.IsConnected());
        assert!(!AVLibSourceConnectionState::Connecting.IsConnected());
        assert!(!AVLibSourceConnectionState::Reconnecting.IsConnected());
    }
}
