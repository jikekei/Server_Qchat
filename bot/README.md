# Server_Qcha.Bot

一个用于 **SCP: Secret Laboratory (SCPSL)** 的 QQ 机器人与服务器联动工具。

它通过 **go-cqhttp 正向 WebSocket** 接收群消息指令，然后通过 **TCP Socket** 把指令转发给你的 SCPSL 服务器侧插件/SocketServer（例如查询在线、广播、踢人、重启回合等）。

## 功能

- `cx`：汇总查询多个服务器在线人数
- `info`：查询多个服务器信息
- `#<n>`：查询第 n 个服务器玩家列表
- `/bd <Steam64>`：绑定 QQ 与 Steam64（写入 MySQL 的 `playerdata.QQ_ID`）
- `/me`：按 QQ 号查询自己的玩家统计
- 管理指令（需要群管理员/群主）：
  - `/bc <n> <内容>`：广播
  - `/round <n>`：重启回合（发送 `rest`）
  - `/ban <n> <ID> <时间> <原因>`：踢出/封禁（发送 `kick&...`）
  - `/setadmin <n> <ID> <分组>`：设置权限（发送 `bc&...`，保持旧协议）

## 运行环境

- .NET 8 SDK
- go-cqhttp（正向 WebSocket，默认 `ws://127.0.0.1:6700`）
- 你的 SCPSL 服务器侧 SocketServer（监听 TCP 端口列表）
- 可选：MySQL（用于绑定/玩家数据查询）

## 快速开始

### 推荐 QQ 框架: NapCatQQ (OneBot 11)

推荐使用 NapCatQQ 来提供 OneBot 11 的 **正向 WebSocket 服务端**：

```text
https://github.com/NapNeko/NapCatQQ
```

在 NapCatQQ 的 OneBot 11 配置里开启 `websocketServers`，把端口设置成 `6700`（或你喜欢的端口），然后把本项目的 `GoCqHttp:WsBaseUri` 指向对应的 `ws://127.0.0.1:<port>`。 citeturn0open2

1. 复制配置文件并按需修改：
   - `src/Server_Qcha.Bot/appsettings.Example.json` -> `src/Server_Qcha.Bot/appsettings.Local.json`
2. 配置 go-cqhttp 正向 WebSocket 地址（默认 6700）：
   - `GoCqHttp:WsBaseUri`
3. 配置 SocketServer 监听端口：
   - `SocketServer:Host`
   - `SocketServer:Ports`
4. （可选）配置 MySQL 连接串：
   - `MySql:ConnectionString`
5. 启动：

```powershell
dotnet run --project src/Server_Qcha.Bot -c Release
```

## 配置说明

主要配置在 `src/Server_Qcha.Bot/appsettings.json`，推荐只在 `src/Server_Qcha.Bot/appsettings.Local.json` 放本机配置（已在 `.gitignore` 里忽略）。

- `Bot:AllowedGroupIds` 为空表示监听所有群；不为空则只处理指定群
- `Bot:NotifyGroupIds`/`Bot:NotifyPrivateUserIds` 当前未使用（预留给未来的告警/通知功能）

## Socket 协议（约定）

本项目会向 SocketServer 发送以下文本命令（UTF-8）：

- `cx`
- `info`
- `list`
- `rest`
- `bc&<text>`
- `kick&<id>&<reason>&<time>`
- `bc&<id>&<group>`（用于 `/setadmin`，保持旧协议）

SocketServer 需要返回可读文本作为机器人回复。

## 安全

- 不要把任何 token/密码写进源码或提交到 GitHub
- 本项目默认从环境变量读取配置（例如 `MySql__ConnectionString`）

## 许可证

默认使用 MIT 许可证，见 `LICENSE`。
