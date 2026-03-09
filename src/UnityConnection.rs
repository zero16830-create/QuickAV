#![allow(non_snake_case)]
#![allow(non_camel_case_types)]

use crate::AudioExportState::{ExportedAudioMeta, SharedExportedAudioState};
use crate::FrameExportClient::{ExportedFrameMeta, SharedExportedFrameState};
use crate::IAVLibSource::AVLibStreamInfo;
use crate::Player::Player;
use crate::PlayerRegistry;
use std::ffi::CStr;
use std::os::raw::{c_char, c_double, c_int, c_void};
use std::slice;

pub const RUSTAV_ABI_VERSION_MAJOR: u32 = 1;
pub const RUSTAV_ABI_VERSION_MINOR: u32 = 2;
pub const RUSTAV_ABI_VERSION_PATCH: u32 = 0;
pub const RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_VERSION: u32 = 2;
pub const RUSTAV_STREAM_INFO_VERSION: u32 = 1;
const RUSTAV_BUILD_INFO: &str = concat!(
    env!("CARGO_PKG_NAME"),
    " ",
    env!("CARGO_PKG_VERSION"),
    " abi=1.2.0\0"
);

#[repr(i32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RustAVErrorCode {
    Ok = 0,
    InvalidArgument = -1,
    InvalidPlayer = -2,
    CreateFailed = -3,
    BufferTooSmall = -4,
    UnsupportedOperation = -5,
    InvalidState = -6,
}

#[repr(i32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RustAVSourceConnectionState {
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Checking = 4,
}

#[repr(C)]
pub struct RustAVFrameMeta {
    pub width: i32,
    pub height: i32,
    pub format: i32,
    pub stride: i32,
    pub data_size: i32,
    pub time_sec: c_double,
    pub frame_index: i64,
}

#[repr(C)]
pub struct RustAVAudioMeta {
    pub sample_rate: i32,
    pub channels: i32,
    pub bytes_per_sample: i32,
    pub sample_format: i32,
    pub buffered_bytes: i32,
    pub time_sec: c_double,
    pub frame_index: i64,
}

#[repr(C)]
pub struct RustAVStreamInfo {
    pub struct_size: u32,
    pub struct_version: u32,
    pub stream_index: i32,
    pub codec_type: i32,
    pub width: i32,
    pub height: i32,
    pub sample_rate: i32,
    pub channels: i32,
}

#[repr(C)]
pub struct RustAVPlayerHealthSnapshot {
    pub state: i32,
    pub runtime_state: i32,
    pub playback_intent: i32,
    pub stop_reason: i32,
    pub is_connected: i32,
    pub is_playing: i32,
    pub is_realtime: i32,
    pub can_seek: i32,
    pub is_looping: i32,
    pub stream_count: i32,
    pub video_decoder_count: i32,
    pub has_audio_decoder: i32,
    pub duration_sec: c_double,
    pub current_time_sec: c_double,
    pub external_time_sec: c_double,
    pub audio_time_sec: c_double,
    pub audio_presented_time_sec: c_double,
    pub audio_sink_delay_sec: c_double,
    pub video_sync_compensation_sec: c_double,
    pub connect_attempt_count: i64,
    pub video_decoder_recreate_count: i64,
    pub audio_decoder_recreate_count: i64,
    pub video_frame_drop_count: i64,
    pub audio_frame_drop_count: i64,
    pub source_packet_count: i64,
    pub source_timeout_count: i64,
    pub source_reconnect_count: i64,
    pub source_is_checking_connection: i32,
    pub source_last_activity_age_sec: c_double,
}

#[repr(C)]
pub struct RustAVPlayerHealthSnapshotV2 {
    pub struct_size: u32,
    pub struct_version: u32,
    pub state: i32,
    pub runtime_state: i32,
    pub playback_intent: i32,
    pub stop_reason: i32,
    pub source_connection_state: i32,
    pub is_connected: i32,
    pub is_playing: i32,
    pub is_realtime: i32,
    pub can_seek: i32,
    pub is_looping: i32,
    pub stream_count: i32,
    pub video_decoder_count: i32,
    pub has_audio_decoder: i32,
    pub duration_sec: c_double,
    pub current_time_sec: c_double,
    pub external_time_sec: c_double,
    pub audio_time_sec: c_double,
    pub audio_presented_time_sec: c_double,
    pub audio_sink_delay_sec: c_double,
    pub video_sync_compensation_sec: c_double,
    pub connect_attempt_count: i64,
    pub video_decoder_recreate_count: i64,
    pub audio_decoder_recreate_count: i64,
    pub video_frame_drop_count: i64,
    pub audio_frame_drop_count: i64,
    pub source_packet_count: i64,
    pub source_timeout_count: i64,
    pub source_reconnect_count: i64,
    pub source_is_checking_connection: i32,
    pub source_last_activity_age_sec: c_double,
}

