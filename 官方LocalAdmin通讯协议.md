# 官方 LocalAdmin ↔ 游戏服务端 通讯协议

> 结论来源：**源码 + 二进制双向交叉验证**，不是猜测。
> - LocalAdmin 侧：`github.com/northwood-studios/LocalAdmin-V2`（master，本地克隆逐行阅读）
> - 游戏侧：本机 `SCPSL_Data/Managed/Assembly-CSharp.dll` 元数据转储 + 字符串表提取
> - 两侧的帧格式、类型码、命令行参数均已互相印证。

---

## 0. 速览：八个要点

1. **连接方向反直觉**：`LocalAdmin` 是 TCP **监听方**，游戏进程是 **主动连接方**（游戏侧代码就叫 `TcpClient`）。
2. **只绑回环**：`new TcpListener(IPAddress.Loopback, 0)` —— 端口由系统随机分配，**不对外监听**。
3. **端口通过命令行传递**：LocalAdmin 把随机端口用 `-console{port}` 塞进游戏进程的启动参数。
4. **无任何鉴权**：没有 token、没有密码、没有握手。安全性完全依赖「只绑 127.0.0.1 + 端口只出现在本机进程命令行」。
5. **双向帧格式不同**：上行（游戏→LA）带 1 字节类型/颜色码；下行（LA→游戏）**纯长度前缀**。
6. **控制码 0x10–0x17** 共 8 个，**0x00–0x0F 被颜色占用**（正好是 `System.ConsoleColor` 的 16 个值）。
7. **心跳是单字节 0x17**，用途是**静默崩溃检测**（进程活着但游戏主循环卡死）。
8. **退出动作协商**：游戏在退出前先发一个控制码，告诉 LocalAdmin「我这是崩溃 / 正常关服 / 要重启」，LocalAdmin 据此决定是否拉起新进程。

---

## 1. 角色与拓扑

```
┌─────────────────────────┐                      ┌──────────────────────────────┐
│      LocalAdmin.exe     │                      │   SCPSL.exe (游戏服务端)      │
│                         │                      │                              │
│  TcpListener            │  ◀─── TCP 连接 ────  │  TcpClient                   │
│  127.0.0.1:<随机端口>    │      （游戏主动连）  │  连 127.0.0.1:<ConsolePort>  │
│                         │                      │                              │
│  · 实时控制台渲染        │  ◀── 日志行 ───────  │  ServerOutput.TcpConsole     │
│  · 心跳超时检测          │  ◀── 控制码 0x10-17 ─│    ↑                         │
│  · 崩溃自动重启          │  ─── 命令文本 ──────▶│  ServerConsole.EnterCommand  │
│  · 日志清理              │                      │                              │
└─────────────────────────┘                      └──────────────────────────────┘
         子进程关系：LocalAdmin 用 Process.Start 拉起 SCPSL.exe
```

**关键点**：这条链路是**控制台/进程生命周期**通道，**不是** Remote Admin 协议。它传的是「服务器控制台输入输出」，等价于你在服务器窗口里敲的字和看到的行。

---

## 2. 握手与启动

### 2.1 完整启动命令行

LocalAdmin 拉起游戏时拼出的参数串（`Core/LocalAdmin.cs` → `RunScpsl()`）：

```
-batchmode -nographics
-txbuffer {SlToLaBufferSize}     ← 注意：这两个是「参数名 + 空格 + 数值」
-rxbuffer {LaToSlBufferSize}     ←
-port{GamePort}                  ← 这两个是紧贴式，中间无空格
-console{ConsolePort}            ←
-id{LocalAdminPID}
[-heartbeat]           # 仅当 LA 配置 enable_heartbeat: true
[-disableAnsiColors]   # 当 LA 禁用 TrueColor 时（Windows 上默认禁用）
{_gameArguments}       # 用户在 -- 之后透传的参数
```

> **分隔方式不统一，别按「全都紧贴」来写。** 对照官方 `RunScpsl()` 的拼接语句：
> ```csharp
> $"-batchmode -nographics -txbuffer {Config.SlToLaBufferSize} -rxbuffer {Config.LaToSlBufferSize} " +
> $"-port{GamePort} -console{Server!.ConsolePort} -id{Environment.ProcessId}{extraArgs} {_gameArguments}"
> ```
> 即 `-txbuffer` / `-rxbuffer` **带空格**，`-port` / `-console` / `-id` **紧贴**。
> `extraArgs` 形如 `" -disableAnsiColors"` / `" -heartbeat"`（前置空格拼接）。
>
> 复刻实现时按官方原样最稳妥（本项目 `server-labapi` 之外的面板 LocalAdmin 实现即照此拼接）。
>
> 勘误说明：本文早期版本写成「全部紧贴式」，与源码不符，已更正。

