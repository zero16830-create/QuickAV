# Unity 插件 ABI 说明

## 当前状态

RustAV 当前对 Unity 提供一套正式命名空间 ABI，公共导出统一收敛为 `RustAV_*`。

例外只有 Unity 引擎约定入口：

1. `UnityPluginLoad`
2. `UnityPluginUnload`

这两个函数名必须保持精确匹配，不能重命名。

## ABI 版本

1. `RustAV_GetAbiVersion() -> uint32_t`
2. `RustAV_GetBuildInfo() -> const char*`

当前 ABI 版本：`1.2.0`

## 错误码

所有控制类接口统一使用以下负值错误码：

1. `RustAVErrorCode_Ok = 0`
2. `RustAVErrorCode_InvalidArgument = -1`
3. `RustAVErrorCode_InvalidPlayer = -2`
4. `RustAVErrorCode_CreateFailed = -3`
5. `RustAVErrorCode_BufferTooSmall = -4`
6. `RustAVErrorCode_UnsupportedOperation = -5`
7. `RustAVErrorCode_InvalidState = -6`

约定：

1. `< 0` 表示错误
2. `0` 表示成功或当前无数据
3. `> 0` 仅用于“已有帧 / 已有音频 / 实际复制字节数”这类查询结果

## 播放器接口

### 创建

1. `RustAV_PlayerCreateTexture(path, targetTexture) -> int32_t`
   - 仅用于 Windows D3D11 纹理写入路径。
2. `RustAV_PlayerCreatePullRGBA(path, targetWidth, targetHeight) -> int32_t`
   - 适用于 Windows / iOS / Android。
   - 同时启用 RGBA 帧导出和 PCM 音频导出。

### 生命周期与控制

1. `RustAV_PlayerRelease(id) -> int32_t`
2. `RustAV_PlayerUpdate(id) -> int32_t`
3. `RustAV_PlayerGetDuration(id) -> double`
4. `RustAV_PlayerGetTime(id) -> double`
5. `RustAV_PlayerPlay(id) -> int32_t`
6. `RustAV_PlayerStop(id) -> int32_t`
7. `RustAV_PlayerSeek(id, time) -> int32_t`
8. `RustAV_PlayerSetLoop(id, loopValue) -> int32_t`
9. `RustAV_PlayerSetAudioSinkDelaySeconds(id, delaySec) -> int32_t`
10. `RustAV_PlayerGetHealthSnapshot(id, outSnapshot) -> int32_t`
11. `RustAV_PlayerGetHealthSnapshotV2(id, outSnapshot) -> int32_t`
12. `RustAV_PlayerGetStreamInfo(id, streamIndex, outInfo) -> int32_t`

说明：

1. `RustAV_PlayerUpdate` 在 Windows 纹理路径下用于驱动写入，在 Pull 路径下可安全调用。
2. `RustAV_PlayerSeek` 现在返回状态码，不再返回伪造的 `double`。
3. `RustAV_PlayerSeek` 允许在已连接、可 seek 且未 shutdown 的状态下使用，不再要求必须处于播放中。
4. `RustAV_PlayerStop` 当前语义是“停止播放意图并保留当前位置”，不会自动回到开头。
5. `RustAV_PlayerSetAudioSinkDelaySeconds` 用于把宿主音频设备仍未真正播出的缓冲时长回写给 native。

## 流信息接口

### 结构

```c
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
```

### 接口

1. `RustAV_PlayerGetStreamInfo(id, streamIndex, outInfo) -> int32_t`

说明：

1. 调用方应先初始化 `outInfo->struct_size = sizeof(RustAVStreamInfo)`。
2. 当前 `struct_version = RUSTAV_STREAM_INFO_VERSION`。
3. 视频流返回 `width/height`，音频流返回 `sample_rate/channels`。
4. 这个接口用于宿主获取源流原始尺寸，不依赖当前 RGBA 导出目标尺寸。

## 健康快照

### V1 兼容接口

`RustAV_PlayerGetHealthSnapshot` 保留为兼容接口，继续返回旧版健康快照布局。

适用场景：

1. 已经绑定旧结构的调用方
2. 只需要最小健康信息且不区分 source 连接细分状态的调用方

### V2 推荐接口

`RustAV_PlayerGetHealthSnapshotV2` 是推荐的新接口。调用方应先初始化：

1. `outSnapshot->struct_size = sizeof(RustAVPlayerHealthSnapshotV2)`
2. `outSnapshot->struct_version = RUSTAV_PLAYER_HEALTH_SNAPSHOT_V2_VERSION`

native 侧会校验结构体大小并回填最终版本信息。
如果 `struct_size` 小于当前 ABI 需要的大小，返回 `RustAVErrorCode_BufferTooSmall`。

V2 会返回当前 player 的最小运行时健康信息，包括：

1. 连接状态
2. 播放状态与显式 player state
3. 实时流 / 可 seek / 循环标志
4. stream 数、视频 decoder 数、音频 decoder 是否存在
5. duration / current time / external clock / audio clock / presented audio clock
6. audio sink delay
7. 当前视频同步补偿量
8. connect attempt 计数
9. video/audio decoder 重建计数
10. video/audio 丢帧计数
11. source packet/timeout/reconnect 计数
12. source 是否处于连接检查阶段
13. source 最近活动距离当前的秒数
14. source 细分连接状态 `source_connection_state`

这个接口用于：

