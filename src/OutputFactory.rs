#![allow(non_snake_case)]

use crate::AudioExportState::{ExportedAudioState, SharedExportedAudioState};
use crate::FrameExportClient::{FrameExportClient, SharedExportedFrameState};
use crate::IVideoClient::IVideoClient;
use crate::Logging::Debug::Debug;
use crate::PixelFormat::PixelFormat;
use crate::TextureClient::TextureClient;
use std::os::raw::c_void;

pub struct PlayerOutputBundle {
    pub video_client: Box<dyn IVideoClient + Send>,
    pub frame_export: Option<SharedExportedFrameState>,
    pub audio_export: Option<SharedExportedAudioState>,
}

pub struct OutputFactory;

impl OutputFactory {
    pub fn CreateTexture(target_texture: *mut c_void) -> Option<PlayerOutputBundle> {
        let client = TextureClient::new(target_texture)?;
        Some(PlayerOutputBundle {
            video_client: Box::new(client),
            frame_export: None,
            audio_export: None,
        })
    }

    pub fn CreateFrameExport(
        target_width: i32,
        target_height: i32,
    ) -> Option<PlayerOutputBundle> {
        let mut bundle = Self::CreateFrameAndAudioExport(target_width, target_height)?;
        bundle.audio_export = None;
        Some(bundle)
    }

    pub fn CreateFrameAndAudioExport(
        target_width: i32,
        target_height: i32,
    ) -> Option<PlayerOutputBundle> {
        if target_width <= 0 || target_height <= 0 {
            Debug::LogError("OutputFactory::CreateFrameAndAudioExport - target size must be positive");
            return None;
        }

        let (client, frame_export) = FrameExportClient::new(
            target_width,
            target_height,
            PixelFormat::PIXEL_FORMAT_RGBA32,
        );
        Some(PlayerOutputBundle {
            video_client: Box::new(client),
            frame_export: Some(frame_export),
            audio_export: Some(ExportedAudioState::Shared()),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::OutputFactory;

    #[test]
    fn CreateFrameAndAudioExport_rejects_invalid_target_size() {
        assert!(OutputFactory::CreateFrameAndAudioExport(0, 720).is_none());
        assert!(OutputFactory::CreateFrameAndAudioExport(1280, 0).is_none());
    }

    #[test]
    fn CreateFrameExport_returns_video_export_without_audio_state() {
        let bundle = OutputFactory::CreateFrameExport(640, 360).expect("bundle");
        assert!(bundle.frame_export.is_some());
        assert!(bundle.audio_export.is_none());
    }

    #[test]
    fn CreateFrameAndAudioExport_returns_both_exports() {
        let bundle = OutputFactory::CreateFrameAndAudioExport(640, 360).expect("bundle");
        assert!(bundle.frame_export.is_some());
        assert!(bundle.audio_export.is_some());
    }
}
