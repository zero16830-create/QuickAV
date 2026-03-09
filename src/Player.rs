#![allow(non_snake_case)]
#![allow(non_camel_case_types)]

use crate::AVLibPlayer::{AVLibPlayer, AVLibPlayerControlError, AVLibPlayerHealthSnapshot};
use crate::AudioExportState::SharedExportedAudioState;
use crate::FrameExportClient::SharedExportedFrameState;
use crate::IAVLibSource::AVLibStreamInfo;
use crate::IVideoClient::IVideoClient;
use crate::Logging::Debug::Debug;
use crate::OutputFactory::{OutputFactory, PlayerOutputBundle};
use crate::PixelFormat::PixelFormat;
use crate::SourceFactory::SourceFactory;
use std::os::raw::c_void;

pub struct Player {
    pub _avlib_player: AVLibPlayer,
    _uri: String,
}

impl Player {
    fn ValidateUri(uri: &str) -> bool {
        if !SourceFactory::IsRemoteUri(uri) {
            if std::fs::File::open(uri).is_err() {
                Debug::LogError(&format!("File does not exist at given uri: {}", uri));
                return false;
            }
        }

        true
    }

    pub fn Create(uri: String, video_client: Box<dyn IVideoClient + Send>) -> Option<Self> {
        Self::CreateWithOptionalAudioExport(uri, video_client, None)
    }

    fn CreateWithOutputBundle(
        uri: String,
        output: PlayerOutputBundle,
    ) -> Option<(
        Self,
        Option<SharedExportedFrameState>,
        Option<SharedExportedAudioState>,
    )> {
        let frame_export = output.frame_export.clone();
        let audio_export = output.audio_export.clone();
        let player =
            Self::CreateWithOptionalAudioExport(uri, output.video_client, audio_export.clone())?;
        Some((player, frame_export, audio_export))
    }

    fn CreateWithOptionalAudioExport(
        uri: String,
        video_client: Box<dyn IVideoClient + Send>,
        audio_export: Option<SharedExportedAudioState>,
    ) -> Option<Self> {
        if !Self::ValidateUri(&uri) {
            return None;
        }

        let target_width = video_client.Width();
        let target_height = video_client.Height();
        let target_format = match video_client.Format() {
            PixelFormat::PIXEL_FORMAT_NONE => PixelFormat::PIXEL_FORMAT_RGBA32,
            f => f,
        };

        let source = SourceFactory::Create(uri.clone());
        let av_player = AVLibPlayer::new(
            source,
            target_width,
            target_height,
            target_format,
            video_client,
            audio_export,
        )?;

        Some(Self {
            _avlib_player: av_player,
            _uri: uri,
        })
    }

    pub fn CreateWithTexture(uri: String, target_texture: *mut c_void) -> Option<Self> {
        let (player, _, _) = Self::CreateWithOutputBundle(uri, OutputFactory::CreateTexture(target_texture)?)?;
        Some(player)
    }

    pub fn CreateWithFrameExport(
        uri: String,
        target_width: i32,
        target_height: i32,
    ) -> Option<(Self, SharedExportedFrameState)> {
        let (player, frame_shared, _) =
            Self::CreateWithOutputBundle(uri, OutputFactory::CreateFrameExport(target_width, target_height)?)?;
        Some((player, frame_shared?,))
    }

    pub fn CreateWithFrameAndAudioExport(
        uri: String,
        target_width: i32,
        target_height: i32,
    ) -> Option<(Self, SharedExportedFrameState, SharedExportedAudioState)> {
        let (player, frame_shared, audio_shared) = Self::CreateWithOutputBundle(
            uri,
            OutputFactory::CreateFrameAndAudioExport(target_width, target_height)?,
        )?;
        Some((player, frame_shared?, audio_shared?))
    }

    pub fn Write(&mut self) {
        self._avlib_player.Write();
    }

    pub fn Play(&self) {
        self._avlib_player.Play();
    }

    pub fn Stop(&self) {
        self._avlib_player.Stop();
    }

    pub fn CanSeek(&self) -> bool {
        self._avlib_player.CanSeek()
    }

    pub fn Seek(&self, time: f64) -> Result<(), AVLibPlayerControlError> {
        self._avlib_player.Seek(time)
    }

    pub fn CanLoop(&self) -> bool {
        self._avlib_player.CanLoop()
    }

    pub fn SetLoop(&self, loop_value: bool) {
        self._avlib_player.SetLoop(loop_value);
    }

    pub fn IsLooping(&self) -> bool {
        self._avlib_player.IsLooping()
    }

    pub fn IsPlaying(&self) -> bool {
        self._avlib_player.IsPlaying()
    }

    pub fn IsRealtime(&self) -> bool {
        self._avlib_player.IsRealtime()
    }

    pub fn Duration(&self) -> f64 {
        self._avlib_player.Duration()
    }

    pub fn CurrentTime(&self) -> f64 {
        self._avlib_player.CurrentTime()
    }

    pub fn SetAudioSinkDelay(&self, delay_sec: f64) {
        self._avlib_player.SetAudioSinkDelay(delay_sec);
    }

    pub fn HealthSnapshot(&self) -> AVLibPlayerHealthSnapshot {
        self._avlib_player.HealthSnapshot()
    }

    pub fn StreamInfo(&self, stream_index: i32) -> Option<AVLibStreamInfo> {
        self._avlib_player.StreamInfo(stream_index)
    }
}
