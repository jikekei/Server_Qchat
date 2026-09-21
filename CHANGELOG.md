# 更新日志 (Changelog)

## v2.0.0 — 2026-09-21

### 架构级重大升级与新特性

#### 1. 独立常驻守护进程架构 (Server_Qcha.Daemon)
- **进程生命周期彻底解耦**：将 SCPSL 游戏服务端的父进程托管职责剥离为独立的轻量常驻守护进程 `Server_Qcha.Daemon.exe`。
- **零宕机平滑运维**：Web 面板或 QQ 机器人因更新、配置修改、重启或意外异常退出时，**底层守护进程与游戏服务端持续平稳运行，在线玩家零感知、不掉线！**
- **自拉起与互联机制**：Bot 默认通过 `AutoStartDaemon: true` 自动探测并拉起守护进程，两者通过本地带密钥鉴权的轻量 HTTP API 进行毫秒级内部通信。

#### 2. Web 管理面板增加守护进程监控与生命周期控制
- **实时性能看板卡片**：在「服务器进程」页面顶部集成守护进程核心指标卡片，实时呈现运行状态指示灯、PID、物理工作集内存 (MB)、专用内存 (MB)、进程运行时长 (天/时/分/秒)、并发线程数以及托管游戏服数量。
- **生命周期一键控制**：提供守护进程的启动、平滑停止、重启与手动刷新状态功能，支持带二次确认的安全关机防护。

#### 3. QQ 官方机器人开放平台 (OpenAPI v2) 原生集成
- **官方合规协议接入**：完整对接腾讯 QQ 开放平台 OpenAPI v2，基于 WebSocket 长连接保活与 AccessToken 动态刷新。
- **多端消息收发与分段**：支持群聊（需 @机器人）与私聊指令交互，内置被动回复配额管理（`msg_seq`）与长文本自动智能分段下发。
- **官方输入面板 (/v2/panels) 自动化管理**：自动根据系统指令集生成符合腾讯规范的输入面板结构，管理员敏感指令自动附带 `only_admin: true` 权限控制，并支持在 Web 面板上一键同步或清空。

#### 4. 双接入引擎在线平滑热切换
- 支持在 NapCat (OneBot 11) 与 QQ 官方机器人之间在线切换，自动断开旧连接并启动新协议引擎，无需重启主程序。

#### 5. 运行时数据目录集中收敛
- 引入统一的数据目录隔离机制，所有本地运行时生成的持久化文件统一收敛至 `data/` 目录（包含 `panel.db*`、`bot-settings.json`、`localadmin-servers.json` 等），便于多环境迁移与一键备份。

#### 6. 进程保护与启动脚本体系
- **单实例互斥保护**：引入 `Local\Server_Qcha_Bot_SingleInstance` 系统互斥体，防止误双击导致端口冲突。
- **异常退出控制台暂停**：主程序启动或运行发生未捕获异常时自动 pause 停顿，彻底杜绝黑框闪退导致无法排查错误的问题。
- **开箱即用运维脚本**：配套提供 `一键启动(守护+机器人).bat`、`启动守护进程(Daemon).bat` 与 `停止守护进程.bat`。

---

## v1.2.0 — 2026-05-21

### 新功能

#### 1. 游戏内 `.ac` 实时推送指令
玩家可在游戏中按 `~` 打开控制台，输入 `.ac <消息内容>`，消息将实时推送至指定 QQ 群。
- **主动推送架构**：游戏服务端在指令触发时，异步建立 TCP 连接将消息发往 QQ 机器人，发送完毕立即断开。全程在后台线程执行，不阻塞游戏主线程。
- **管理员同步广播**：当玩家使用 `.ac` 发送消息后，当前对局内所有拥有管理权限（`RemoteAdminAccess`）的在线管理员屏幕上会同时显示持续 10 秒的广播提示。
- **目标群聊统一配置**：推送目标 QQ 群号在机器人端配置文件中管理。

#### 2. 双向通信鉴权系统 (AuthToken)
为游戏服务端插件与 QQ 机器人之间的所有 TCP 通信增加了安全校验层。
- 双端通过设置相同的 `AuthToken` 进行身份认证，所有通信数据自动附带 Token 签名。
- 双向强制校验，防止第三方伪造控制指令或垃圾推送。
- `AuthToken` 留空时自动回退为无鉴权兼容模式。

#### 3. 后台通知监听服务
QQ 机器人端新增独立的 TCP 监听服务，专门用于接收游戏服务端的主动推送。
- 支持自定义监听 IP（`NotificationHost`）和端口（`NotificationPort`）。
- 与命令通信端口完全独立，互不干扰。

### 配置说明

#### 游戏服务端插件配置（EXILED `config.yml`）
```yaml
server_qcha:
  bot_i_p: '127.0.0.1'
  bot_port: 10088
  auth_token: 'YourSecretTokenHere'
```

#### QQ 机器人端配置（`appsettings.json`）
```json
{
  "SocketServer": {
    "Host": "127.0.0.1",
    "Ports": [ 10087 ],
    "NotificationHost": "0.0.0.0",
    "NotificationPort": 10088,
    "AuthToken": "YourSecretTokenHere",
    "ConnectTimeoutMs": 10000,
    "ReadTimeoutMs": 2000,
    "Retries": 3,
    "RetryDelayMs": 1000
  },
  "Bot": {
    "AllowedGroupIds": [],
    "NotifyGroupIds": [],
    "NotifyPrivateUserIds": [],
    "AcTargetGroupId": 123456789
  }
}
```

### 涉及文件变更

#### 游戏服务端插件（server/）
- `Main.cs`：新增 `BotIP`、`BotPort`、`AuthToken` 配置项；添加 `Instance` 单例。
- `TcpCommandServer.cs`：接入 AuthToken 鉴权逻辑。
- `AcCommand.cs`：实现 `.ac` 控制台指令。
- `BotNotificationClient.cs`：异步 TCP 推送客户端。
- `Server_Qcha.csproj`：引入新增编译文件。

#### QQ 机器人端（bot/）
- `Configuration/BotOptions.cs`：新增 `AcTargetGroupId`。
- `Configuration/SocketServerOptions.cs`：新增 `NotificationHost`、`NotificationPort`、`AuthToken`。
- `Socket/SocketCommandClient.cs`：发送命令时附带 AuthToken。
- `Socket/BotNotificationListenerService.cs`：后台 TCP 通知监听服务。
- `Program.cs`：注册 `BotNotificationListenerService`。
- `appsettings.json` / `appsettings.Example.json`：新增配置字段。

### 安全须知
部署前请务必修改默认的 `AuthToken`！默认值 `QchaSecret_123` 仅用于演示，请替换为自定义强密码，并确保游戏服端与机器人端的 Token 完全一致。

---

## v1.0.0 — 2026-05-20
- 基础 OneBot 11 / NapCat 接入与 SCPSL 游戏服 TCP 命令交互。
- 基础指令集：`/help`、`/cx`、`/info`、`/list`、`/bd`、`/me`、`/version`、`/bc`、`/round`、`/ban`、`/setadmin`。