### 2.2 游戏端识别的参数全表

从 `Assembly-CSharp.dll` 字符串表提取（这些字面量连续排布，属于同一个参数解析器）：

| 参数 | 说明 |
|---|---|
| `-port` | **必需**，游戏服务端口 |
| `-console` | 控制台 TCP 端口（即本协议） |
| `-id` | LocalAdmin 的进程 PID |
| `-txbuffer` | 游戏→LA 方向的缓冲区字节数 |
| `-rxbuffer` | LA→游戏 方向的缓冲区字节数 |
| `-heartbeat` | 启用心跳上报 |
| `-keepsession` | 保留会话 |
| `-appdatapath` | 覆盖 AppData 路径 |
| `-configpath` | 覆盖配置路径 |
| `-disableconfigvalidation` | 跳过配置校验 |
| `-stdout` | 输出到标准输出 |
| `-key` | （密钥类参数） |

### 2.3 启动校验

游戏在没有拿到 `-port` 时会拒绝以专用服务器身份启动，原文：

```
"-port" argument is required for dedicated server. Aborting startup.
Make sure you are using latest version of LocalAdmin.
```

说明游戏端**硬校验 LocalAdmin 的存在**，并且提示「确保 LocalAdmin 是最新版」——这是官方的版本兼容策略（靠提示而非协议协商）。

### 2.4 游戏端实现类

```
ServerOutput.IServerOutput              ← 输出后端接口
├── ServerOutput.StandardOutput         ← 控制台标准输出
├── ServerOutput.FileConsole            ← 写日志文件
├── ServerOutput.NonDedicatedOutput     ← 非专用服
└── ServerOutput.TcpConsole             ← 本协议
        .ctor(ushort port, int receiveBufferSize, int sendBufferSize)
        field SpecifiedReceiveBufferSize / SpecifiedSendBufferSize
        field const DefaultReceiveBufferSize / DefaultSendBufferSize

ServerOutput.IOutputEntry               ← 一条待发送的输出
├── TextOutputEntry      { string Text; byte Color; const int Offset; }
├── RoundRestartedEntry
├── IdleEnterEntry / IdleExitEntry
├── ExitActionResetEntry / ExitActionShutdownEntry
├── ExitActionSilentShutdownEntry / ExitActionRestartEntry
└── HeartbeatEntry
```

`TextOutputEntry` 的字段正好对应帧格式里的「颜色码 + 长度 + 文本」，**三方印证**。

---

## 3. 帧格式（两个方向不对称）

### 3.1 上行：游戏 → LocalAdmin

读 1 字节 `code`，然后**分两种情况**：

**情况 A：`code < 0x10`（控制台输出行）**

```
┌──────┬───────────────┬──────────────────────┐
│ code │ length (int32)│ payload (UTF-8 文本)  │
│ 1 B  │   4 B，小端    │      length 字节      │
└──────┴───────────────┴──────────────────────┘
```
- `code` 即 `System.ConsoleColor` 值（0–15），决定这一行的颜色。
- LocalAdmin 收到后渲染成 `{code:X}{text}`，再拆出首字符当颜色码。

**情况 B：`code >= 0x10`（控制消息，无负载）**

```
┌──────────┐
│   code   │   ← 就这一个字节，后面什么都不跟
│   1 B    │
└──────────┘
```

### 3.2 下行：LocalAdmin → 游戏

**没有类型码**，纯长度前缀：

```
┌───────────────┬──────────────────────┐
│ length (int32)│ payload (UTF-8 文本)  │
│   4 B，小端    │      length 字节      │
└───────────────┴──────────────────────┘
```

游戏侧会校验长度是否与声明一致，不符即报：

```
[TcpConsole] Received data length is NOT {0}! Received data amount: {1}.
[TcpClient] Receive exception: 
```

> 编码统一 UTF-8，且 LocalAdmin 侧用的是 `new UTF8Encoding(false, true)` —— **遇到非法字节直接抛异常**（`throwOnInvalidBytes: true`）。

### 3.3 控制码全表

| 码 | 名称 | 语义 |
|---|---|---|
| `0x00`–`0x0F` | *（保留）* | 输出行颜色，非控制码 |
| `0x10` | `RoundRestart` | 回合重启（LA 借此重置日志） |
| `0x11` | `IdleEnter` | 进入空闲态 |
| `0x12` | `IdleExit` | 退出空闲态 |
| `0x13` | `ExitActionReset` | 退出动作重置 → 视为**崩溃** |
| `0x14` | `ExitActionShutdown` | 正常关服 |
| `0x15` | `ExitActionSilentShutdown` | 静默关服（不等按键） |
| `0x16` | `ExitActionRestart` | 请求**重启** |
| `0x17` | `Heartbeat` | 心跳 |

