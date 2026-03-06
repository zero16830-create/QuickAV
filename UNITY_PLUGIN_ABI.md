# Unity 插件 ABI 说明

## 目标

RustAV 当前面向 Unity 插件提供两条视频输出路径：

1. Windows D3D11 纹理写入。
2. 通用 CPU RGBA 拉帧导出（Windows / iOS / Android 统一）。
3. 通用 PCM 音频拉取导出（随 `CreatePlayerPullRGBA` 一并启用）。

推荐策略：

1. Windows 如已持有 Unity D3D11 纹理，优先使用纹理写入。
2. iOS / Android 统一使用 RGBA 拉帧接口。
3. 如需 Unity 播放声音，使用 `GetAudioMetaPCM / CopyAudioPCM` 将 PCM 喂给 `AudioClip` 或平台音频设备。

## 导出函数

### 播放器创建

1. `GetPlayer(path, targetTexture) -> int`
   - 仅适用于 Windows D3D11 纹理路径。
   - 成功返回 `player id`。
   - 失败返回 `-1`。
2. `CreatePlayerPullRGBA(path, targetWidth, targetHeight) -> int`
   - 适用于 Windows / iOS / Android。
   - `targetWidth`、`targetHeight` 必须大于 `0`。
   - 成功返回 `player id`。
   - 失败返回 `-1`。

### 生命周期控制

1. `Play(id) -> int`
   - 启动拉流和解码节奏。
   - 成功返回 `0`，失败返回 `-1`。
2. `Stop(id) -> int`
   - 停止播放。
   - 成功返回 `0`，失败返回 `-1`。
3. `ReleasePlayer(id) -> int`
   - 释放播放器。
   - 成功返回 `1`，失败返回 `-1`。

### 运行期辅助

1. `UpdatePlayer(id) -> int`
   - Windows 纹理模式：用于执行纹理写入。
   - RGBA 拉帧模式：可安全调用，但当前不会产生额外写入动作。
2. `Duration(id) -> double`
3. `Time(id) -> double`
4. `Seek(id, time) -> double`
5. `SetLoop(id, loopValue) -> int`
6. `SetAudioSinkDelaySeconds(id, delaySec) -> int`
   - 将平台音频设备当前仍未真正播出的缓冲时长回写给 native。
   - `delaySec` 必须是非负有限数。
   - 不回写该值也能播放，但实时音画同步精度会下降。

## RGBA 拉帧接口

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

字段语义：

1. `width` / `height`：当前导出帧尺寸。
2. `format`：当前固定为 `PIXEL_FORMAT_RGBA32`。
3. `stride`：每行字节数，当前等于 `width * 4`。
4. `data_size`：当前整帧字节数。
5. `time_sec`：帧时间戳，单位秒。
6. `frame_index`：导出帧序号，单调递增。

### 取帧函数

1. `GetFrameMetaRGBA(id, outMeta) -> int`
   - 返回 `1`：已有帧，`outMeta` 已填充。
   - 返回 `0`：播放器有效，但当前还没有首帧。
   - 返回 `-1`：参数无效、`id` 无效，或该播放器不是 RGBA 拉帧模式。
2. `CopyFrameRGBA(id, dst, dstLen) -> int`
   - 返回 `> 0`：实际复制的字节数。
   - 返回 `0`：当前还没有可用帧。
   - 返回 `-1`：参数无效，或目标缓冲区长度不足。

## PCM 音频拉取接口

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

字段语义：

1. `sample_rate`：当前 PCM 采样率。
2. `channels`：声道数。
3. `bytes_per_sample`：单样本字节数。
4. `sample_format`：当前样本格式，现阶段固定为 `RustAVAudioSampleFormat_F32`。
5. `buffered_bytes`：当前 native 内部待消费的 PCM 字节数。
6. `time_sec`：当前队首 PCM 的时间戳，单位秒；消费式读取后会随已消费字节持续前移。
7. `frame_index`：导出音频块序号，单调递增。

