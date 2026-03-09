# Unity 自动发布链路

## 目标

把 Rust 插件构建、Unity 示例程序构建、Tag 和 Release 收敛到一条 GitHub Actions 流水线。

## 工作流

工作流文件：`.github/workflows/unity-release.yml`

触发方式：

1. push 到 `main` / `master`
2. 手工 `workflow_dispatch`

执行顺序：

1. 计算版本号和 Tag
2. 并行构建 Windows / Android / iOS Unity 插件
3. 组装 `UnityPlugins` 目录并打 `RustAV-UnityPlugins-v<version>.zip`
4. 将插件中的原生运行时和 `Assets/UnityAV/UnityAV.Runtime.asmdef + Runtime` 注入 `RustAV/UnityAVExample`
5. 使用 `game-ci/unity-builder@v4` 构建 Windows / Android / iOS Unity 示例程序
6. 打包各平台 Unity 产物
7. 自动创建 Git Tag 和 GitHub Release

## 版本策略

版本入口：`scripts/ci/compute_release_version.py`

规则：

1. 若仓库没有历史 `vX.Y.Z` Tag，则使用 `Cargo.toml` 中的版本
2. 若已有历史 Tag，且 `Cargo.toml` 未提升主次版本，则自动递增 patch
3. 最终 Tag 格式为 `v<version>`

## 必要 Secrets

GameCI 构建 Unity 需要以下 GitHub Secrets：

1. `UNITY_LICENSE`
2. `UNITY_EMAIL`
3. `UNITY_PASSWORD`

## Release 资产

Release 至少包含：

1. `RustAV-UnityPlugins-v<version>.zip`
2. `RustAVExample-Windows64-v<version>.zip`
3. `RustAVExample-Android-v<version>.zip`
4. `RustAVExample-iOS-v<version>.zip`

补充约束：

1. `RustAV-UnityPlugins-v<version>.zip` 必须同时包含 `Assets/UnityAV/UnityAV.Runtime.asmdef + Runtime` 和各平台 `Assets/Plugins` 原生插件
2. `UnityAVExample` 仓库内自带 `Editor / Validation / Materials / Scenes`，CI 注入时只覆盖运行时子树，不覆盖示例工程资源
3. 发布链路不再依赖或生成 `UnityAV.dll`
