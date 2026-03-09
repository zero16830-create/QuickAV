use crate::av_lib_packet::AvLibPacket;
use ffmpeg_next::decoder::{Audio as AVLibAudioDecoderHandle, Video as AVLibVideoDecoderHandle};
use ffmpeg_next::ffi::AVMediaType;

#[repr(i32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AvLibSourceConnectionState {
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Checking = 4,
}

impl AvLibSourceConnectionState {
    pub fn is_connected(self) -> bool {
        matches!(
            self,
            AvLibSourceConnectionState::Connected | AvLibSourceConnectionState::Checking
        )
    }
}

#[derive(Clone, Copy, Debug)]
pub struct AvLibStreamInfo {
    pub index: i32,
    pub codec_type: AVMediaType,
    pub width: i32,
    pub height: i32,
    pub sample_rate: i32,
    pub channels: i32,
}

impl AvLibStreamInfo {
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
pub struct AvLibSourceRuntimeStats {
    pub connection_state: AvLibSourceConnectionState,
    pub packet_count: u64,
    pub timeout_count: u64,
    pub reconnect_count: u64,
    pub is_checking_connection: bool,
    pub last_activity_age_sec: f64,
}

impl AvLibSourceRuntimeStats {
    pub fn empty() -> Self {
        Self {
            connection_state: AvLibSourceConnectionState::Disconnected,
            packet_count: 0,
            timeout_count: 0,
            reconnect_count: 0,
            is_checking_connection: false,
            last_activity_age_sec: -1.0,
        }
    }
}

pub trait AvLibSource: Send {
    fn connect(&mut self);
    fn connection_state(&self) -> AvLibSourceConnectionState;
    fn is_connected(&self) -> bool {
        self.connection_state().is_connected()
    }
    fn duration(&self) -> f64;

    fn stream_count(&self) -> i32;
    fn stream_type(&self, stream_index: i32) -> AVMediaType;
    fn stream(&self, stream_index: i32) -> AvLibStreamInfo;
    fn time_base(&self, stream_index: i32) -> f64;
    fn frame_rate(&self, stream_index: i32) -> f64;
    fn frame_duration(&self, stream_index: i32) -> f64;

    fn is_realtime(&self) -> bool;
    fn can_seek(&self) -> bool;
    fn seek(&mut self, from: f64, to: f64);
    fn try_get_next(&mut self, stream_index: i32) -> Option<AvLibPacket>;
    fn recycle(&mut self, packet: AvLibPacket);
    fn create_video_decoder(&self, stream_index: i32) -> Option<AVLibVideoDecoderHandle>;
    fn create_audio_decoder(&self, stream_index: i32) -> Option<AVLibAudioDecoderHandle>;
    fn runtime_stats(&self) -> AvLibSourceRuntimeStats;
}

#[cfg(test)]
mod tests {
    use super::AvLibSourceConnectionState;

    #[test]
    fn connection_state_connected_variants_report_connected() {
        assert!(AvLibSourceConnectionState::Connected.is_connected());
        assert!(AvLibSourceConnectionState::Checking.is_connected());
    }

    #[test]
    fn connection_state_disconnected_variants_report_not_connected() {
        assert!(!AvLibSourceConnectionState::Disconnected.is_connected());
        assert!(!AvLibSourceConnectionState::Connecting.is_connected());
        assert!(!AvLibSourceConnectionState::Reconnecting.is_connected());
    }
}