pub const RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_SIZE: u32 =
    std::mem::size_of::<RustAVPlayerHealthSnapshotV2>() as u32;
pub const RUSTAV_STREAM_INFO_SIZE: u32 = std::mem::size_of::<RustAVStreamInfo>() as u32;

#[no_mangle]
pub extern "system" fn RustAV_GetAbiVersion() -> u32 {
    (RUSTAV_ABI_VERSION_MAJOR << 24)
        | (RUSTAV_ABI_VERSION_MINOR << 16)
        | RUSTAV_ABI_VERSION_PATCH
}

#[no_mangle]
pub extern "system" fn RustAV_GetBuildInfo() -> *const c_char {
    RUSTAV_BUILD_INFO.as_ptr() as *const c_char
}

fn NormalizePath(path: *const c_char) -> Option<String> {
    if path.is_null() {
        return None;
    }

    Some(
        unsafe { CStr::from_ptr(path) }
            .to_string_lossy()
            .into_owned(),
    )
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerCreateTexture(
    path: *const c_char,
    targetTexture: *mut c_void,
) -> c_int {
    if path.is_null() || targetTexture.is_null() {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    let Some(path_str) = NormalizePath(path) else {
        return RustAVErrorCode::InvalidArgument as c_int;
    };

    PlayerRegistry::TryCleanPlayersCache();
    match Player::CreateWithTexture(path_str, targetTexture) {
        Some(player) => PlayerRegistry::StorePlayer(player, None, None),
        None => RustAVErrorCode::CreateFailed as c_int,
    }
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerCreatePullRGBA(
    path: *const c_char,
    targetWidth: c_int,
    targetHeight: c_int,
) -> c_int {
    let Some(path_str) = NormalizePath(path) else {
        return RustAVErrorCode::InvalidArgument as c_int;
    };

    if targetWidth <= 0 || targetHeight <= 0 {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    PlayerRegistry::TryCleanPlayersCache();

    let created = Player::CreateWithFrameAndAudioExport(path_str, targetWidth, targetHeight);
    match created {
        Some((player, frame_shared, audio_shared)) => PlayerRegistry::StorePlayer(
            player,
            Some(frame_shared),
            Some(audio_shared),
        ),
        None => RustAVErrorCode::CreateFailed as c_int,
    }
}

pub fn ForcePlayersWrite() {
    PlayerRegistry::ForcePlayersWrite();
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerRelease(id: c_int) -> c_int {
    if PlayerRegistry::ReleasePlayer(id) {
        RustAVErrorCode::Ok as c_int
    } else {
        RustAVErrorCode::InvalidPlayer as c_int
    }
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerUpdate(id: c_int) -> c_int {
    PlayerRegistry::WithPlayerMut(id, RustAVErrorCode::InvalidPlayer as c_int, |player| {
        player.Write();
        RustAVErrorCode::Ok as c_int
    })
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerGetDuration(id: c_int) -> c_double {
    PlayerRegistry::WithPlayer(id, -1.0, |player| player.Duration())
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerGetTime(id: c_int) -> c_double {
    PlayerRegistry::WithPlayer(id, -1.0, |player| player.CurrentTime())
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerPlay(id: c_int) -> c_int {
    PlayerRegistry::WithPlayer(id, RustAVErrorCode::InvalidPlayer as c_int, |player| {
        player.Play();
        RustAVErrorCode::Ok as c_int
    })
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerStop(id: c_int) -> c_int {
    PlayerRegistry::WithPlayer(id, RustAVErrorCode::InvalidPlayer as c_int, |player| {
        player.Stop();
        RustAVErrorCode::Ok as c_int
    })
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerSeek(id: c_int, time: c_double) -> c_int {
    if !time.is_finite() || time < 0.0 {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    PlayerRegistry::WithPlayer(id, RustAVErrorCode::InvalidPlayer as c_int, |player| {
        match player.Seek(time) {
            Ok(()) => RustAVErrorCode::Ok as c_int,
            Err(crate::AVLibPlayer::AVLibPlayerControlError::UnsupportedOperation) => {
                RustAVErrorCode::UnsupportedOperation as c_int
            }
            Err(crate::AVLibPlayer::AVLibPlayerControlError::InvalidState) => {
                RustAVErrorCode::InvalidState as c_int
            }
        }
    })
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerSetLoop(id: c_int, loop_value: bool) -> c_int {
    PlayerRegistry::WithPlayer(id, RustAVErrorCode::InvalidPlayer as c_int, |player| {
        player.SetLoop(loop_value);
        RustAVErrorCode::Ok as c_int
    })
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerSetAudioSinkDelaySeconds(
    id: c_int,
    delay_sec: c_double,
) -> c_int {
    if !delay_sec.is_finite() || delay_sec < 0.0 {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    PlayerRegistry::WithPlayer(id, RustAVErrorCode::InvalidPlayer as c_int, |player| {
        player.SetAudioSinkDelay(delay_sec);
        RustAVErrorCode::Ok as c_int
    })
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerGetHealthSnapshot(
    id: c_int,
    outSnapshot: *mut RustAVPlayerHealthSnapshot,
) -> c_int {
    if outSnapshot.is_null() {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    let snapshot = PlayerRegistry::WithPlayer(id, None, |player| Some(player.HealthSnapshot()));
    let Some(snapshot) = snapshot else {
        return RustAVErrorCode::InvalidPlayer as c_int;
    };

    unsafe {
        *outSnapshot = ToCPlayerHealthSnapshot(snapshot);
    }

    RustAVErrorCode::Ok as c_int
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerGetHealthSnapshotV2(
    id: c_int,
    outSnapshot: *mut RustAVPlayerHealthSnapshotV2,
) -> c_int {
    if outSnapshot.is_null() {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    unsafe {
        if (*outSnapshot).struct_size < RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_SIZE {
            return RustAVErrorCode::BufferTooSmall as c_int;
        }
    }

    let snapshot = PlayerRegistry::WithPlayer(id, None, |player| Some(player.HealthSnapshot()));
    let Some(snapshot) = snapshot else {
        return RustAVErrorCode::InvalidPlayer as c_int;
    };

    unsafe {
        *outSnapshot = ToCPlayerHealthSnapshotV2(snapshot);
    }

    RustAVErrorCode::Ok as c_int
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerGetFrameMetaRGBA(
    id: c_int,
    outMeta: *mut RustAVFrameMeta,
) -> c_int {
    if outMeta.is_null() {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    let shared = match require_frame_export(id) {
        Ok(shared) => shared,
        Err(code) => return code,
    };

    let shared = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    let meta = shared.Meta();
    unsafe {
        *outMeta = ToCFrameMeta(meta);
    }

    if meta.HasFrame { 1 } else { 0 }
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerGetStreamInfo(
    id: c_int,
    streamIndex: c_int,
    outInfo: *mut RustAVStreamInfo,
) -> c_int {
    if outInfo.is_null() || streamIndex < 0 {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    unsafe {
        if (*outInfo).struct_size < RUSTAV_STREAM_INFO_SIZE {
            return RustAVErrorCode::BufferTooSmall as c_int;
        }
    }

    let stream_info = PlayerRegistry::WithPlayer(id, None, |player| player.StreamInfo(streamIndex));
    let Some(stream_info) = stream_info else {
        return RustAVErrorCode::InvalidPlayer as c_int;
    };

    unsafe {
        *outInfo = ToCStreamInfo(stream_info);
    }

    RustAVErrorCode::Ok as c_int
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerCopyFrameRGBA(
    id: c_int,
    destination: *mut u8,
    destinationLength: c_int,
) -> c_int {
    if destination.is_null() || destinationLength <= 0 {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    let shared = match require_frame_export(id) {
        Ok(shared) => shared,
        Err(code) => return code,
    };

    let shared = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    let destination = unsafe { slice::from_raw_parts_mut(destination, destinationLength as usize) };
    let copied = shared.CopyTo(destination);

    if copied == 0 && shared.Meta().HasFrame {
        RustAVErrorCode::BufferTooSmall as c_int
    } else {
        copied
    }
}

fn ToCFrameMeta(meta: ExportedFrameMeta) -> RustAVFrameMeta {
    RustAVFrameMeta {
        width: meta.Width,
        height: meta.Height,
        format: meta.Format as i32,
        stride: meta.Stride,
        data_size: meta.DataLength,
        time_sec: meta.Time,
        frame_index: meta.FrameIndex,
    }
}

fn ToCAudioMeta(meta: ExportedAudioMeta) -> RustAVAudioMeta {
    RustAVAudioMeta {
        sample_rate: meta.SampleRate,
        channels: meta.Channels,
        bytes_per_sample: meta.BytesPerSample,
        sample_format: meta.SampleFormat,
        buffered_bytes: meta.BufferedBytes,
        time_sec: meta.Time,
        frame_index: meta.FrameIndex,
    }
}

fn ToCStreamInfo(info: AVLibStreamInfo) -> RustAVStreamInfo {
    RustAVStreamInfo {
        struct_size: RUSTAV_STREAM_INFO_SIZE,
        struct_version: RUSTAV_STREAM_INFO_VERSION,
        stream_index: info.index,
        codec_type: info.codec_type as i32,
        width: info.width,
        height: info.height,
        sample_rate: info.sample_rate,
        channels: info.channels,
    }
}

fn ToCPlayerHealthSnapshot(
    snapshot: crate::AVLibPlayer::AVLibPlayerHealthSnapshot,
) -> RustAVPlayerHealthSnapshot {
    RustAVPlayerHealthSnapshot {
        state: snapshot.state,
        runtime_state: snapshot.runtime_state,
        playback_intent: snapshot.playback_intent,
        stop_reason: snapshot.stop_reason,
        is_connected: snapshot.is_connected as i32,
        is_playing: snapshot.is_playing as i32,
        is_realtime: snapshot.is_realtime as i32,
        can_seek: snapshot.can_seek as i32,
        is_looping: snapshot.is_looping as i32,
        stream_count: snapshot.stream_count,
        video_decoder_count: snapshot.video_decoder_count,
        has_audio_decoder: snapshot.has_audio_decoder as i32,
        duration_sec: snapshot.duration_sec,
        current_time_sec: snapshot.current_time_sec,
        external_time_sec: snapshot.external_time_sec,
        audio_time_sec: snapshot.audio_time_sec,
        audio_presented_time_sec: snapshot.audio_presented_time_sec,
        audio_sink_delay_sec: snapshot.audio_sink_delay_sec,
        video_sync_compensation_sec: snapshot.video_sync_compensation_sec,
        connect_attempt_count: snapshot.connect_attempt_count as i64,
        video_decoder_recreate_count: snapshot.video_decoder_recreate_count as i64,
        audio_decoder_recreate_count: snapshot.audio_decoder_recreate_count as i64,
        video_frame_drop_count: snapshot.video_frame_drop_count as i64,
        audio_frame_drop_count: snapshot.audio_frame_drop_count as i64,
        source_packet_count: snapshot.source_packet_count as i64,
        source_timeout_count: snapshot.source_timeout_count as i64,
        source_reconnect_count: snapshot.source_reconnect_count as i64,
        source_is_checking_connection: snapshot.source_is_checking_connection as i32,
        source_last_activity_age_sec: snapshot.source_last_activity_age_sec,
    }
}

fn ToCPlayerHealthSnapshotV2(
    snapshot: crate::AVLibPlayer::AVLibPlayerHealthSnapshot,
) -> RustAVPlayerHealthSnapshotV2 {
    RustAVPlayerHealthSnapshotV2 {
        struct_size: RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_SIZE,
        struct_version: RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_VERSION,
        state: snapshot.state,
        runtime_state: snapshot.runtime_state,
        playback_intent: snapshot.playback_intent,
        stop_reason: snapshot.stop_reason,
        source_connection_state: snapshot.source_connection_state,
        is_connected: snapshot.is_connected as i32,
        is_playing: snapshot.is_playing as i32,
        is_realtime: snapshot.is_realtime as i32,
        can_seek: snapshot.can_seek as i32,
        is_looping: snapshot.is_looping as i32,
        stream_count: snapshot.stream_count,
        video_decoder_count: snapshot.video_decoder_count,
        has_audio_decoder: snapshot.has_audio_decoder as i32,
        duration_sec: snapshot.duration_sec,
        current_time_sec: snapshot.current_time_sec,
        external_time_sec: snapshot.external_time_sec,
        audio_time_sec: snapshot.audio_time_sec,
        audio_presented_time_sec: snapshot.audio_presented_time_sec,
        audio_sink_delay_sec: snapshot.audio_sink_delay_sec,
        video_sync_compensation_sec: snapshot.video_sync_compensation_sec,
        connect_attempt_count: snapshot.connect_attempt_count as i64,
        video_decoder_recreate_count: snapshot.video_decoder_recreate_count as i64,
        audio_decoder_recreate_count: snapshot.audio_decoder_recreate_count as i64,
        video_frame_drop_count: snapshot.video_frame_drop_count as i64,
        audio_frame_drop_count: snapshot.audio_frame_drop_count as i64,
        source_packet_count: snapshot.source_packet_count as i64,
        source_timeout_count: snapshot.source_timeout_count as i64,
        source_reconnect_count: snapshot.source_reconnect_count as i64,
        source_is_checking_connection: snapshot.source_is_checking_connection as i32,
        source_last_activity_age_sec: snapshot.source_last_activity_age_sec,
    }
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerGetAudioMetaPCM(
    id: c_int,
    outMeta: *mut RustAVAudioMeta,
) -> c_int {
    if outMeta.is_null() {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    let shared = match require_audio_export(id) {
        Ok(shared) => shared,
        Err(code) => return code,
    };

    let shared = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    let meta = shared.Meta();
    unsafe {
        *outMeta = ToCAudioMeta(meta);
    }

    if meta.HasAudio { 1 } else { 0 }
}

#[no_mangle]
pub extern "system" fn RustAV_PlayerCopyAudioPCM(
    id: c_int,
    destination: *mut u8,
    destinationLength: c_int,
) -> c_int {
    if destination.is_null() || destinationLength <= 0 {
        return RustAVErrorCode::InvalidArgument as c_int;
    }

    let shared = match require_audio_export(id) {
        Ok(shared) => shared,
        Err(code) => return code,
    };

    let mut shared = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    let destination = unsafe { slice::from_raw_parts_mut(destination, destinationLength as usize) };
    let copied = shared.CopyTo(destination);

    if copied == 0 && shared.Meta().HasAudio {
        RustAVErrorCode::BufferTooSmall as c_int
    } else {
        copied
    }
}

fn require_frame_export(id: c_int) -> Result<SharedExportedFrameState, c_int> {
    if !PlayerRegistry::ValidatePlayerId(id) {
        return Err(RustAVErrorCode::InvalidPlayer as c_int);
    }

    PlayerRegistry::SnapshotFrameExport(id)
        .ok_or(RustAVErrorCode::UnsupportedOperation as c_int)
}

fn require_audio_export(id: c_int) -> Result<SharedExportedAudioState, c_int> {
    if !PlayerRegistry::ValidatePlayerId(id) {
        return Err(RustAVErrorCode::InvalidPlayer as c_int);
    }

    PlayerRegistry::SnapshotAudioExport(id)
        .ok_or(RustAVErrorCode::UnsupportedOperation as c_int)
}

#[cfg(test)]
mod tests {
    use super::{
        RustAV_PlayerGetHealthSnapshot, RustAV_PlayerGetHealthSnapshotV2, RustAV_GetAbiVersion,
        RustAV_GetBuildInfo, RustAVErrorCode, RustAVPlayerHealthSnapshot,
        RustAVPlayerHealthSnapshotV2, RustAVSourceConnectionState, ToCPlayerHealthSnapshot,
        ToCPlayerHealthSnapshotV2,
        RUSTAV_ABI_VERSION_MAJOR, RUSTAV_ABI_VERSION_MINOR, RUSTAV_ABI_VERSION_PATCH,
        RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_SIZE, RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_VERSION,
    };
    use crate::AVLibPlayer::AVLibPlayerHealthSnapshot;
    use std::ffi::CStr;

    #[test]
    fn AbiVersion_matches_declared_constants() {
        let expected = (RUSTAV_ABI_VERSION_MAJOR << 24)
            | (RUSTAV_ABI_VERSION_MINOR << 16)
            | RUSTAV_ABI_VERSION_PATCH;
        assert_eq!(RustAV_GetAbiVersion(), expected);
    }

    #[test]
    fn BuildInfo_contains_package_version_and_is_nul_terminated() {
        let info_ptr = RustAV_GetBuildInfo();
        assert!(!info_ptr.is_null());

        let info = unsafe { CStr::from_ptr(info_ptr) }
            .to_str()
            .expect("build info must be utf-8");

        assert!(info.contains(env!("CARGO_PKG_VERSION")));
        assert!(info.contains("abi=1.1.0"));
    }

    #[test]
    fn PlayerGetHealthSnapshot_rejects_null_output_pointer() {
        let result = RustAV_PlayerGetHealthSnapshot(0, std::ptr::null_mut());
        assert_eq!(result, RustAVErrorCode::InvalidArgument as i32);
    }

    #[test]
    fn PlayerGetHealthSnapshotV2_rejects_null_output_pointer() {
        let result = RustAV_PlayerGetHealthSnapshotV2(0, std::ptr::null_mut());
        assert_eq!(result, RustAVErrorCode::InvalidArgument as i32);
    }

    #[test]
    fn PlayerGetHealthSnapshotV2_rejects_too_small_struct() {
        let mut snapshot = RustAVPlayerHealthSnapshotV2 {
            struct_size: 0,
            struct_version: 0,
            state: 0,
            runtime_state: 0,
            playback_intent: 0,
            stop_reason: 0,
            source_connection_state: 0,
            is_connected: 0,
            is_playing: 0,
            is_realtime: 0,
            can_seek: 0,
            is_looping: 0,
            stream_count: 0,
            video_decoder_count: 0,
            has_audio_decoder: 0,
            duration_sec: 0.0,
            current_time_sec: 0.0,
            external_time_sec: 0.0,
            audio_time_sec: 0.0,
            audio_presented_time_sec: 0.0,
            audio_sink_delay_sec: 0.0,
            video_sync_compensation_sec: 0.0,
            connect_attempt_count: 0,
            video_decoder_recreate_count: 0,
            audio_decoder_recreate_count: 0,
            video_frame_drop_count: 0,
            audio_frame_drop_count: 0,
            source_packet_count: 0,
            source_timeout_count: 0,
            source_reconnect_count: 0,
            source_is_checking_connection: 0,
            source_last_activity_age_sec: 0.0,
        };

        let result = RustAV_PlayerGetHealthSnapshotV2(0, &mut snapshot);
        assert_eq!(result, RustAVErrorCode::BufferTooSmall as i32);
    }

    #[test]
    fn ToCPlayerHealthSnapshot_maps_extended_runtime_fields() {
        let snapshot = AVLibPlayerHealthSnapshot {
            state: 3,
            runtime_state: 2,
            playback_intent: 1,
            stop_reason: 0,
            source_connection_state: RustAVSourceConnectionState::Checking as i32,
            is_connected: true,
            is_playing: true,
            is_realtime: true,
            can_seek: false,
            is_looping: false,
            stream_count: 2,
            video_decoder_count: 1,
            has_audio_decoder: true,
            duration_sec: 10.0,
            current_time_sec: 1.5,
            external_time_sec: 1.6,
            audio_time_sec: 1.4,
            audio_presented_time_sec: 1.3,
            audio_sink_delay_sec: 0.1,
            video_sync_compensation_sec: -0.02,
            connect_attempt_count: 4,
            video_decoder_recreate_count: 2,
            audio_decoder_recreate_count: 1,
            video_frame_drop_count: 8,
            audio_frame_drop_count: 3,
            source_packet_count: 21,
            source_timeout_count: 5,
            source_reconnect_count: 2,
            source_is_checking_connection: true,
            source_last_activity_age_sec: 0.75,
        };

        let c_snapshot: RustAVPlayerHealthSnapshot = ToCPlayerHealthSnapshot(snapshot);
        assert_eq!(c_snapshot.state, 3);
        assert_eq!(c_snapshot.runtime_state, 2);
        assert_eq!(c_snapshot.playback_intent, 1);
        assert_eq!(c_snapshot.stop_reason, 0);
        assert_eq!(c_snapshot.is_connected, 1);
        assert_eq!(c_snapshot.is_playing, 1);
        assert_eq!(c_snapshot.is_realtime, 1);
        assert_eq!(c_snapshot.can_seek, 0);
        assert_eq!(c_snapshot.has_audio_decoder, 1);
        assert_eq!(c_snapshot.connect_attempt_count, 4);
        assert_eq!(c_snapshot.video_decoder_recreate_count, 2);
        assert_eq!(c_snapshot.audio_decoder_recreate_count, 1);
        assert_eq!(c_snapshot.video_frame_drop_count, 8);
        assert_eq!(c_snapshot.audio_frame_drop_count, 3);
        assert_eq!(c_snapshot.source_packet_count, 21);
        assert_eq!(c_snapshot.source_timeout_count, 5);
        assert_eq!(c_snapshot.source_reconnect_count, 2);
        assert_eq!(c_snapshot.source_is_checking_connection, 1);
        assert!((c_snapshot.source_last_activity_age_sec - 0.75).abs() < f64::EPSILON);
    }

    #[test]
    fn ToCPlayerHealthSnapshotV2_maps_versioned_runtime_fields() {
        let snapshot = AVLibPlayerHealthSnapshot {
            state: 3,
            runtime_state: 2,
            playback_intent: 1,
            stop_reason: 0,
            source_connection_state: 4,
            is_connected: true,
            is_playing: true,
            is_realtime: true,
            can_seek: false,
            is_looping: false,
            stream_count: 2,
            video_decoder_count: 1,
            has_audio_decoder: true,
            duration_sec: 10.0,
            current_time_sec: 1.5,
            external_time_sec: 1.6,
            audio_time_sec: 1.4,
            audio_presented_time_sec: 1.3,
            audio_sink_delay_sec: 0.1,
            video_sync_compensation_sec: -0.02,
            connect_attempt_count: 4,
            video_decoder_recreate_count: 2,
            audio_decoder_recreate_count: 1,
            video_frame_drop_count: 8,
            audio_frame_drop_count: 3,
            source_packet_count: 21,
            source_timeout_count: 5,
            source_reconnect_count: 2,
            source_is_checking_connection: true,
            source_last_activity_age_sec: 0.75,
        };

        let c_snapshot: RustAVPlayerHealthSnapshotV2 = ToCPlayerHealthSnapshotV2(snapshot);
        assert_eq!(c_snapshot.struct_size, RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_SIZE);
        assert_eq!(
            c_snapshot.struct_version,
            RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_VERSION
        );
        assert_eq!(c_snapshot.state, 3);
        assert_eq!(c_snapshot.runtime_state, 2);
        assert_eq!(c_snapshot.playback_intent, 1);
        assert_eq!(c_snapshot.stop_reason, 0);
        assert_eq!(
            c_snapshot.source_connection_state,
            RustAVSourceConnectionState::Checking as i32
        );
        assert_eq!(c_snapshot.is_connected, 1);
        assert_eq!(c_snapshot.is_playing, 1);
        assert_eq!(c_snapshot.is_realtime, 1);
        assert_eq!(c_snapshot.can_seek, 0);
        assert_eq!(c_snapshot.has_audio_decoder, 1);
        assert_eq!(c_snapshot.connect_attempt_count, 4);
        assert_eq!(c_snapshot.video_decoder_recreate_count, 2);
        assert_eq!(c_snapshot.audio_decoder_recreate_count, 1);
        assert_eq!(c_snapshot.video_frame_drop_count, 8);
        assert_eq!(c_snapshot.audio_frame_drop_count, 3);
        assert_eq!(c_snapshot.source_packet_count, 21);
        assert_eq!(c_snapshot.source_timeout_count, 5);
        assert_eq!(c_snapshot.source_reconnect_count, 2);
        assert_eq!(c_snapshot.source_is_checking_connection, 1);
        assert!((c_snapshot.source_last_activity_age_sec - 0.75).abs() < f64::EPSILON);
    }
}
