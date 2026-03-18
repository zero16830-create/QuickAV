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
#define RUSTAV_ABI_VERSION_MINOR 10u
#define RUSTAV_ABI_VERSION_PATCH 0u
#define RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_VERSION 2u
#define RUSTAV_PLAYER_OPEN_OPTIONS_VERSION 1u
#define RUSTAV_STREAM_INFO_VERSION 1u
#define RUSTAV_VIDEO_FRAME_CONTRACT_VERSION 1u
#define RUSTAV_PLAYBACK_TIMING_CONTRACT_VERSION 1u
#define RUSTAV_AV_SYNC_CONTRACT_VERSION 1u
#define RUSTAV_NATIVE_VIDEO_TARGET_VERSION 1u
#define RUSTAV_NATIVE_VIDEO_INTEROP_CAPS_VERSION 1u
#define RUSTAV_NATIVE_VIDEO_BRIDGE_DESCRIPTOR_VERSION 1u
#define RUSTAV_NATIVE_VIDEO_PATH_SELECTION_VERSION 1u
#define RUSTAV_NATIVE_VIDEO_FRAME_VERSION 1u
#define RUSTAV_NATIVE_VIDEO_PLANE_TEXTURES_VERSION 1u
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

typedef enum RustAVNativeVideoPlatformKind {
    RustAVNativeVideoPlatformKind_Unknown = 0,
    RustAVNativeVideoPlatformKind_Windows = 1,
    RustAVNativeVideoPlatformKind_Ios = 2,
    RustAVNativeVideoPlatformKind_Android = 3
} RustAVNativeVideoPlatformKind;

typedef enum RustAVNativeVideoSurfaceKind {
    RustAVNativeVideoSurfaceKind_Unknown = 0,
    RustAVNativeVideoSurfaceKind_D3D11Texture2D = 1,
    RustAVNativeVideoSurfaceKind_MetalTexture = 2,
    RustAVNativeVideoSurfaceKind_CVPixelBuffer = 3,
    RustAVNativeVideoSurfaceKind_AndroidSurfaceTexture = 4,
    RustAVNativeVideoSurfaceKind_AndroidHardwareBuffer = 5
} RustAVNativeVideoSurfaceKind;

#define RUSTAV_NATIVE_VIDEO_TARGET_FLAG_NONE 0u
#define RUSTAV_NATIVE_VIDEO_TARGET_FLAG_EXTERNAL_TEXTURE (1u << 0)
#define RUSTAV_NATIVE_VIDEO_TARGET_FLAG_UNITY_OWNED_TEXTURE (1u << 1)
#define RUSTAV_NATIVE_VIDEO_TARGET_FLAG_DISABLE_DIRECT_TARGET_PRESENT (1u << 2)

#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_NONE 0u
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_TARGET_BINDING_SUPPORTED (1u << 0)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_FRAME_ACQUIRE_SUPPORTED (1u << 1)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_FRAME_RELEASE_SUPPORTED (1u << 2)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_FALLBACK_COPY_PATH (1u << 3)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_EXTERNAL_TEXTURE_TARGET (1u << 4)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_SOURCE_SURFACE_ZERO_COPY (1u << 5)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_PRESENTED_FRAME_DIRECT_BINDABLE (1u << 6)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_PRESENTED_FRAME_STRICT_ZERO_COPY (1u << 7)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_SOURCE_PLANE_TEXTURES_SUPPORTED (1u << 8)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_SOURCE_PLANE_VIEWS_SUPPORTED (1u << 9)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_CONTRACT_TARGET_SUPPORTED (1u << 10)
#define RUSTAV_NATIVE_VIDEO_CAP_FLAG_RUNTIME_BRIDGE_PENDING (1u << 11)

#define RUSTAV_NATIVE_VIDEO_FRAME_FLAG_NONE 0u
#define RUSTAV_NATIVE_VIDEO_FRAME_FLAG_HAS_FRAME (1u << 0)
#define RUSTAV_NATIVE_VIDEO_FRAME_FLAG_HARDWARE_DECODE (1u << 1)
#define RUSTAV_NATIVE_VIDEO_FRAME_FLAG_ZERO_COPY (1u << 2)
#define RUSTAV_NATIVE_VIDEO_FRAME_FLAG_CPU_FALLBACK (1u << 3)

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

