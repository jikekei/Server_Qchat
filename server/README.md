# Server_Qcha (EXILED Plugin)

这是 **SCPSL (SCP: Secret Laboratory) Dedicated Server** 的 EXILED 插件端，用来在服务器上开启一个 **TCP 命令端口**，接收外部程序（例如本仓库的 `bot/` QQ 机器人）发送的文本命令，并在服务器内执行对应操作，然后把执行结果返回给调用方。

## 你能用它做什么

- 查询服务器信息/在线人数（供 QQ 群里查询）
- 广播消息到服务器
- 重启回合
- 封禁玩家（按玩家 `Id`）
- 查询玩家列表

## 安装

1. 确保你的 SCPSL Dedicated Server 已安装 EXILED。
2. 编译插件（仓库根目录执行）：

```powershell
dotnet build server/Server_Qcha.csproj -c Release
```

3. 找到编译产物：

`server/bin/Release/Server_Qcha.dll`

4. 把 `Server_Qcha.dll` 放到服务器的 EXILED 插件目录（示例）：

- Windows：`...\\EXILED\\Plugins\\`
- Linux：`~/.config/EXILED/Plugins/`

5. 重启服务器，让插件加载并生成配置文件。

## 配置项说明

插件配置类在 `server/Main.cs` 里，常用字段：

- `TcpPort`：监听的 TCP 端口（默认 `10087`）
- `IP`：监听 IP（默认 `127.0.0.1`，同机最安全）
- `ServerName`：显示用服务器名（例如 `1服`、`测试服`）
- `ContentText`：当 `DisplayMode=1` 时附加显示的文本
- `DisplayMode`：
  - `0`：返回结果里附带查询时间
  - `1`：返回结果里附带 `ContentText`
  - `2`：只返回基础信息
- `Debug`：为 `true` 时部分错误会返回更详细信息

建议：

- 如果 `bot/` 和服务器在同一台机器上，保持 `IP=127.0.0.1`。
- 如果要跨机器调用，改成 `0.0.0.0` 或服务器内网 IP，并在防火墙只放行可信来源。

## TCP 调用协议（非常重要）

1. **一次连接只发一个命令**（UTF-8 纯文本）
2. 服务端会返回一段 UTF-8 文本作为结果，然后关闭连接

### 支持的命令

无参数命令：

- `cx`：返回在线人数、在线管理数量（以及可选附加文本/时间）
- `info`：返回更详细的服务器信息（DD/博士/SCP/回合时间等）
- `list`：返回当前玩家列表（`Nickname-Id`）
- `start`：启动回合
- `rest`：重启回合（保护：回合开始超过 60 秒会拒绝）
- `allrest`：重启服务器进程
- `ac`：返回一段预留的“求助/告警”文本（默认返回 `null`，主要给扩展用）

带参数命令（用 `&` 分隔）：

- `bc&<text>`：广播 15 秒 `[管理员消息]<text>`
- `ychhe&<text>`：广播 17 秒 `[该信息为自动发送]<text>`，并锁大厅
- `kick&<id>&<reason>&<time>`：封禁玩家
  - `<id>`：玩家 `Id`（`list` 返回的那个）
  - `<reason>`：原因（允许包含空格；如果你自己拼接时包含 `&`，会被当成分隔符）
  - `<time>`：封禁时长（整数，单位取决于服务器/EXILED 的 Ban 实现）

返回值：都是一段可读文本。调用方（机器人）通常把它原样发回 QQ 群/私聊。

## 手动调用示例（不依赖 QQ 机器人）

### PowerShell 发送 `cx`

```powershell
$client = [System.Net.Sockets.TcpClient]::new("127.0.0.1", 10087)
$stream = $client.GetStream()
$bytes  = [System.Text.Encoding]::UTF8.GetBytes("cx")
$stream.Write($bytes, 0, $bytes.Length)

$buf = New-Object byte[] 4096
$n   = $stream.Read($buf, 0, $buf.Length)
[System.Text.Encoding]::UTF8.GetString($buf, 0, $n)

$stream.Dispose()
$client.Dispose()
```

### PowerShell 广播

```powershell
$cmd = "bc&服务器将于5分钟后重启"
```

把上面的 `"cx"` 替换成 `$cmd` 即可。

## 与 bot/ 的关系

- `bot/`（QQ 机器人）负责接收群消息指令（OneBot 11 / NapCatQQ 正向 WS）
- `server/`（本插件）负责在 SCPSL 服务器内执行命令

如果你只想在内网做自动化，也可以不用 `bot/`，直接用你自己的程序按上述 TCP 协议调用。
