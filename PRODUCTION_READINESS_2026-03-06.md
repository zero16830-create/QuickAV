# RustAV 生产级基线验收报告（2026-03-06）

## 范围

本报告只覆盖本轮已实际验证的范围：

1. `RustAV` native 内核
2. Windows 本地原生测试器 `examples/test_player.rs`
3. 实时协议：`RTSP`、`RTMP`
4. Unity 侧只做静态编译，不做场景运行验收

不在本报告内的范围：

1. iOS 真机运行验收
2. Android 真机运行验收
3. Unity 场景端到端运行验收
4. 超过 60 秒的长期 soak

## 本轮收口内容

1. `test_player` 已支持视频显示 + 原生 Windows 音频输出
2. 音频输出链加入启动缓冲、低水位补水、防碎片提交
3. 音频线程启用 1ms 定时器精度和高优先级
4. 音频输出延迟已回灌 native 主时钟
5. 播放器内核加入实时同步补偿环
6. `RTMP` 时间轴已修正为共享媒体 origin
7. Unity `MediaPlayerPull.cs` 已静态接入 `SetAudioSinkDelaySeconds`
8. 音频导出改为严格顺序消费，不再丢弃已到期音频帧
9. `RustAVAudioMeta` 已补 `sample_format`，PCM ABI 自描述
10. `time_sec` 会随 `CopyAudioPCM` 的部分消费连续前移
11. `SetAudioSinkDelaySeconds` 不再被 `ResetAudioClock` 隐式清零
12. 音频 decoder 在实时流参数变化时会自动重建
13. Unity C# 侧已统一切到 `rustav_native / __Internal`
14. `NativeInitializer` 已改为静态持有回调，并使用独立渲染事件宿主
15. `MediaPlayer` / `MediaPlayerPull` 已补原生句柄、`Texture2D`、`AudioClip` 释放

## 验证命令

### Rust 静态检查

```powershell
cargo check --lib --examples
```

结果：通过

### Unity 静态编译

```powershell
dotnet msbuild D:/TestProject/Video/UnityAV/Solution/UnityAV/UnityAV.csproj `
  /t:Build `
  /p:Configuration=Debug `
  /p:TargetFrameworkVersion=v4.8 `
  /p:PostBuildEvent=
```

结果：通过  
产物：`UnityAV/Solution/UnityAV/bin/Debug/UnityAV.dll`

### 60 秒实时压力跑

```powershell
cargo run --example test_player -- --uri=rtsp://127.0.0.1:8554/mystream_av --width=1280 --height=720 --max-seconds=60
cargo run --example test_player -- --uri=rtmp://127.0.0.1:1935/mystream_av --width=1280 --height=720 --max-seconds=60
```

日志文件：

1. `target/rtsp_sync_60s.log`
2. `target/rtmp_sync_60s.log`

### 二次修正后的短回归

```powershell
cargo run --example test_player -- --uri=rtsp://127.0.0.1:8554/mystream_av --width=1280 --height=720 --max-seconds=6
cargo run --example test_player -- --uri=rtmp://127.0.0.1:1935/mystream_av --width=1280 --height=720 --max-seconds=6
```

日志文件：

1. `target/rtsp_sync_final.log`
2. `target/rtmp_sync_final.log`

结果摘要：

1. RTSP：`underflow = 0`，`count = 4`，`p95_abs = 56.2 ms`
2. RTMP：`underflow = 0`，`count = 4`，`p95_abs = 52.0 ms`

补充说明：

1. 这轮日志主要用于确认音频顺序消费、Unity FFI 语义修正和播放器资源释放没有引入回归。
2. 长时间同步统计仍以前面的 60 秒压力跑结果为主。

## 统计结果

### RTSP

全量统计：

1. `count = 54`
2. `min = -58.7 ms`
3. `max = 33.0 ms`
4. `avg = -7.5 ms`
5. `p95_abs = 52.1 ms`
6. `p99_abs = 58.7 ms`
7. `underflow = 0`
8. `audio_clock_reset = 3`

去掉前 5 条暖机样本后：

1. `count = 49`
2. `min = -58.6 ms`
3. `max = 33.0 ms`
4. `avg = -6.5 ms`
5. `p95_abs = 45.4 ms`
6. `p99_abs = 58.6 ms`

### RTMP

全量统计：

1. `count = 54`
2. `min = -59.9 ms`
3. `max = 57.0 ms`
4. `avg = -2.9 ms`
5. `p95_abs = 49.0 ms`
6. `p99_abs = 59.9 ms`
7. `underflow = 0`
8. `audio_clock_reset = 4`

去掉前 5 条暖机样本后：

1. `count = 49`
2. `min = -59.9 ms`
3. `max = 36.3 ms`
4. `avg = -3.6 ms`
5. `p95_abs = 41.0 ms`
6. `p99_abs = 59.9 ms`

## 结果判断

在当前验证范围内，给出以下判断：

1. Windows 原生测试播放链已经达到可交付的生产级工程基线
2. `RTSP/RTMP` 两条实时链路在 60 秒压力跑内均无 `underflow`
3. 稳态同步偏差已压到 `±60ms` 级别
4. 该结果足以作为后续 Unity / iOS / Android 真机验证的基线版本

## 风险与边界

当前仍保留以下边界：

1. 本轮没有做 Unity 场景级运行验收
2. 本轮没有做 iOS / Android 真机验收
3. `audio_clock_reset` 仍会在本地循环测试流时间轴跳变时出现
4. 仍未完成 2 小时 / 24 小时级长期 soak
5. Unity 侧这轮只做了静态编译和代码级硬化，尚未做场景级播放验证

## 后续建议

1. 在 Unity Windows 场景中做端到端实跑验收
2. 用同一套 `SetAudioSinkDelaySeconds` 闭环推进 iOS / Android 真机验证
3. 补 2 小时以上 soak test，观察是否仍保持 `underflow = 0`
