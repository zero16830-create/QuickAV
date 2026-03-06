# AVLibAudioDecoder / A-V Sync 计划

## 目标
- 新增音频解码内核，支持文件源、RTMP，尽量接通 RTSP。
- 建立统一播放时钟，文件流以音频为主时钟，实时流以音频播放时钟/外部时钟为主。
- 先打通内核与验证，再补最小 Unity ABI PCM 导出。

## 里程碑
- M1：新增 AudioFrame / AVLibAudioDecoder / PlaybackClock 骨架（已完成）
- M2：接通 AVLibFileSource 音频包与文件音画同步（已完成）
- M3：接通 RTMP 音频解码与实时同步（已完成）
- M4：接通 RTSP 音频与 RTP 时间轴归一化（已完成）
- M5：本地插桩、日志分析、回归修正（已完成）
- M6：补 Unity/FFI PCM 导出接口与本地拉取验证（已完成）

## 当前状态
- 文件源：音频解码可用，样例文件本地回归 `av_sync delta` 稳定在约 `-17ms ~ -11ms`
- RTMP：本地带音频推流 `rtmp://127.0.0.1:1935/mystream_av` 已跑通，`av_sync delta` 大致在 `-105ms ~ -10ms`
- RTSP：本地带音频推流 `rtsp://127.0.0.1:8554/mystream_av` 已跑通，`retina` 已改成 `Permissive` 初始时间戳策略，FFI 拉流首包约 `0.224s`
- Unity/FFI：已新增 `GetAudioMetaPCM / CopyAudioPCM`，`CreatePlayerPullRGBA` 创建的播放器现在同时带视频帧导出与 PCM 音频导出
- 本地回归：新增 `audio_probe`，已验证文件、RTMP、RTSP 三条链路都能通过 FFI 拉到音频 PCM 数据

## 验证日志摘要
- `cargo check --lib`：通过
- `cargo check --examples`：通过
- `cargo run --example test_player -- --max-seconds=3`：通过，文件源音频解码与同步正常
- `cargo run --example test_player -- --uri=rtmp://127.0.0.1:1935/mystream_av --max-seconds=6`：通过
- `cargo run --example test_player -- --uri=rtsp://127.0.0.1:8554/mystream_av --max-seconds=6`：通过
- `cargo run --example audio_probe -- ../RustAV/TestFiles/SampleVideo_1280x720_10mb.mp4 3`：通过，首个视频帧与首个音频块都在约 `0.081s`
- `cargo run --example audio_probe -- rtmp://127.0.0.1:1935/mystream_av 6`：通过，首个视频帧/音频块约 `0.243s`
- `cargo run --example audio_probe -- rtsp://127.0.0.1:8554/mystream_av 4`：通过，首个视频帧/音频块约 `0.224s`

## 备注
- 本机 `mediamtx + ffmpeg` 这组环境里，RTSP 带音频发布使用 `tcp` 稳定，使用 `udp` 发布时服务端会主动断开；读取侧仍然保持 `UDP 优先 -> TCP 回退`
- 本机 `RTMP` 带音频推流偶发被服务端主动断开，`AVLibRTMPSource` 会自动重连并恢复；`audio_probe` 在该场景下仍能继续拿到视频帧与 PCM 音频
