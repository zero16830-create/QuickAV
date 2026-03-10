#pragma once

#include <stdbool.h>
#include <stdint.h>

#if defined(_WIN32)
#define RUSTAV_CALL __stdcall
#define RUSTAV_CALLBACK_CALL __stdcall
#else
#define RUSTAV_CALL
#define RUSTAV_CALLBACK_CALL
#endif

#define RUSTAV_ABI_VERSION_MAJOR 1u
#define RUSTAV_ABI_VERSION_MINOR 4u
#define RUSTAV_ABI_VERSION_PATCH 0u
#define RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_VERSION 2u
#define RUSTAV_PLAYER_OPEN_OPTIONS_VERSION 1u
#define RUSTAV_STREAM_INFO_VERSION 1u
#define RUSTAV_MAKE_ABI_VERSION(major, minor, patch) \
    ((((uint32_t)(major)) << 24) | (((uint32_t)(minor)) << 16) | ((uint32_t)(patch)))
#define RUSTAV_ABI_VERSION \
    RUSTAV_MAKE_ABI_VERSION( \
        RUSTAV_ABI_VERSION_MAJOR, \
        RUSTAV_ABI_VERSION_MINOR, \
        RUSTAV_ABI_VERSION_PATCH)

#ifdef __cplusplus
extern "C" {
#endif

typedef enum RustAVErrorCode {
    RustAVErrorCode_Ok = 0,
    RustAVErrorCode_InvalidArgument = -1,
    RustAVErrorCode_InvalidPlayer = -2,
    RustAVErrorCode_CreateFailed = -3,
    RustAVErrorCode_BufferTooSmall = -4,
    RustAVErrorCode_UnsupportedOperation = -5,
    RustAVErrorCode_InvalidState = -6
} RustAVErrorCode;

typedef enum RustAVPlayerState {
    RustAVPlayerState_Idle = 0,
    RustAVPlayerState_Connecting = 1,
    RustAVPlayerState_Ready = 2,
    RustAVPlayerState_Playing = 3,
    RustAVPlayerState_Paused = 4,
    RustAVPlayerState_Shutdown = 5,
    RustAVPlayerState_Ended = 6
} RustAVPlayerState;

typedef enum RustAVPlaybackIntent {
    RustAVPlaybackIntent_Stopped = 0,
    RustAVPlaybackIntent_PlayRequested = 1
} RustAVPlaybackIntent;

typedef enum RustAVStopReason {
    RustAVStopReason_None = 0,
    RustAVStopReason_UserStop = 1,
    RustAVStopReason_EndOfStream = 2
} RustAVStopReason;

typedef enum RustAVSourceConnectionState {
    RustAVSourceConnectionState_Disconnected = 0,
    RustAVSourceConnectionState_Connecting = 1,
    RustAVSourceConnectionState_Connected = 2,
    RustAVSourceConnectionState_Reconnecting = 3,
    RustAVSourceConnectionState_Checking = 4
} RustAVSourceConnectionState;

typedef enum RustAVAudioSampleFormat {
    RustAVAudioSampleFormat_Unknown = 0,
    RustAVAudioSampleFormat_F32 = 1
} RustAVAudioSampleFormat;

typedef enum RustAVBackendKind {
    RustAVBackendKind_Auto = 0,
    RustAVBackendKind_Ffmpeg = 1,
    RustAVBackendKind_Gstreamer = 2
} RustAVBackendKind;

typedef struct RustAVFrameMeta {
    int32_t width;
    int32_t height;
    int32_t format;
    int32_t stride;
    int32_t data_size;
    double time_sec;
    int64_t frame_index;
} RustAVFrameMeta;

typedef struct RustAVAudioMeta {
    int32_t sample_rate;
    int32_t channels;
    int32_t bytes_per_sample;
    int32_t sample_format;
    int32_t buffered_bytes;
    double time_sec;
    int64_t frame_index;
} RustAVAudioMeta;

typedef struct RustAVPlayerOpenOptions {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t backend_kind;
    int32_t strict_backend;
} RustAVPlayerOpenOptions;

typedef struct RustAVStreamInfo {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t stream_index;
    int32_t codec_type;
    int32_t width;
    int32_t height;
    int32_t sample_rate;
    int32_t channels;
} RustAVStreamInfo;

typedef struct RustAVPlayerHealthSnapshot {
    int32_t state;
    int32_t runtime_state;
    int32_t playback_intent;
    int32_t stop_reason;
    int32_t is_connected;
    int32_t is_playing;
    int32_t is_realtime;
    int32_t can_seek;
    int32_t is_looping;
    int32_t stream_count;
    int32_t video_decoder_count;
    int32_t has_audio_decoder;
    double duration_sec;
    double current_time_sec;
    double external_time_sec;
    double audio_time_sec;
    double audio_presented_time_sec;
    double audio_sink_delay_sec;
    double video_sync_compensation_sec;
    int64_t connect_attempt_count;
    int64_t video_decoder_recreate_count;
    int64_t audio_decoder_recreate_count;
    int64_t video_frame_drop_count;
    int64_t audio_frame_drop_count;
    int64_t source_packet_count;
    int64_t source_timeout_count;
    int64_t source_reconnect_count;
    int32_t source_is_checking_connection;
    double source_last_activity_age_sec;
} RustAVPlayerHealthSnapshot;

typedef struct RustAVPlayerHealthSnapshotV2 {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t state;
    int32_t runtime_state;
    int32_t playback_intent;
    int32_t stop_reason;
    int32_t source_connection_state;
    int32_t is_connected;
    int32_t is_playing;
    int32_t is_realtime;
    int32_t can_seek;
    int32_t is_looping;
    int32_t stream_count;
    int32_t video_decoder_count;
    int32_t has_audio_decoder;
    double duration_sec;
    double current_time_sec;
    double external_time_sec;
    double audio_time_sec;
    double audio_presented_time_sec;
    double audio_sink_delay_sec;
    double video_sync_compensation_sec;
    int64_t connect_attempt_count;
    int64_t video_decoder_recreate_count;
    int64_t audio_decoder_recreate_count;
    int64_t video_frame_drop_count;
    int64_t audio_frame_drop_count;
    int64_t source_packet_count;
    int64_t source_timeout_count;
    int64_t source_reconnect_count;
    int32_t source_is_checking_connection;
    double source_last_activity_age_sec;
} RustAVPlayerHealthSnapshotV2;

typedef void (RUSTAV_CALLBACK_CALL *RustAVLogCallback)(const char* message);
typedef void (RUSTAV_CALL *RustAVRenderEventFunc)(int32_t eventId);

uint32_t RUSTAV_CALL RustAV_GetAbiVersion(void);
const char* RUSTAV_CALL RustAV_GetBuildInfo(void);

int32_t RUSTAV_CALL RustAV_PlayerCreateTexture(const char* path, void* targetTexture);
int32_t RUSTAV_CALL RustAV_PlayerCreateTextureEx(
    const char* path,
    void* targetTexture,
    const RustAVPlayerOpenOptions* options);
int32_t RUSTAV_CALL RustAV_PlayerCreatePullRGBA(const char* path, int32_t targetWidth, int32_t targetHeight);
int32_t RUSTAV_CALL RustAV_PlayerCreatePullRGBAEx(
    const char* path,
    int32_t targetWidth,
    int32_t targetHeight,
    const RustAVPlayerOpenOptions* options);
int32_t RUSTAV_CALL RustAV_PlayerRelease(int32_t id);
int32_t RUSTAV_CALL RustAV_PlayerUpdate(int32_t id);
double RUSTAV_CALL RustAV_PlayerGetDuration(int32_t id);
double RUSTAV_CALL RustAV_PlayerGetTime(int32_t id);
int32_t RUSTAV_CALL RustAV_PlayerPlay(int32_t id);
int32_t RUSTAV_CALL RustAV_PlayerStop(int32_t id);
int32_t RUSTAV_CALL RustAV_PlayerSeek(int32_t id, double time);
int32_t RUSTAV_CALL RustAV_PlayerSetLoop(int32_t id, bool loopValue);
int32_t RUSTAV_CALL RustAV_PlayerSetAudioSinkDelaySeconds(int32_t id, double delaySec);
int32_t RUSTAV_CALL RustAV_PlayerGetBackendKind(int32_t id);
int32_t RUSTAV_CALL RustAV_PlayerGetHealthSnapshot(int32_t id, RustAVPlayerHealthSnapshot* outSnapshot);
int32_t RUSTAV_CALL RustAV_PlayerGetHealthSnapshotV2(int32_t id, RustAVPlayerHealthSnapshotV2* outSnapshot);
int32_t RUSTAV_CALL RustAV_PlayerGetStreamInfo(int32_t id, int32_t streamIndex, RustAVStreamInfo* outInfo);
int32_t RUSTAV_CALL RustAV_PlayerGetFrameMetaRGBA(int32_t id, RustAVFrameMeta* outMeta);
int32_t RUSTAV_CALL RustAV_PlayerCopyFrameRGBA(int32_t id, uint8_t* destination, int32_t destinationLength);
int32_t RUSTAV_CALL RustAV_PlayerGetAudioMetaPCM(int32_t id, RustAVAudioMeta* outMeta);
int32_t RUSTAV_CALL RustAV_PlayerCopyAudioPCM(int32_t id, uint8_t* destination, int32_t destinationLength);
int32_t RUSTAV_CALL RustAV_GetBackendRuntimeDiagnostic(
    int32_t backendKind,
    const char* path,
    bool requireAudioExport,
    char* destination,
    int32_t destinationLength);

void RUSTAV_CALL RustAV_DebugInitialize(bool cacheLogs);
void RUSTAV_CALL RustAV_DebugTeardown(void);
void RUSTAV_CALL RustAV_DebugClearCallbacks(void);
void RUSTAV_CALL RustAV_DebugRegisterLogCallback(RustAVLogCallback callback);
void RUSTAV_CALL RustAV_DebugRegisterWarningCallback(RustAVLogCallback callback);
void RUSTAV_CALL RustAV_DebugRegisterErrorCallback(RustAVLogCallback callback);

/* Unity 约定入口，函数名必须保持精确匹配。 */
void RUSTAV_CALL UnityPluginLoad(void* interfaces);
void RUSTAV_CALL UnityPluginUnload(void);
RustAVRenderEventFunc RUSTAV_CALL RustAV_GetRenderEventFunc(void);

#ifdef __cplusplus
}
#endif
