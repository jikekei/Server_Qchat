# 配置文件参考手册

本文档提供 **Server_Qcha (Qridge)** 所有配置项的完整说明、数据类型与推荐默认值。

---

## 目录
- [配置文件加载优先级](#配置文件加载优先级)
- [主程序配置 (`appsettings.json`)](#主程序配置-appsettingsjson)
  - [WebPanel（Web 控制面板）](#webpanelweb-控制面板)
  - [LocalAdmin（LocalAdmin 托管引擎）](#localadminlocaladmin-托管引擎)
  - [SocketServer（TCP 命令与通知通信）](#socketservertcp-命令与通知通信)
  - [GoCqHttp（OneBot 11 机器人连接）](#gocqhttponebot-11-机器人连接)
  - [Bot（群白名单与推送设置）](#bot群白名单与推送设置)
  - [MySql（数据库连接）](#mysql数据库连接)
- [游戏服插件配置 (`config.yml`)](#游戏服插件配置-configyml)

---

## 配置文件加载优先级

系统启动时按以下顺序加载配置，后加载的配置会覆盖先加载的同名项：
1. `appsettings.json`（默认基础配置）
2. `appsettings.Local.json`（本地私有配置，推荐在此修改，已被 Git 忽略）
3. 操作系统环境变量（支持使用 `__` 作为层级分隔符，例如 `WebPanel__Port=8080`）

---

## 主程序配置 (`appsettings.json`)

### WebPanel（Web 控制面板）
控制面板服务绑定的 IP、端口与安全策略。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `Enabled` | bool | `true` | 是否启用 Web 控制面板服务 |
| `Host` | string | `"0.0.0.0"` | 监听 IP。`"0.0.0.0"` 允许局域网与公网访问；`"127.0.0.1"` 仅允许本机访问 |
| `Port` | int | `8080` | Web 面板访问端口 |
| `SessionMinutes` | int | `480` | 登录会话过期时间（分钟） |
| `DatabasePath` | string | `"panel.db"` | 面板内置 SQLite 数据库文件存储路径 |
| `DefaultAdminUsername` | string | `"admin"` | 默认超级管理员用户名 |
| `ResetBuiltInPasswordOnStartup` | bool | `true` | 若超级管理员账号不存在，启动时是否自动生成随机初始密码并打印在控制台 |

---

### LocalAdmin（LocalAdmin 托管引擎）
用于取代官方 LocalAdmin，控制本地 SCPSL 服务端进程的生命周期与心跳守护。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `Enabled` | bool | `true` | 是否启用 LocalAdmin 进程托管引擎 |
| `ConsoleBufferLines` | int | `2000` | Web 交互控制台内存环形缓冲保留的最大日志行数 |
| `LogDirectory` | string | `"localadmin-logs"` | 本地控制台日志保存目录 |
| `WriteLogFiles` | bool | `true` | 是否自动将控制台输出写入本地日志文件 |
| `LogExpirationDays` | int | `0` | 日志保留天数，`0` 代表不自动清理 |
| `Servers` | array | `[]` | 托管的游戏服务器实例列表（见下表） |

#### `Servers` 数组内各实例项参数：
| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `Id` | string | `"main"` | 实例唯一标识（小写英文字母与数字） |
| `Name` | string | `"1号主服"` | 实例在面板中展示的友好名称 |
| `ExecutablePath` | string | `""` | `SCPSL.exe` 的绝对路径 |
| `WorkingDirectory` | string | `""` | 工作目录，留空则自动取可执行程序所在目录 |
| `GamePort` | int | `7777` | 游戏服务监听端口 |
| `AutoStart` | bool | `false` | 主程序启动时是否自动拉起该游戏实例 |
| `EnableHeartbeat` | bool | `true` | 是否开启心跳检测与静默崩溃自愈 |
| `RestartOnCrash` | bool | `true` | 进程异常退出时是否自动重新拉起 |
| `RestartLimit` | int | `4` | 重启限流：时间窗口内允许的最大连续重启次数 |
| `RestartTimeWindowSeconds`| int | `480` | 重启限流的滑动时间窗口长度（秒） |
| `GracefulStopTimeoutSeconds`| int | `30` | 关服超时时间（秒），超时仍未退出则强制终止 |

---

### SocketServer（TCP 命令与通知通信）
控制主程序与各游戏服务端插件之间的 TCP 双向通信通道。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `Host` | string | `"127.0.0.1"` | 游戏服务端插件监听的 IP 地址 |
| `Ports` | int[] | `[ 10087 ]` | 游戏服务端插件监听端口列表（多服各占用一个独立端口） |
| `NotificationHost` | string | `"0.0.0.0"` | 接收游戏内 `.ac` 推送与心跳上报的本地监听绑定 IP |
| `NotificationPort` | int | `10088` | 接收通知的本地监听端口 |
| `AuthToken` | string | `"QchaSecret_123"`| 双向通信鉴权 Token，**务必修改且与插件保持一致** |
| `ConnectTimeoutMs` | int | `10000` | 连接游戏服务端的超时时间（毫秒） |
| `ReadTimeoutMs` | int | `2000` | 读取游戏服务端响应的超时时间（毫秒） |
| `Retries` | int | `3` | 指令失败时的重试次数 |

---

### GoCqHttp（OneBot 11 机器人连接）
用于连接 QQ 协议端（如 NapCatQQ、LLOneBot）。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `WsBaseUri` | string | `"ws://127.0.0.1:6700"` | OneBot 11 正向 WebSocket 地址 |
| `ReconnectDelaySeconds` | int | `5` | 断线重连初始等待时间（秒） |
| `MaxReconnectDelaySeconds`| int | `30` | 断线重连最大间隔（秒） |

---

### Bot（群白名单与推送设置）

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `AllowedGroupIds` | long[] | `[]` | 允许响应指令的 QQ 群白名单。留空表示响应全部群 |
| `AcTargetGroupId` | long | `0` | 接收游戏内 `.ac` 报警与求助消息的目标 QQ 群号 |

---

### MySql（数据库连接）
用于玩家绑定数据与统计信息的持久化存储（可选）。

| 字段 | 类型 | 默认值 | 描述 |
|---|---|---|---|
| `ConnectionString` | string | `""` | MySQL 连接字符串，例如 `Server=127.0.0.1;Database=scpsl;User ID=root;Password=pass;` |

---

## 游戏服插件配置 (`config.yml`)

插件首次加载自动生成在对应插件目录中：

| 键名 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `is_enabled` | bool | `true` | 是否启用本插件 |
| `tcp_port` | int | `10087` | 本服 TCP 命令监听端口 |
| `ip` | string | `"127.0.0.1"` | 本服 TCP 命令监听 IP |
| `server_name` | string | `"1服"` | 本服显示名称 |
| `bot_ip` | string | `"127.0.0.1"` | 主程序所在的 IP 地址 |
| `bot_port` | int | `10088` | 主程序的 `NotificationPort` 端口 |
| `auth_token` | string | `"QchaSecret_123"`| 鉴权密钥，**必须与主程序 AuthToken 一致** |
| `debug` | bool | `false` | 是否开启调试日志输出 |