---

## 4. 命令通道语义

LocalAdmin 收到键盘输入后（`SetupReader()`）的处理顺序：

1. **先查 LocalAdmin 自有命令表**（大小写不敏感）：

| 命令 | 是否转发给游戏 | 作用 |
|---|---|---|
| `hbctrl enable/disable/status` | ✗ | 心跳开关与状态 |
| `hbc` | ✗ | 取消重启倒计时 |
| `restart` | ✓（发 `exit`） | 设 `ExitAction=Restart` 后向游戏发 `exit` |
| `forcerestart` | ✗ | 直接杀进程并重启 |
| `help` / `license` / `resave` / `lacfg` / `pluginmanager` | ✗ | 本地功能 |
| `exit` | ✓ | 透传（`SendToGame=true`） |

2. **未命中命令 → 原样透传给游戏**，等价于在服务器控制台敲这条命令。
3. **特殊拦截**：输入 `exit` / `quit` / `stop` 时，LA 先置
   `DisableExitActionSignals=true; ExitAction=SilentShutdown`，再透传 —— 避免把用户主动关服误判成崩溃。

> 值得注意的是：透传的命令文本会被游戏当作 **`ServerConsole` 控制台输入**执行。这意味着**谁连上这个端口，谁就等于拿到了服务器控制台**。安全性完全靠回环绑定。

---

## 5. 心跳与静默崩溃检测

- 启用条件：LA 配置 `enable_heartbeat: true` → 启动加 `-heartbeat` → 游戏周期性发 `0x17`。
- LA 侧状态机：

```
Disabled ──(enable)──▶ AwaitingFirstHeartbeat ──(收到 0x17)──▶ Active
                                │                                 │
                        80 秒内没等到首个心跳                 超过阈值 → 倒计时
                        提示「静默崩溃检测未生效」            超时 → 重启服务器
```

- 相关配置（`config_localadmin.txt`）：

| 键 | 默认值 | 含义 |
|---|---|---|
| `enable_heartbeat` | `true` | 是否启用 |
| `heartbeat_span_max_threshold` | `30` | 心跳间隔超过此秒数即视为异常 |
| `heartbeat_restart_in_seconds` | `11` | 异常后倒计时多少秒执行重启 |
| `restart_on_crash` | `true` | 崩溃后自动重启 |

- 倒计时期间可敲 `hbc` 中止；`hbctrl status` 查看状态。

---

## 6. 退出动作协商（本协议最精妙的设计）

问题：游戏进程退出了，LocalAdmin 怎么知道该「就此收工」还是「立刻拉起一个新的」？

解法：**游戏在退出前，先通过控制码把意图告诉 LocalAdmin**。

| 游戏发出的码 | LA 设置的 ExitAction | 进程 Exited 后 LA 的行为 |
|---|---|---|
| `0x13` Reset | `Crash` | 报「游戏进程已终止」；若 `restart_on_crash` 则重启 |
| `0x14` Shutdown | `Shutdown` | 自己一并退出 |
| `0x15` SilentShutdown | `SilentShutdown` | 静默退出，不等按键 |
| `0x16` Restart | `Restart` | 重启游戏进程 |

配套机制：
- `DisableExitActionSignals` 为真时忽略游戏发来的退出信号（用于 LA 主动操作场景）。
- 重启次数限制：默认 **480 秒窗口内最多 4 次**（`--restartsLimit` / `--restartsTimeWindow`），防止崩溃循环打爆机器；LA 自己发起的重启（`_ignoreNextRestart`）不计数。

---

## 7. 缓冲区与流控

| 配置键 | 默认 | 下限 | 映射参数 |
|---|---|---|---|
| `la_to_sl_buffer_size` | `25000` | `101` | `-rxbuffer`（游戏收） |
| `sl_to_la_buffer_size` | `200000` | `351` | `-txbuffer`（游戏发） |

- 两侧都按这个值设置 `TcpClient` 的 `ReceiveBufferSize` / `SendBufferSize`，并 `NoDelay = true`。
- 发送前检查 `length + 4 > txBuffer`，超限则**拒绝发送并报错**：
  `Failed to send command - configured LA to SL buffer size is too small.`
- **没有流控、没有序列号、没有重传**。纯字节流，TCP 保证有序。

### 已知的脆弱点（源码层面）

1. **轮询式读取**：`await Task.Delay(10)` + `DataAvailable` 轮询，而非基于 `await ReadAsync` 的阻塞等待 —— 有 10ms 级延迟和忙等开销。
2. **满包等待可能死锁**：
   ```
   while (_client.Available < length) await Task.Delay(20);
   ```
   如果对端发了长度头却没能把 body 发全（且连接未断），这里会**无限期空转**，没有超时。
