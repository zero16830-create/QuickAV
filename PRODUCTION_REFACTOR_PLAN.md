# RustAV 企业生产级改造计划

## 目标

将 RustAV 从项目集成型 Unity 播放内核收敛为企业生产级媒体 SDK 基线。

## 当前状态

1. M1 已完成：正式 ABI 收敛到 `RustAV_*` 命名空间，头文件、Unity 托管层和 build info 已同步。
2. M2 已完成：`SourceFactory` 已引入，`IAVLibSource` 已能直接提供 decoder，`downcast` 与 URI 分发已从核心层移除。
3. M3 已完成当前范围收口：player runtime state / playback intent / stop reason / seek 契约 / source connection state 已正式化。
4. M4 已完成当前范围收口：`PlaybackClock`、视频 decoder 自动重建、实时同步补偿和 `audio sink delay` 闭环已落地并完成 60 秒 AV soak。
5. M5 已完成当前范围收口：`core / output factory / player registry / unity binding` 边界已拆清，Windows / pull 模式职责明确。
6. M6 已完成：FFmpeg 构建已切到仓库内 vendored `ffmpeg-sys-next 8.0.1`，CI 不再运行时 patch cargo registry。
7. M7 已完成当前范围收口：health snapshot V1/V2、player/source/decoder 统计和发布门槛脚本已齐备。
8. M8 已完成当前范围收口：单测、CI dry-run、Unity 静态编译、60 秒 RTSP/RTMP AV soak 已固化为可执行验收。

## 里程碑

### M1 ABI 收敛 [已完成]
1. 统一导出命名空间为 `RustAV_*`。
2. 引入 ABI 版本与构建信息导出。
3. 建立统一错误码体系。
4. 头文件与真实导出面对齐。
5. Unity 托管层切换到新 ABI。

### M2 核心分层收敛 [已完成主干]
1. 去掉 `AVLibPlayer` 的 URI 前缀分发。 [已完成]
2. 去掉 `AVLibDecoder` 的 downcast 创建 decoder。 [已完成]
3. 引入 `SourceFactory` / `source.Create*Decoder` 抽象。 [已完成]
4. 分离控制面与数据面。 [后续继续深化]

### M3 并发模型收敛 [当前范围已完成]
1. 梳理 player/source/video/audio 线程职责。
2. 减少 polling 与大锁串行访问。
3. 明确 shutdown/seek/reconnect 状态机。 [已完成]

### M4 同步与动态流恢复 [当前范围已完成]
1. 正式化 `PlaybackClock` 模式。
2. 补齐视频 decoder 重建路径。 [已完成分辨率变化重建]
3. 完整处理 timestamp discontinuity / reconnect / seek。 [已完成当前范围收口]
4. 建立同步统计指标。 [已完成]

### M5 平台层收敛 [当前范围已完成]
1. 拆分 core / platform adapter / unity binding。 [已完成]
2. 稳定 Windows D3D11 路径。 [已完成当前范围收口]
3. 固化 iOS / Android pull 模式边界。 [已完成当前范围收口]
4. 为后续硬解/零拷贝预留接口。 [已完成接口边界预留]

### M6 构建与供应链收敛 [已完成]
1. 去掉 CI 对三方 crate 的临时 patch。 [已完成：`ffmpeg-sys-next 8.0.1` 已固化到仓库内，workflow 不再运行时改 cargo registry]
2. 固化 FFmpeg 构建策略。 [已完成]
3. 统一本地/CI/发布构建入口。 [已完成]
4. 固化 Unity 插件产物布局。 [已完成]

### M7 可观测性 [当前范围已完成]
1. 统一日志分级和命名。 [已完成当前范围收口]
2. 导出运行时统计与健康快照。 [已完成]
3. 统一 player/stream/protocol 维度诊断信息。 [已完成当前范围收口]

### M8 质量关口 [当前范围已完成]
1. 单元测试。 [已完成]
2. 集成测试。 [已完成]
3. soak test。 [已完成 60 秒 RTSP/RTMP AV soak，并新增 `scripts/qa/run_av_soak.ps1`]
4. 三平台编译验收。 [Windows / iOS 静态检查入口已纳入发布门槛脚本]
5. 发布前验收清单。 [已完成]

## 执行记录

### 2026-03-06
1. 新增 `RustAV_GetAbiVersion` / `RustAV_GetBuildInfo`。
2. 新增正式错误码 `RustAVErrorCode`。
3. `RustAV.h` 已升级为正式 SDK 头文件。
4. Unity C# 绑定已切到新 ABI。
5. 新增 `SourceFactory`，`Player` 不再直接识别 `rtsp://` / `rtmp://`。
6. `IAVLibSource` 已提供 `CreateVideoDecoder` / `CreateAudioDecoder`。
7. `AVLibDecoder` 不再依赖具体 source 类型。
8. 新增 `RustAV_PlayerGetHealthSnapshot`，用于导出 player 运行时健康信息。
9. 视频 decoder 已支持基于流形态变化的自动重建。
10. 新增基础测试：
   - `tests/fixed_size_queue.rs`
   - `tests/playback_clock.rs`
   - `tests/audio_export_state.rs`
