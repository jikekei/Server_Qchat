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

Bot 和守护进程的可执行文件位于 ZIP 根目录，DLL 和 .NET 运行时统一放在 `runtime/`。两者共用根目录的 `appsettings.json`，面板静态资源保存在 `wwwroot/`。Windows 包另含启动脚本。升级旧布局时，解压到新目录，再迁移现有配置和 `data/` 等运行数据。替换正在托管游戏服的守护进程需安排维护窗口。

每次发布时，在 GitHub 发布说明顶部用 `> [!NOTE]` 写明升级所依据的版本和需要更新的组件，其余组件注明「无需更新」。仅同步版本号的组件也写「无需更新」。需要重启守护进程时，用 `> [!WARNING]` 提醒安排维护窗口，并说明托管游戏服会停止、在线玩家会断开。发布页同时提供两套插件包，沿用旧版时标明实际插件版本。

发布任务会解压 Windows 和 Linux ZIP，核对目录布局，再启动包内的守护进程和 Bot，验证守护进程的健康接口及面板静态资源。检查通过后才上传 ZIP 和校验文件。

可在仓库根目录执行以下命令生成本地发布目录，目标目录须尚未存在。

```powershell
./bot/scripts/Publish-Release.ps1 -Runtime win-x64 -OutputDirectory dist/bot-win-x64
```

完整 EXILED / LabAPI 插件依赖本地合法安装的游戏程序集 `SCPSL_REFERENCES`，不在这些公开 CI 包中。当前 CI 验证共享协议及两种 TCP 实现，不能代替游戏内插件兼容性验证。历史 LocalAdmin 冒烟脚本也不作为 CI 检查：它依赖 Windows 本机进程布局，需要单独维护。

## 依赖更新与故障处理

Dependabot 每周一北京时间 10:00 检查 GitHub Actions 与全部 NuGet 项目，按组创建更新 PR。PR 自动执行 CI；不自动合并依赖升级。
失败时打开 Actions 对应运行，查看红色任务及 TRX 报告，修复后提交即可重新检查。

配置对应 GitHub 官方说明：[工作流语法](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、[.NET 构建测试](https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing-net)。
