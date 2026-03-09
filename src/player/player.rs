use crate::audio_export_state::SharedExportedAudioState;
use crate::av_lib_player::{AvLibPlayer, AvLibPlayerControlError, AvLibPlayerHealthSnapshot};
use crate::av_lib_source::AvLibStreamInfo;
use crate::frame_export_client::SharedExportedFrameState;
use crate::logging::debug::Debug;
use crate::output_factory::{OutputFactory, PlayerOutputBundle};
use crate::pixel_format::PixelFormat;
use crate::source_factory::SourceFactory;
use crate::video_client::VideoClient;
use std::os::raw::c_void;

pub struct Player {
    pub av_lib_player: AvLibPlayer,
}

impl Player {
    fn validate_uri(uri: &str) -> bool {
        if !SourceFactory::is_remote_uri(uri) {
            if std::fs::File::open(uri).is_err() {
                Debug::log_error(&format!("File does not exist at given uri: {}", uri));
                return false;
            }
        }

        true
    }

    pub fn create(uri: String, video_client: Box<dyn VideoClient + Send>) -> Option<Self> {
        Self::create_with_optional_audio_export(uri, video_client, None)
    }

    fn create_with_output_bundle(
        uri: String,
        output: PlayerOutputBundle,
    ) -> Option<(
        Self,
        Option<SharedExportedFrameState>,
        Option<SharedExportedAudioState>,
    )> {
        let frame_export = output.frame_export.clone();
        let audio_export = output.audio_export.clone();
        let player = Self::create_with_optional_audio_export(
            uri,
            output.video_client,
            audio_export.clone(),
        )?;
        Some((player, frame_export, audio_export))
    }

    fn create_with_optional_audio_export(
        uri: String,
        video_client: Box<dyn VideoClient + Send>,
        audio_export: Option<SharedExportedAudioState>,
    ) -> Option<Self> {
        if !Self::validate_uri(&uri) {
            return None;
        }

        let target_width = video_client.width();
        let target_height = video_client.height();
        let target_format = match video_client.format() {
            PixelFormat::Unknown => PixelFormat::Rgba32,
            f => f,
        };

        let source = SourceFactory::create(uri.clone());
        let av_player = AvLibPlayer::new(
            source,
            target_width,
            target_height,
            target_format,
            video_client,
            audio_export,
        )?;

        Some(Self {
            av_lib_player: av_player,
        })
    }

    pub fn create_with_texture(uri: String, target_texture: *mut c_void) -> Option<Self> {
        let (player, _, _) =
            Self::create_with_output_bundle(uri, OutputFactory::create_texture(target_texture)?)?;
        Some(player)
    }

    pub fn create_with_frame_export(
        uri: String,
        target_width: i32,
        target_height: i32,
    ) -> Option<(Self, SharedExportedFrameState)> {
        let (player, frame_shared, _) = Self::create_with_output_bundle(
            uri,
            OutputFactory::create_frame_export(target_width, target_height)?,
        )?;
        Some((player, frame_shared?))
    }

    pub fn create_with_frame_and_audio_export(
        uri: String,
        target_width: i32,
        target_height: i32,
    ) -> Option<(Self, SharedExportedFrameState, SharedExportedAudioState)> {
        let (player, frame_shared, audio_shared) = Self::create_with_output_bundle(
            uri,
            OutputFactory::create_frame_and_audio_export(target_width, target_height)?,
        )?;
        Some((player, frame_shared?, audio_shared?))
    }

    pub fn write(&mut self) {
        self.av_lib_player.write();
    }

    pub fn play(&self) {
        self.av_lib_player.play();
    }

    pub fn stop(&self) {
        self.av_lib_player.stop();
    }

    pub fn can_seek(&self) -> bool {
        self.av_lib_player.can_seek()
    }

    pub fn seek(&self, time: f64) -> Result<(), AvLibPlayerControlError> {
        self.av_lib_player.seek(time)
    }

    pub fn can_loop(&self) -> bool {
        self.av_lib_player.can_loop()
    }

    pub fn set_loop(&self, loop_value: bool) {
        self.av_lib_player.set_loop(loop_value);
    }

    pub fn is_looping(&self) -> bool {
        self.av_lib_player.is_looping()
    }

    pub fn is_playing(&self) -> bool {
        self.av_lib_player.is_playing()
    }

    pub fn is_realtime(&self) -> bool {
        self.av_lib_player.is_realtime()
    }

    pub fn duration(&self) -> f64 {
        self.av_lib_player.duration()
    }

    pub fn current_time(&self) -> f64 {
        self.av_lib_player.current_time()
    }

    pub fn set_audio_sink_delay(&self, delay_sec: f64) {
        self.av_lib_player.set_audio_sink_delay(delay_sec);
    }

    pub fn health_snapshot(&self) -> AvLibPlayerHealthSnapshot {
        self.av_lib_player.health_snapshot()
    }

    pub fn stream_info(&self, stream_index: i32) -> Option<AvLibStreamInfo> {
        self.av_lib_player.stream_info(stream_index)
    }
}