11. `PlayerHealthSnapshot` 已补显式 state、连接尝试、decoder 重建和音视频丢帧计数。
12. `IAVLibSource` 已新增统一 `RuntimeStats()`，RTSP/RTMP 已开始暴露 source packet/timeout/reconnect 计数。
13. GitHub Actions 中 Android/iOS 的三方补丁逻辑已迁入 `scripts/ci`，构建补丁开始版本化管理。
14. iOS 已新增 `ios-staticlib/Cargo.toml`，CI 不再改主 `Cargo.toml` 的 `crate-type`。
15. `scripts/ci/common.py` 已收敛公共路径解析、命令执行和文件落盘逻辑。
16. `scripts/ci/build_unity_plugins.py` 已成为本地 / CI / 发布统一入口，workflow 不再直接散落调用平台脚本。
17. 新增 `src/OutputFactory.rs`，`Player` 已不再直接构造 texture / frame export 输出细节。
18. 新增 `src/PlayerRegistry.rs`，`UnityConnection` / `dllmain` 已不再直接持有 player 表实现。
19. `AVLibPlayer` 已拆分出 `runtime_state + playback_intent + stop_reason` 三层状态语义，health snapshot 不再只暴露混合态。
20. `RustAV_PlayerSeek` 已收成显式结果语义，不再 silent no-op；文件 seek 已允许在非播放态执行，并会同步 flush 音视频缓存。
21. `IAVLibSource` 已新增正式 `ConnectionState()`；RTSP/RTMP/File source 已开始从 `bool IsConnected()` 迁移到枚举连接态。
22. `RustAV_PlayerGetHealthSnapshotV2` 已新增 `size/version` 防护和 `source_connection_state`，旧版 V1 接口保留兼容。
23. `ffmpeg-sys-next 8.0.1` 已固化到 `third_party/ffmpeg-sys-next-8.0.1`，Android/iOS workflow 不再运行时 patch cargo registry。
24. 已新增 `PRODUCTION_RELEASE_CHECKLIST.md`，把 ABI、播放主链、同步与打包门槛固化为发布前检查项。
25. 已新增 `scripts/qa/run_production_gate.ps1`，统一执行 `cargo check/test`、iOS 静态检查、CI 入口 dry-run 和 `audio_probe` 样例门槛。

### 2026-03-09
1. `RustAV_PlayerGetHealthSnapshotV2` 已补齐正式 `size/version` 文档说明，并把过小结构体错误码收敛到 `RustAVErrorCode_BufferTooSmall`。
2. `scripts/qa/run_av_soak.ps1` 已新增，统一执行 RTSP/RTMP 带音频实时链路 soak。
3. `scripts/qa/run_production_gate.ps1` 已接入可选 `-RtspAvUri/-RtmpAvUri`，可直接串起 Rust / CI / Unity 静态编译和 AV soak 门槛。
4. 已完成本地 60 秒 RTSP AV soak：`first_video=0.467s`、`first_audio=0.244s`、`timeouts=0`、`reconnects=0`、`vdrop=0`、`adrop=0`。
5. 已完成本地 60 秒 RTMP AV soak：`first_video=1.257s`、`first_audio=0.244s`、`timeouts=0`、`reconnects=0`、`vdrop=0`、`adrop=0`。
6. 在当前约束范围内，M1-M8 已全部收口到企业生产级工程基线。
7. 已新增 `RustAV_PlayerGetStreamInfo`，用于导出主视频流原始宽高/音频流采样信息，ABI 升级到 `1.2.0`。
8. 已新增 `scripts/qa/run_unity_validation.ps1`，统一执行 Unity 场景级构建、插件同步和文件/RTSP/RTMP 运行验证。
9. Unity 场景级验证包已切到默认窗口模式，并支持 `-windowWidth/-windowHeight` 显式覆盖；未显式覆盖时按源视频尺寸开窗。
10. `scripts/qa/run_unity_validation.ps1` 已补 `av_sync` 统计、阈值判定、完成前样本截断和 `runInBackground` 稳定化。
11. Unity 示例工程已清理到最小运行集合，并复制进 `RustAV/UnityAVExample`。
12. 已新增 Unity Release CI 基础设施：版本号计算、插件注入、版本化压缩打包、GameCI 发布工作流。