### 取音频函数

1. `GetAudioMetaPCM(id, outMeta) -> int`
   - 返回 `1`：已有 PCM，`outMeta` 已填充。
   - 返回 `0`：播放器有效，但当前还没有首个音频块。
   - 返回 `-1`：参数无效、`id` 无效，或该播放器不支持音频导出。
2. `CopyAudioPCM(id, dst, dstLen) -> int`
   - 返回 `> 0`：实际复制的字节数。
   - 返回 `0`：当前没有可消费 PCM。
   - 返回 `-1`：参数无效，或目标缓冲区长度不足。
   - 该接口是**消费式读取**，复制成功后内部缓冲会前移。

## 推荐调用顺序

### Windows 纹理模式

1. `id = GetPlayer(uri, texturePtr)`
2. `Play(id)`
3. Unity 渲染循环中调用 `UpdatePlayer(id)` 或渲染事件回调
4. 结束时调用 `ReleasePlayer(id)`

### 通用 RGBA 拉帧模式

1. `id = CreatePlayerPullRGBA(uri, width, height)`
2. `Play(id)`
3. 每帧调用：
   - `GetFrameMetaRGBA(id, &meta)`
   - 若返回 `1`，分配或复用 `meta.data_size` 大小的缓冲区
   - `CopyFrameRGBA(id, buffer, bufferLen)`
   - `GetAudioMetaPCM(id, &audioMeta)`
   - 若返回 `1`，分配或复用 `audioMeta.buffered_bytes` 大小的缓冲区
   - `CopyAudioPCM(id, audioBuffer, audioBufferLen)`
4. 将数据上传到 Unity `Texture2D`
5. 将 `float32 interleaved PCM` 写入 Unity `AudioClip` 或平台音频输出
6. 若已知平台音频输出仍有设备缓冲，调用 `SetAudioSinkDelaySeconds(id, delaySec)`
7. 结束时调用 `ReleasePlayer(id)`

## 音画同步闭环

1. Native 主时钟默认使用音频主时钟。
2. `GetAudioMetaPCM / CopyAudioPCM` 只表示 PCM 已经导出，不代表用户已经真正听到声音。
3. 平台音频层应周期性调用 `SetAudioSinkDelaySeconds`，把设备缓冲时长回写给 native。
4. Windows `test_player` 已接入此闭环。
5. `UnityAV/Solution/UnityAV/MediaPlayerPull.cs` 已接入 `AudioClip` 环形缓冲和 Unity DSP buffer 的延迟估算。

## 线程与内存约束

1. FFI 层建议由 Unity 主线程统一调度，避免跨线程交叉控制同一 `player id`。
2. `CopyFrameRGBA` 会把内部最新帧复制到调用方缓冲区，调用方拿到的是独立副本。
3. `GetFrameMetaRGBA` 只返回最新帧快照，不保证保留历史帧。
4. 若需要固定分辨率贴图，Unity 侧应按创建时的目标宽高分配 `Texture2D`。
5. `CopyAudioPCM` 是消费式接口，Unity 侧应维护自己的环形音频缓冲，避免音频线程直接跨线程控制 native player。
6. Unity 托管层当前已通过 `NativePlugin.cs` 统一原生库名：
   - Windows / Android：`rustav_native`
   - iOS：`__Internal`

## 当前平台约束

1. `GetPlayer` 目前只对 Windows D3D11 有效。
2. `CreatePlayerPullRGBA` 是当前三端统一的最小公共能力。
3. Android 云端产物为 `librustav_native.so`。
4. iOS 云端产物为 `RustAV.xcframework`，打包头文件位于 `include/RustAV.h`。
5. Unity iOS 侧 `DllImport` 应使用 `__Internal`，Android / Windows 侧使用 `rustav_native`。
6. 三端如需生产级实时同步，音频输出层都应接入 `SetAudioSinkDelaySeconds`。