typedef struct RustAVNativeVideoTarget {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t platform_kind;
    int32_t surface_kind;
    uint64_t target_handle;
    uint64_t auxiliary_handle;
    int32_t width;
    int32_t height;
    int32_t pixel_format;
    uint32_t flags;
} RustAVNativeVideoTarget;

typedef struct RustAVNativeVideoInteropCaps {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t backend_kind;
    int32_t platform_kind;
    int32_t surface_kind;
    int32_t supported;
    int32_t hardware_decode_supported;
    int32_t zero_copy_supported;
    int32_t acquire_release_supported;
    uint32_t flags;
} RustAVNativeVideoInteropCaps;

typedef struct RustAVNativeVideoBridgeDescriptor {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t backend_kind;
    int32_t target_platform_kind;
    int32_t target_surface_kind;
    int32_t target_width;
    int32_t target_height;
    int32_t target_pixel_format;
    uint32_t target_flags;
    int32_t platform_kind;
    int32_t surface_kind;
    int32_t state;
    int32_t runtime_kind;
    int32_t supported;
    int32_t hardware_decode_supported;
    int32_t zero_copy_supported;
    int32_t acquire_release_supported;
    uint32_t caps_flags;
    int32_t target_valid;
    int32_t requested_external_texture_target;
    int32_t direct_target_present_allowed;
    int32_t target_binding_supported;
    int32_t external_texture_target_supported;
    int32_t frame_acquire_supported;
    int32_t frame_release_supported;
    int32_t fallback_copy_path;
    int32_t source_surface_zero_copy;
    int32_t presented_frame_direct_bindable;
    int32_t presented_frame_strict_zero_copy;
    int32_t source_plane_textures_supported;
    int32_t source_plane_views_supported;
} RustAVNativeVideoBridgeDescriptor;

typedef struct RustAVNativeVideoPathSelection {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t kind;
    int32_t has_source_frame;
    int32_t has_presented_frame;
    int32_t source_memory_kind;
    int32_t presented_memory_kind;
    int32_t bridge_state;
    int32_t source_surface_zero_copy;
    int32_t source_plane_textures_supported;
    int32_t target_zero_copy;
    int32_t cpu_fallback;
} RustAVNativeVideoPathSelection;

typedef struct RustAVNativeVideoFrame {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t surface_kind;
    uint64_t native_handle;
    uint64_t auxiliary_handle;
    int32_t width;
    int32_t height;
    int32_t pixel_format;
    double time_sec;
    int64_t frame_index;
    uint32_t flags;
} RustAVNativeVideoFrame;

typedef enum RustAVNativeVideoPlaneTextureFormat {
    RustAVNativeVideoPlaneTextureFormat_Unknown = 0,
    RustAVNativeVideoPlaneTextureFormat_R8Unorm = 1,
    RustAVNativeVideoPlaneTextureFormat_Rg8Unorm = 2
} RustAVNativeVideoPlaneTextureFormat;

typedef enum RustAVNativeVideoPlaneResourceKind {
    RustAVNativeVideoPlaneResourceKind_Unknown = 0,
    RustAVNativeVideoPlaneResourceKind_D3D11Texture2D = 1,
    RustAVNativeVideoPlaneResourceKind_D3D11ShaderResourceView = 2
} RustAVNativeVideoPlaneResourceKind;

typedef struct RustAVNativeVideoPlaneTextures {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t surface_kind;
    int32_t source_pixel_format;
    uint64_t y_native_handle;
    uint64_t y_auxiliary_handle;
    int32_t y_width;
    int32_t y_height;
    int32_t y_texture_format;
    uint64_t uv_native_handle;
    uint64_t uv_auxiliary_handle;
    int32_t uv_width;
    int32_t uv_height;
    int32_t uv_texture_format;
    double time_sec;
    int64_t frame_index;
    uint32_t flags;
} RustAVNativeVideoPlaneTextures;