3. **长度字段未做上限校验**，直接 `ArrayPool.Rent(length)` —— 恶意/损坏长度会导致大内存分配。

---

## 8. 与本项目自研协议（10087/10088）的对照

| 维度 | 官方 LocalAdmin 链路 | 本项目自研链路 |
|---|---|---|
| 拓扑 | LA 监听，**游戏主动连** | 插件主动外连 Bot |
| 绑定 | 仅 127.0.0.1 | 可跨机（依赖 AuthToken） |
| 鉴权 | **无** | 每包 `AuthToken`，失败回 `Unauthorized` |
| 定界 | **4 字节小端长度前缀** | 半关闭 `Shutdown(Send)` |
| 通道 | 单条连接双向复用 | 双通道（命令 10087 / 通知 10088） |
| 消息类型 | 1 字节类型码 + 可选负载 | JSON 信封 `type` 字段 |
| 心跳 | 单字节 `0x17`，30s 阈值 | JSON `heartbeat`，30s + 指数退避 |
| 离线感知 | LA 侧超时重启 | Bot 侧 90s 无心跳移除条目 |
| 承载内容 | 控制台输入输出 + 进程生命周期 | 业务指令（踢人、广播、回合控制） |
| 安全性来源 | 网络隔离（回环） | 密码学鉴权（Token） |

### 三个可以直接借鉴的点

1. **长度前缀定界是对的**。官方在 2020 年代的实现里就是「1 字节码 + 4 字节长度 + 负载」，比半关闭可靠得多 —— 这独立印证了交接文档里「建议改 4 字节长度前缀」的结论。
2. **混合帧值得抄**：低频但需要结构化的消息用 JSON，高频低延迟的用单字节控制码。官方用 1 个字节表达 8 种状态，零解析成本。
3. **退出动作协商机制**值得移植：在游戏关闭/插件卸载前，先发一个「这是正常关闭还是异常」的标记，能显著减少上层的误判告警。

### 一个不要抄的地方

官方这条链路**零鉴权**，因为它绑死在回环地址上。本项目的 Bot 可能跨机部署（`103.205.254.65` 的 MySQL、独立的 Bot 进程），**必须保留 AuthToken**，不能因为「官方都没鉴权」就简化掉。

---

## 9. 能不能直接复用这条链路？

**不建议，但值得知道边界。**

- 它能做的：把**任意文本**当作服务器控制台命令执行 —— 理论上可以发 `bc 内容`、`kick 1 reason` 等。
- 它的限制：
  1. 必须有 LocalAdmin 在场（游戏硬校验 `-port` 并提供 `-console`）；
  2. 只绑回环，**跨机不可用**；
  3. 无鉴权、无消息类型区分，响应解析要解析控制台文本输出（脆弱）；
  4. 长度上限 25000 字节，且无分帧协议；
  5. 官方随时可能改（这属于内部实现，非公开 API）。

**结论**：本项目自研的双通道 + AuthToken 协议在**跨机部署**和**结构化语义**上明显优于官方链路；官方链路的价值在于它是「同机场景的零配置方案」和「协议设计参考」。

---

## 10. 未验证 / 待确认

- `-id`（LocalAdmin PID）在游戏端的具体用途未定位到调用点，推测用于进程存活校验或父进程识别。
- `-keepsession` / `-key` 的语义未展开（与本协议无关，属于游戏其他功能）。
- 游戏侧 `TcpConsole` 的 `DefaultReceiveBufferSize` / `DefaultSendBufferSize` 常量值未能从元数据读出（只存在于 IL body，需反编译）。
- 尚未实测：在真实服务器上抓一次 `-console` 端口的原始字节流做协议确认（本地可抓 LocalAdmin 启动瞬间的 loopback 流量）。
- Remote Admin（游戏内 M 键面板）是**另一套独立的 TCP 协议**，带密码鉴权与独立端口，本次未研究。如需可比对，可另开一轮。

---

## 附：关键源码位置索引

| 主题 | 文件 |
|---|---|
| TCP 服务端与帧读写 | `Core/TcpServer.cs` |
| 启动参数、心跳监控、退出动作 | `Core/LocalAdmin.cs` |
| 配置项定义与序列化 | `IO/Config.cs` |
| 本地命令基类 / 注册表 | `Commands/Meta/CommandBase.cs`、`CommandService.cs` |
| 心跳命令 | `Commands/HeartbeatControlCommand.cs`、`HeartbeatCancelCommand.cs` |
| 重启命令 | `Commands/RestartCommand.cs`、`ForceRestartCommand.cs` |
| 游戏侧实现 | `Assembly-CSharp.dll` → `ServerOutput.TcpConsole`、`ServerOutput.OutputCodes`、`ServerOutput.TextOutputEntry` |
