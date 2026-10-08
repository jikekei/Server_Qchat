# 自动化工作流

所有生效配置均在仓库根目录 `.github/` 下。

## 持续集成

`CI` 在向 main 推送、提交 PR、手动运行和每周一北京时间 10:00 时执行。
Windows / Linux 分别测试 EXILED 与 LabAPI 的 TCP 监听源码，构建当前机器人与独立守护进程；另一个 Windows 任务测试旧版机器人并编译轻量 API。
测试报告在 Actions 的 Artifacts 中保留 14 天。真实数据库测试明确排除，流程不需要生产密钥。

`global.json` 固定 .NET 8 SDK 系列。NuGet 配置仅使用公开源，干净的 GitHub 执行环境不依赖开发者的 Windows 离线包目录。

## 打包

手动运行 `Release packages` 可以生成预览包。推送 `vX.Y.Z` 或 `vX.Y.Z-suffix` 标签时，版本必须与机器人项目的 Version 一致；检查通过后生成 win-x64 / linux-x64 自包含 ZIP、SHA256 校验文件，并上传到 GitHub Release 草稿。
草稿不会自动对外发布，也不部署或重启生产服务。版本发布不需要额外密钥，写权限仅授予创建草稿的任务。

ZIP 内包含机器人、wwwroot、默认配置、许可证，以及与机器人同目录的独立守护进程，共用机器人配置。Windows 包另含启动脚本。Linux 解压后需为两个本机可执行文件设置执行权限，并手动启动守护进程；当前自动拉起逻辑使用 Windows 的 .exe 文件名。

完整 EXILED / LabAPI 插件依赖本地合法安装的游戏程序集 `SCPSL_REFERENCES`，不在这些公开 CI 包中。当前 CI 验证共享协议及两种 TCP 实现，不能代替游戏内插件兼容性验证。历史 LocalAdmin 冒烟脚本也不作为 CI 检查：它依赖 Windows 本机进程布局，需要单独维护。

## 依赖更新与故障处理

Dependabot 每周一北京时间 10:00 检查 GitHub Actions 与全部 NuGet 项目，按组创建更新 PR。PR 自动执行 CI；不自动合并依赖升级。
失败时打开 Actions 对应运行，查看红色任务及 TRX 报告，修复后提交即可重新检查。

配置对应 GitHub 官方说明：[工作流语法](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、[.NET 构建测试](https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing-net)。
