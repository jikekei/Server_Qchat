# Server_Qcha（LabAPI 版）

SCPSL 服务端插件：把游戏服与 QQ 机器人后台双向打通。

本目录是 `server/`（EXILED 版）的 **LabAPI 迁移版本**，功能等价、**通信协议完全一致，机器人端无需任何改动**。

- 迁移对照表与行为差异 → [MIGRATION-EXILED-to-LABAPI.md](MIGRATION-EXILED-to-LABAPI.md)
- TCP 协议细节 → 上级目录 [`通信协议文档.md`](../通信协议文档.md)

## 运行环境

| 项目 | 值 |
|---|---|
| 目标框架 | .NET Framework 4.8（`net48`） |
| 插件 API | LabAPI **1.1.7** |
| 插件基类 | `LabApi.Loader.Features.Plugins.Plugin<Config>` |
| 编译引用 | LabAPI NuGet 包；游戏程序集取自本机 `SCPSL_Data/Managed` 目录（不提交到仓库） |

### 编译准备

从 Steam 安装 SCP:SL Dedicated Server，并将 `SCPSL_REFERENCES` 环境变量指向该服务器的 `SCPSL_Data/Managed` 目录。`LabApi` API 通过官方 [`Northwood.LabAPI`](https://www.nuget.org/packages/Northwood.LabAPI) NuGet 包还原；游戏本身的程序集只从本机安装目录读取。然后还原并编译：

```powershell
$env:SCPSL_REFERENCES = 'C:\path\to\SCPSL_Data\Managed'
dotnet restore server-labapi/Server_Qcha.csproj
dotnet build server-labapi/Server_Qcha.csproj -c Release
```

## 构建

```bash
dotnet build server-labapi/Server_Qcha.csproj -c Release
```

产物：`server-labapi/bin/Release/Server_Qcha.dll`（约 35 KB，单文件，不含依赖副本）。

也可以打开 `server/Server_Qcha.Server.sln` —— 里面同时挂了 EXILED 版和 LabAPI 版两个项目，方便对照。

> EXILED 版用的是旧式 csproj，只能用 MSBuild / Visual Studio 构建；
> 本目录是 SDK 风格 csproj，`dotnet build` 与 MSBuild 都可以。

## 部署

1. 把 `Server_Qcha.dll` 放进 LabAPI 的**按端口划分**的插件目录：

   ```
   %APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<服务器端口>\Server_Qcha.dll
   ```

   端口取服务端 `config_gameplay.txt` 里的 `port`。想让所有端口共用就放 `plugins\global\`。

2. 启动一次服务器，LabAPI 会自动生成配置：

   ```
   %APPDATA%\SCP Secret Laboratory\LabAPI\configs\<服务器端口>\Server_Qcha\
     ├─ config.yml       ← 本插件配置（键名与 EXILED 版一致，可直接拷旧配置覆盖）
     └─ properties.yml   ← LabAPI 保留文件，is_enabled 控制是否启用本插件
   ```

3. 按需改 `config.yml`，重启服务器（或游戏内执行 `reload configs`）。

> **LabAPI 与 EXILED 冲突，同一端口不要同时加载两者。** 迁移期间建议先挑一个测试端口验证。

## 配置项

`config.yml` 由插件首次启动自动生成，字段含义与原 EXILED 版完全对应：

| 键 | 默认值 | 说明 |
|---|---|---|
| `tcp_port` | `10087` | 命令通道起始监听端口，被占用时自动 +1 探测（最多 100 个） |
| `ip` | `127.0.0.1` | 命令通道监听地址 |
| `server_name` | `1服` | 服名，会出现在 QQ 群回显里 |
| `content_text` | 空 | `display_mode = 1` 时显示的内容 |
| `display_mode` | `2` | `0` 显示查询时间 / `1` 显示 `content_text` / `2` 留空 |
| `bot_ip` | `127.0.0.1` | QQ 机器人后台监听地址 |
| `bot_port` | `10088` | QQ 机器人后台监听端口 |
| `auth_token` | `QchaSecret_123` | 双向鉴权 Token，**必须与机器人端一致** |
| `sort_order` | `0` | 排序权重，`>0` 按此排序，`=0` 自动 |
| `connect_host` | 空 | 机器人回连本服用的 IP；跨机部署必填，同机留空 |
| `debug` | `false` | 打开后输出命令收发、心跳明细等调试日志 |

## 与机器人端对接

沿用 EXILED 版完全相同的端口、Token 与报文格式：

- **命令通道**：本插件监听 `tcp_port`（默认 10087），机器人主动连入，一问一答。
- **通知通道**：本插件连出 `bot_ip:bot_port`（默认 `127.0.0.1:10088`），发送 `register` / `heartbeat` / `unregister` / `ac`。

由插件主动外连，所以游戏服**不需要放开入站端口**，内网 / NAT 环境可直接用。

---

## 许可证

Copyright 2025 hmyhserver.top
