# 配置文件参考手册

本文档提供 **Server_Qcha (Qridge) v2.0** 所有配置项的完整说明、数据类型与推荐默认值。

---

## 目录
- [配置文件加载机制与优先级](#配置文件加载机制与优先级)
- [主程序配置 (`appsettings.json`)](#主程序配置-appsettingsjson)
  - [WebPanel（Web 控制面板）](#webpanelweb-控制面板)
  - [LocalAdmin（进程托管与独立守护配置）](#localadmin进程托管与独立守护配置)
  - [Bot（机器人模式与群白名单）](#bot机器人模式与群白名单)
  - [OfficialQq（QQ 官方机器人 OpenAPI v2）](#officialqqqq-官方机器人-openapi-v2)
  - [GoCqHttp（NapCat / OneBot 11 机器人连接）](#gocqhttpnapcat--onebot-11-机器人连接)
  - [SocketServer（TCP 命令与通知通信）](#socketservertcp-命令与通知通信)
  - [MySql（玩家数据库持久化）](#mysql玩家数据库持久化)
- [游戏服务端插件配置 (`config.yml`)](#游戏服务端插件配置-configyml)

---

## 配置文件加载机制与优先级

系统在启动时会依次加载以下配置文件，后加载的文件会覆盖先加载文件中的同名键值：
1. `appsettings.json`（程序包内置的基础默认配置）
2. `appsettings.Local.json`（推荐使用的私有配置文件，已被 Git 忽略，适合存储密码与密钥）
3. 操作系统环境变量（支持双下划线 `__` 表示分层配置，例如 `WebPanel__Port=8080`）

---

## 主程序配置 (`appsettings.json`)

### WebPanel（Web 控制面板）
定义 Web 控制台绑定的 IP 地址、监听端口、会话有效期与数据库存储路径。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `Enabled` | bool | `true` | 是否启用 Web 控制面板服务 |
| `Host` | string | `"0.0.0.0"` | 监听 IP。`"0.0.0.0"` 允许局域网与公网访问；`"127.0.0.1"` 仅允许同机访问 |
| `Port` | int | `8080` | Web 控制台访问端口 |
| `SessionMinutes` | int | `480` | 登录会话过期有效时长（分钟） |
| `DatabasePath` | string | `"data/panel.db"` | 面板内置 SQLite 数据库文件存储路径（集中存储于 `data/` 目录） |
| `DefaultAdminUsername` | string | `"admin"` | 默认超级管理员初始用户名 |
| `ResetBuiltInPasswordOnStartup` | bool | `true` | 超级管理员不存在时，启动是否自动生成随机初始密码并打印至控制台 |

---

### LocalAdmin（进程托管与独立守护配置）
用于管理 SCPSL 游戏服务端进程生命周期，支持独立常驻守护进程与嵌入模式。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `Enabled` | bool | `true` | 是否启用 LocalAdmin 进程托管功能 |
| `Mode` | string | `"Daemon"` | 运行模式：`"Daemon"` 为独立守护进程架构（推荐，防掉线）；`"Embedded"` 为进程内直接托管 |
| `DaemonUri` | string | `"http://127.0.0.1:10090"` | 独立守护进程的内部通信端点 |
| `DaemonToken` | string | `"QchaSecret_123"` | Bot 与守护进程之间的安全鉴权密钥 |
| `AutoStartDaemon` | bool | `true` | Bot 启动时若检测到守护进程未运行，是否自动在后台静默拉起守护进程 |
| `DefaultExecutablePath` | string | 参见配置 | 默认的 SCPSL.exe 服务端可执行文件路径 |
| `ConsoleBufferLines` | int | `2000` | Web 交互控制台内存环形缓冲保留的最大日志行数 |
| `LogDirectory` | string | `"localadmin-logs"` | 控制台输出日志本地归档目录 |
| `WriteLogFiles` | bool | `true` | 是否自动将控制台输出写入本地日志文件 |
| `LogExpirationDays` | int | `0` | 历史日志自动清理保留天数，`0` 表示不自动清理 |
| `Servers` | array | `[]` | 托管的游戏服务器实例列表（结构见下表） |

#### `Servers` 数组内每个游戏实例配置项：
| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `Id` | string | `"main"` | 实例唯一标识（建议由纯英文字母与数字构成） |
| `Name` | string | `"主服务器"` | 实例在 Web 控制台中显示的友好名称 |
| `ExecutablePath` | string | 路径字符串 | 本实例对应的 `SCPSL.exe` 完整绝对路径 |
| `WorkingDirectory` | string | `""` | 实例运行工作目录，留空则自动取程序所在目录 |
| `GamePort` | int | `7777` | 游戏服务监听端口 |
| `ExtraArguments` | string | `""` | 传递给 SCPSL 的附加命令行参数 |
| `AutoStart` | bool | `false` | 守护进程启动时是否自动拉起本实例 |
| `EnableHeartbeat` | bool | `true` | 是否开启心跳检测与静默崩溃检测自愈 |
| `HeartbeatSpanMaxThreshold`| int | `30` | 判定心跳超时失联的等待阈值（秒） |
| `HeartbeatRestartInSeconds`| int | `11` | 心跳判定异常后触发重启的倒计时（秒） |
| `RestartOnCrash` | bool | `true` | 进程异常崩溃退出时是否自动重新拉起 |
| `RestartLimit` | int | `4` | 重启限流：时间窗口内允许的最大连续重启次数 |
| `RestartTimeWindowSeconds` | int | `480` | 重启限流的滑动时间窗口长度（秒） |
| `GracefulStopTimeoutSeconds`| int | `30` | 关服宽限等待时间（秒），超时后执行强制终止 |
| `DisableAnsiColors` | bool | `true` | 是否移除 ANSI 终端颜色代码 |
| `RedirectStandardStreams` | bool | `true` | 是否重定向并截获标准输出与标准错误流 |

---

### Bot（机器人模式与群白名单）
机器人通用接入模式与行为策略。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `Mode` | string | `"NapCat"` | 当前运行模式：`"NapCat"`（OneBot 11）、`"Official"`（QQ官方）、`"Both"` 或 `"None"` |
| `AllowedGroupIds` | long[] | `[]` | 允许响应指令的 QQ 群白名单（适用于 NapCat 模式）。空数组表示响应所有群 |
| `NotifyGroupIds` | long[] | `[]` | 接收系统日常通知事件的目标群号列表 |
| `NotifyPrivateUserIds` | long[] | `[]` | 接收系统日常通知事件的目标私聊 QQ 号列表 |
| `AcTargetGroupId` | long | `0` | 接收游戏内 `.ac` 报警与求助信息的目标 QQ 群号 |

---

### OfficialQq（QQ 官方机器人 OpenAPI v2）
腾讯 QQ 开放平台官方机器人的连接配置。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `ApiBase` | string | `"https://api.bot.qq.com"`| 腾讯 OpenAPI 请求基地址 |
| `AppId` | string | `""` | 开放平台分配的机器人 AppID |
| `ClientSecret` | string | `""` | 开放平台分配的 AppSecret |
| `Sandbox` | bool | `false` | 是否连接沙箱测试环境 |
| `Intents` | int | `33554432` | WebSocket 事件订阅位掩码 |
| `ShardIndex` | int | `0` | 分片索引（多进程集群时使用） |
| `ShardTotal` | int | `1` | 分片总数 |
| `MaxTextLength` | int | `800` | 单条消息最大文本字符数，超出则自动智能分段 |
| `AllowActivePush` | bool | `false` | 是否允许主动下发通知（需平台开通对应权限） |
| `AdminOpenIds` | string[] | `[]` | 拥有管理员权限的用户 OpenId 白名单 |
| `AllowedGroupOpenIds` | string[] | `[]` | 允许响应指令的群组 OpenId 白名单 |
| `NotifyGroupOpenIds` | string[] | `[]` | 接收日常通知的目标群组 OpenId 列表 |
| `NotifyPrivateOpenIds` | string[] | `[]` | 接收日常通知的目标用户 OpenId 列表 |
| `AcTargetGroupOpenId` | string | `""` | 接收游戏内 `.ac` 报警的目标群组 OpenId |
| `ReconnectDelaySeconds` | int | `5` | WebSocket 断线重连初始等待时长（秒） |
| `MaxReconnectDelaySeconds`| int | `60` | WebSocket 断线重连最大退避时长（秒） |
| `RequestTimeoutSeconds` | int | `15` | OpenAPI HTTP 请求超时时间（秒） |

---

### GoCqHttp（NapCat / OneBot 11 机器人连接）
用于通过 OneBot 11 标准连接 NapCatQQ 等框架。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `WsBaseUri` | string | `"ws://127.0.0.1:6700"` | OneBot 11 正向 WebSocket 服务端地址 |
| `ReconnectDelaySeconds` | int | `5` | 断线重连初始等待时间（秒） |
| `MaxReconnectDelaySeconds`| int | `30` | 断线重连最大间隔（秒） |

---

### SocketServer（TCP 命令与通知通信）
控制主程序与各游戏服务端插件之间的 TCP 双向通信通道。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `Host` | string | `"127.0.0.1"` | 游戏服务端插件监听的主机 IP 地址 |
| `Ports` | int[] | `[ 10087 ]` | 各游戏服务端插件命令监听端口列表 |
| `NotificationHost` | string | `"0.0.0.0"` | 接收游戏内 `.ac` 推送与心跳上报的本地绑定 IP |
| `NotificationPort` | int | `10088` | 接收游戏通知的本地监听端口 |
| `AuthToken` | string | `"QchaSecret_123"` | 双向通信鉴权密钥，**务必修改且与插件端保持完全一致** |
| `ConnectTimeoutMs` | int | `10000` | 连接游戏服务端的网络超时时间（毫秒） |
| `ReadTimeoutMs` | int | `2000` | 读取游戏服务端回执的超时时间（毫秒） |
| `Retries` | int | `3` | 指令重发重试最大次数 |
| `RetryDelayMs` | int | `1000` | 两次重试之间的等待间隔（毫秒） |

---

### MySql（玩家数据库持久化）
用于玩家 Steam 账号绑定关系与游玩战绩的持久化存储（可选）。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `ConnectionString` | string | `""` | MySQL 数据库连接字符串（例如 `Server=127.0.0.1;Database=scpsl;User ID=root;Password=pass;`） |

---

## 游戏服务端插件配置 (`config.yml`)

插件首次加载时自动生成于对应框架的配置目录下（EXILED 或 LabAPI）：

| 键名 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `is_enabled` | bool | `true` | 是否启用本插件 |
| `tcp_port` | int | `10087` | 本服 TCP 命令监听端口 |
| `ip` | string | `"127.0.0.1"` | 本服 TCP 命令监听绑定 IP |
| `server_name` | string | `"1服"` | 本服在系统中的展示名称 |
| `bot_ip` | string | `"127.0.0.1"` | 主程序所在的 IP 地址（分布式时填写主控机 IP） |
| `bot_port` | int | `10088` | 主程序的 `NotificationPort` 监听端口 |
| `auth_token` | string | `"QchaSecret_123"` | 通信鉴权密钥，**必须与主程序 AuthToken 保持一致** |
| `debug` | bool | `false` | 是否在游戏服务端控制台输出调试日志 |

---

Copyright © 2025 hmyhserver.top Lab. All rights reserved.