typedef struct RustAVNativeVideoPlaneViews {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t surface_kind;
    int32_t source_pixel_format;
    uint64_t y_native_handle;
    uint64_t y_auxiliary_handle;
    int32_t y_width;
    int32_t y_height;
    int32_t y_texture_format;
    int32_t y_resource_kind;
    uint64_t uv_native_handle;
    uint64_t uv_auxiliary_handle;
    int32_t uv_width;
    int32_t uv_height;
    int32_t uv_texture_format;
    int32_t uv_resource_kind;
    double time_sec;
    int64_t frame_index;
    uint32_t flags;
} RustAVNativeVideoPlaneViews;

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

typedef struct RustAVVideoFrameContract {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t memory_kind;
    int32_t surface_kind;
    int32_t pixel_format;
    int32_t width;
    int32_t height;
    int32_t plane_count;
    int32_t hardware_decode;
    int32_t zero_copy;
    int32_t cpu_fallback;
    int32_t native_handle_present;
    int32_t auxiliary_handle_present;
    int32_t color_range;
    int32_t color_matrix;
    int32_t color_primaries;
    int32_t color_transfer;
    int32_t color_bit_depth;
    int32_t color_dynamic_range;
    int32_t has_color_dynamic_range_override;
    int32_t color_dynamic_range_override;
    double time_sec;
    int32_t has_frame_index;
    int64_t frame_index;
    int32_t has_nominal_fps;
    double nominal_fps;
    int32_t has_timeline_origin_sec;
    double timeline_origin_sec;
    uint64_t seek_epoch;
    int32_t discontinuity;
} RustAVVideoFrameContract;

typedef struct RustAVPlaybackTimingContract {
    uint32_t struct_size;
    uint32_t struct_version;
    double master_time_sec;
    double external_time_sec;
    int32_t has_audio_time_sec;
    double audio_time_sec;
    int32_t has_audio_presented_time_sec;
    double audio_presented_time_sec;
    double audio_sink_delay_sec;
    int32_t has_audio_clock;
} RustAVPlaybackTimingContract;

typedef struct RustAVAvSyncContract {
    uint32_t struct_size;
    uint32_t struct_version;
    int32_t master_clock;
    int32_t has_audio_clock_sec;
    double audio_clock_sec;
    int32_t has_video_clock_sec;
    double video_clock_sec;
    double drift_ms;
    int32_t startup_warmup_complete;
    uint64_t drop_total;
    uint64_t duplicate_total;
} RustAVAvSyncContract;

typedef void (RUSTAV_CALLBACK_CALL *RustAVLogCallback)(const char* message);
typedef void (RUSTAV_CALL *RustAVRenderEventFunc)(int32_t eventId);

uint32_t RUSTAV_CALL RustAV_GetAbiVersion(void);
const char* RUSTAV_CALL RustAV_GetBuildInfo(void);

/*
 * 跨平台标准能力
 *
 * 这组接口是 Windows / iOS / Android 应优先依赖的统一合同。
 * 推荐组合：
 *   - RustAV_PlayerCreatePullRGBA[Ex]
 *   - RustAV_PlayerCreateWgpuRGBA[Ex]
 *   - RustAV_PlayerGetFrameMetaRGBA
 *   - RustAV_PlayerCopyFrameRGBA
 *   - RustAV_PlayerGetAudioMetaPCM
 *   - RustAV_PlayerCopyAudioPCM
 *   - RustAV_PlayerPlay / Stop / Seek / SetLoop
 *   - RustAV_PlayerGetHealthSnapshotV2 / RustAV_GetBackendRuntimeDiagnostic
 */
int32_t RUSTAV_CALL RustAV_PlayerCreatePullRGBA(const char* path, int32_t targetWidth, int32_t targetHeight);
int32_t RUSTAV_CALL RustAV_PlayerCreatePullRGBAEx(
    const char* path,
    int32_t targetWidth,
    int32_t targetHeight,
    const RustAVPlayerOpenOptions* options);