1. 宿主运行时自检
2. 自动化测试验收
3. 线上问题快速定位

当前 `state` 取值：

1. `0 = Idle`
2. `1 = Connecting`
3. `2 = Ready`
4. `3 = Playing`
5. `4 = Paused`
6. `5 = Shutdown`
7. `6 = Ended`

补充字段：

1. `runtime_state`
   - 反映 source/decoder 运行态，不混入播放意图
2. `playback_intent`
   - `0 = Stopped`
   - `1 = PlayRequested`
3. `stop_reason`
   - `0 = None`
   - `1 = UserStop`
   - `2 = EndOfStream`
4. `source_connection_state`
   - `0 = Disconnected`
   - `1 = Connecting`
   - `2 = Connected`
   - `3 = Reconnecting`
   - `4 = Checking`

## RGBA 帧导出

### 元信息结构

```c
typedef struct RustAVFrameMeta {
    int32_t width;
    int32_t height;
    int32_t format;
    int32_t stride;
    int32_t data_size;
    double  time_sec;
    int64_t frame_index;
} RustAVFrameMeta;
```

### 接口

1. `RustAV_PlayerGetFrameMetaRGBA(id, outMeta) -> int32_t`
   - `1`：已有帧
   - `0`：当前无帧
   - `<0`：错误
2. `RustAV_PlayerCopyFrameRGBA(id, dst, dstLen) -> int32_t`
   - `>0`：实际复制字节数
   - `0`：当前无帧
   - `<0`：错误

## PCM 音频导出

### 元信息结构

```c
typedef enum RustAVAudioSampleFormat {
    RustAVAudioSampleFormat_Unknown = 0,
    RustAVAudioSampleFormat_F32 = 1
} RustAVAudioSampleFormat;

typedef struct RustAVAudioMeta {
    int32_t sample_rate;
    int32_t channels;
    int32_t bytes_per_sample;
    int32_t sample_format;
    int32_t buffered_bytes;
    double  time_sec;
    int64_t frame_index;
} RustAVAudioMeta;
```

### 接口

1. `RustAV_PlayerGetAudioMetaPCM(id, outMeta) -> int32_t`
   - `1`：已有 PCM
   - `0`：当前无 PCM
   - `<0`：错误
2. `RustAV_PlayerCopyAudioPCM(id, dst, dstLen) -> int32_t`
   - `>0`：实际复制字节数
   - `0`：当前无 PCM
   - `<0`：错误

说明：

1. 音频导出是消费式读取。
2. `time_sec` 会随着已消费字节持续前移。
3. 当前样本格式固定为 `float32 interleaved PCM`。

## 日志接口

1. `RustAV_DebugInitialize(cacheLogs)`
2. `RustAV_DebugTeardown()`
3. `RustAV_DebugClearCallbacks()`
4. `RustAV_DebugRegisterLogCallback(callback)`
5. `RustAV_DebugRegisterWarningCallback(callback)`
6. `RustAV_DebugRegisterErrorCallback(callback)`

日志回调在 Windows 上使用 `__stdcall`，其他平台使用 C 默认调用约定。

## Unity 渲染事件

1. `UnityPluginLoad(void* interfaces)`
2. `UnityPluginUnload(void)`
3. `RustAV_GetRenderEventFunc() -> RustAVRenderEventFunc`

说明：

1. `OnRenderEvent` 不再作为公共 ABI 暴露。
2. Unity C# 层应通过 `RustAV_GetRenderEventFunc` 获取回调指针。

## 推荐调用顺序

### Windows D3D11 纹理模式

1. `id = RustAV_PlayerCreateTexture(uri, texturePtr)`
2. `RustAV_PlayerPlay(id)`
3. 每帧调用 `RustAV_PlayerUpdate(id)` 或渲染事件驱动
4. 结束时调用 `RustAV_PlayerRelease(id)`

### 通用 Pull 模式

1. `id = RustAV_PlayerCreatePullRGBA(uri, width, height)`
2. `RustAV_PlayerPlay(id)`
3. 每帧调用：
   - `RustAV_PlayerUpdate(id)`
   - `RustAV_PlayerGetFrameMetaRGBA(...)`
   - `RustAV_PlayerCopyFrameRGBA(...)`
   - `RustAV_PlayerGetAudioMetaPCM(...)`
   - `RustAV_PlayerCopyAudioPCM(...)`
4. 若宿主音频设备存在未播放缓冲，调用 `RustAV_PlayerSetAudioSinkDelaySeconds(id, delaySec)`
5. 结束时调用 `RustAV_PlayerRelease(id)`

## 线程约束

1. 同一个 `player id` 应由宿主统一线程调度，不要跨线程交叉控制。
2. `CopyFrameRGBA` 返回独立副本，调用方应自行管理缓冲区生命周期。
3. `CopyAudioPCM` 为消费式读取，宿主应自行维护音频环形缓冲。
4. 生产级实时同步要求宿主周期性回写 `RustAV_PlayerSetAudioSinkDelaySeconds`。

## Unity C# 当前绑定

1. Windows / Android：`DllImport("rustav_native")`
2. iOS：`DllImport("__Internal")`
3. `MediaPlayer.cs` 已切换到新的 `RustAV_Player*` 命名
4. `MediaPlayerPull.cs` 已切换到新的 `RustAV_Player*` 命名
5. `NativeInitializer.cs` 已切换到新的 `RustAV_Debug*` 和 `RustAV_GetRenderEventFunc`
