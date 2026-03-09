use crate::audio_export_state::{ExportedAudioState, SharedExportedAudioState};
use crate::frame_export_client::{FrameExportClient, SharedExportedFrameState};
use crate::logging::debug::Debug;
use crate::pixel_format::PixelFormat;
use crate::texture_client::TextureClient;
use crate::video_client::VideoClient;
use std::os::raw::c_void;

pub struct PlayerOutputBundle {
    pub video_client: Box<dyn VideoClient + Send>,
    pub frame_export: Option<SharedExportedFrameState>,
    pub audio_export: Option<SharedExportedAudioState>,
}

pub struct OutputFactory;

impl OutputFactory {
    pub fn create_texture(target_texture: *mut c_void) -> Option<PlayerOutputBundle> {
        let client = TextureClient::new(target_texture)?;
        Some(PlayerOutputBundle {
            video_client: Box::new(client),
            frame_export: None,
            audio_export: None,
        })
    }

    pub fn create_frame_export(
        target_width: i32,
        target_height: i32,
    ) -> Option<PlayerOutputBundle> {
        let mut bundle = Self::create_frame_and_audio_export(target_width, target_height)?;
        bundle.audio_export = None;
        Some(bundle)
    }

    pub fn create_frame_and_audio_export(
        target_width: i32,
        target_height: i32,
    ) -> Option<PlayerOutputBundle> {
        if target_width <= 0 || target_height <= 0 {
            Debug::log_error(
                "OutputFactory::create_frame_and_audio_export - target size must be positive",
            );
            return None;
        }

        let (client, frame_export) =
            FrameExportClient::new(target_width, target_height, PixelFormat::Rgba32);
        Some(PlayerOutputBundle {
            video_client: Box::new(client),
            frame_export: Some(frame_export),
            audio_export: Some(ExportedAudioState::shared()),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::OutputFactory;

    #[test]
    fn create_frame_and_audio_export_rejects_invalid_target_size() {
        assert!(OutputFactory::create_frame_and_audio_export(0, 720).is_none());
        assert!(OutputFactory::create_frame_and_audio_export(1280, 0).is_none());
    }

    #[test]
    fn create_frame_export_returns_video_export_without_audio_state() {
        let bundle = OutputFactory::create_frame_export(640, 360).expect("bundle");
        assert!(bundle.frame_export.is_some());
        assert!(bundle.audio_export.is_none());
    }

    #[test]
    fn create_frame_and_audio_export_returns_both_exports() {
        let bundle = OutputFactory::create_frame_and_audio_export(640, 360).expect("bundle");
        assert!(bundle.frame_export.is_some());
        assert!(bundle.audio_export.is_some());
    }
}