int32_t RUSTAV_CALL RustAV_PlayerCreateWgpuRGBA(const char* path, int32_t targetWidth, int32_t targetHeight);
int32_t RUSTAV_CALL RustAV_PlayerCreateWgpuRGBAEx(
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
int32_t RUSTAV_CALL RustAV_PlayerGetLatestVideoFrameContract(
    int32_t id,
    RustAVVideoFrameContract* outContract);
int32_t RUSTAV_CALL RustAV_PlayerGetLatestSourceVideoFrameContract(
    int32_t id,
    RustAVVideoFrameContract* outContract);
int32_t RUSTAV_CALL RustAV_PlayerGetPlaybackTimingContract(
    int32_t id,
    RustAVPlaybackTimingContract* outContract);
int32_t RUSTAV_CALL RustAV_PlayerGetAvSyncContract(
    int32_t id,
    RustAVAvSyncContract* outContract);
int32_t RUSTAV_CALL RustAV_PlayerGetNativeVideoBridgeDescriptor(
    int32_t id,
    RustAVNativeVideoBridgeDescriptor* outDescriptor);
int32_t RUSTAV_CALL RustAV_PlayerGetNativeVideoPathSelection(
    int32_t id,
    RustAVNativeVideoPathSelection* outSelection);
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

/*
 * NativeVideo / 硬解零拷贝增强能力
 *
 * 这组接口用于表达原生视频表面、硬解能力探测与 acquire/release 生命周期。
 * 它是增强路径，不替代 PullRGBA/PullPCM 主路径。
 * 当前运行时严格验证范围是 Windows + D3D11。
 * iOS / Android 的 platform/surface 枚举与目标描述已经保留合同，
 * 后续平台桥接应围绕这组合同继续推进。
 * 对于 iOS / Android，当前允许通过 capability flags 区分：
 *   - CONTRACT_TARGET_SUPPORTED
 *   - RUNTIME_BRIDGE_PENDING
 * 也就是“目标合同已就绪，但 runtime bridge 尚未实装”。
 * 推荐做法：
 *   - 先调用 RustAV_PlayerGetNativeVideoInteropCaps
 *   - 能力满足时再调用 RustAV_PlayerCreateNativeVideoOutput[Ex]
 *   - 读取帧时配合 RustAV_PlayerAcquireNativeVideoFrame / ReleaseNativeVideoFrame
 *   - 如需获取原始硬解 surface，可调用 RustAV_PlayerAcquireNativeVideoSourceFrame
 *   - 如需 Unity Compute Shader 消费 NV12 source planes，可调用 RustAV_PlayerGetNativeVideoSourcePlaneTextures
 *   - 如果不满足，则回退到 PullRGBA/PullPCM 主路径
 */
int32_t RUSTAV_CALL RustAV_PlayerGetNativeVideoInteropCaps(
    int32_t backendKind,
    const char* path,
    const RustAVNativeVideoTarget* target,
    RustAVNativeVideoInteropCaps* outCaps);
int32_t RUSTAV_CALL RustAV_PlayerCreateNativeVideoOutput(
    const char* path,
    const RustAVNativeVideoTarget* target);
int32_t RUSTAV_CALL RustAV_PlayerCreateNativeVideoOutputEx(
    const char* path,
    const RustAVNativeVideoTarget* target,
    const RustAVPlayerOpenOptions* options);
int32_t RUSTAV_CALL RustAV_PlayerAcquireNativeVideoFrame(
    int32_t id,
    RustAVNativeVideoFrame* outFrame);
int32_t RUSTAV_CALL RustAV_PlayerAcquireNativeVideoSourceFrame(
    int32_t id,
    RustAVNativeVideoFrame* outFrame);
int32_t RUSTAV_CALL RustAV_PlayerGetNativeVideoSourcePlaneTextures(
    int32_t id,
    RustAVNativeVideoPlaneTextures* outTextures);
int32_t RUSTAV_CALL RustAV_PlayerGetNativeVideoSourcePlaneViews(
    int32_t id,
    RustAVNativeVideoPlaneViews* outViews);
int32_t RUSTAV_CALL RustAV_PlayerReleaseNativeVideoFrame(
    int32_t id,
    int64_t frameIndex);

/*
 * Windows 专属纹理互操作增强能力
 *
 * 这组接口依赖 Windows 图形互操作与 Unity 原生渲染事件。
 * Android / iOS 不应把它当成主播放路径。
 */
int32_t RUSTAV_CALL RustAV_PlayerCreateTexture(const char* path, void* targetTexture);
int32_t RUSTAV_CALL RustAV_PlayerCreateTextureEx(
    const char* path,
    void* targetTexture,
    const RustAVPlayerOpenOptions* options);

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
int32_t RUSTAV_CALL RustAV_GetRenderEventBaseId(void);

#ifdef __cplusplus
}
#endif
