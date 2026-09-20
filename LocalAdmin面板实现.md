# Web 面板复刻 LocalAdmin 实现说明

> 目标：把官方 `LocalAdmin.exe` 的能力搬进机器人自带的 Web 管理面板 ——
> 在浏览器里**启动 / 停止 / 重启**游戏服务端进程、**实时看控制台**、**下发控制台命令**，
> 并具备**静默崩溃检测**与**崩溃自动重启**。
>
> 协议依据见同目录 [《官方 LocalAdmin 通讯协议》](./官方LocalAdmin通讯协议.md)
> （源码 + 二进制双向印证）。本实现严格按该协议落地。

---

## 0. 结论速览

| 项 | 结果 |
|---|---|
| 新增能力 | 机器人进程扮演 LocalAdmin 角色，托管 SCPSL 服务端进程 |
| 默认状态 | `appsettings.json` 中为 **`Enabled = true`**（`appsettings.Example.json` 模板保持 `false`） |
| 关闭时的表现 | 面板**不会静默消失**：`server.control` 持有者仍能看到「服务器进程」菜单，进入后显示未启用提示与开启方法 |
| 端到端验证 | **47/47 断言通过**（`bot/tests/LocalAdmin.SmokeTest/`，用假游戏进程实跑） |
| 兼容性 | 不启动官方 LocalAdmin 也能跑；两者**不能同时**托管同一个游戏实例 |
| 硬性前提 | 游戏服务端与机器人进程必须**同机**（控制台通道只绑回环，官方设计如此） |

---

## 1. 角色与拓扑

```
┌────────────────────┐   HTTPS/HTTP    ┌──────────────────────────────┐
│   浏览器 · 面板     │ ──────────────▶ │  机器人进程 (Server_Qcha.Bot) │
│  「服务器进程」页    │ ◀────────────── │                              │
└────────────────────┘   轮询 + 命令    │  ① 托管进程生命周期 (+1 LocalAdmin 角色) │
                                      │  ② TcpListener 127.0.0.1:<随机>         │
                                      │  ③ 业务指令网关（原有，插件 TCP）        │
                                      └───────────────┬──────────────┘
                                                      │ Process.Start（含 -console / -id / -heartbeat）
                                                      ▼
                                      ┌──────────────────────────────┐
                                      │      SCPSL.exe (游戏服务端)    │
                                      │  ServerOutput.TcpConsole     │
                                      │  主动 TCP 连回 127.0.0.1:<端口> │
                                      └──────────────────────────────┘
```

关键点：**连接方向反直觉** —— 机器人是 TCP **监听方**，游戏进程才是主动连接方。
端口由机器人随机分配后通过命令行 `-console{port}` 塞给游戏，游戏启动后连回来。

### 与既有插件通道的关系

这是**两条互不替代的独立通道**：

| | 插件 TCP 通道（原有） | LocalAdmin 控制台通道（本次新增） |
|---|---|---|
| 端口 | 10087（命令）/ 10088（通知） | 127.0.0.1 随机 |
| 拓扑 | 插件主动外连机器人 | 机器人监听，游戏主动连 |
| 鉴权 | `AuthToken` 双向 | **无**（仅回环 + 面板鉴权） |
| 承载 | 结构化业务指令 `bc&` / `kick&` / `list` | **任意控制台文本** + 进程生命周期 |
| 面板入口 | 「服务器」页 | 「服务器进程」页 |

面板上的广播/踢人/回合控制仍然走插件通道（`IServerCommandGateway` 未改动）；
服务器进程的启停与「控制台命令」走新通道。**刻意没有把两者合并**——控制台命令的
响应是纯文本，要靠解析控制台输出来判断结果，用于业务指令会非常脆弱（协议文档 §9 已论证）。

---

## 2. 功能对照（官方 LocalAdmin → 本实现）

| 官方能力 | 本实现 | 说明 |
|---|---|---|
| 拉起游戏进程 | ✅ | 参数拼接与官方逐字一致，见 §3.1 |
| 实时控制台渲染 | ✅ | 面板终端，16 色映射，增量轮询 |
| 控制台命令下发 | ✅ | 下行帧 4 字节小端长度 + UTF-8 |
| 内置命令 `hbctrl` | ✅ | `enable` / `disable` / `status`，语义一致 |
| 内置命令 `hbc` | ✅ | 中止重启倒计时（回到「等待首个心跳」） |
| 内置命令 `restart` | ✅ | 面板「重启」按钮等价 |
| 内置命令 `forcerestart` | ✅ | 面板「强制重启」按钮等价 |
| 内置命令 `exit` | ✅ | 且同样会先抑制退出信号，避免误判崩溃 |
| 未命中命令原样透传 | ✅ | 等价于在服务器窗口敲字 |
| 静默崩溃检测（心跳） | ✅ | 单字节 `0x17` + 阈值 + 倒计时 |
| 崩溃自动重启 | ✅ | 由 `0x13 ExitActionReset` 触发 |
| 重启限流 | ✅ | 时间窗口内次数上限，防崩溃循环 |
| 退出动作协商 | ✅ | `0x13` / `0x14` / `0x15` / `0x16` 全覆盖 |
| 优雅关服 / 静默关服 | ✅ | 面板「优雅停止」/ 强制结束 |
| 日志落盘 + 过期清理 | ✅ | 清理默认关闭（见 §6.7） |
| 空闲态跟踪 | ✅ | `0x11` / `0x12` 记录到控制台 |
| 回合重启标记 | ✅ | `0x10` 记录到控制台 |
| `--` 透传额外参数 | ✅ | 配置项 `ExtraArguments` |
| TUI 界面 | ❌ → 改为 Web | 本实现的目标形态 |
| `pluginmanager` / `resave` / `lacfg` / `license` | ❌ | 纯 TUI/LA 自身功能，无对应形态；`lacfg` 由配置文件替代 |

---

## 3. 协议实现要点

### 3.1 启动参数拼接

与官方 `RunScpsl()` 逐字一致：

```
-batchmode -nographics -txbuffer {SlToLaBufferSize} -rxbuffer {LaToSlBufferSize}
-port{GamePort} -console{ConsolePort} -id{机器人PID} [-disableAnsiColors] [-heartbeat] {ExtraArguments}
```

⚠️ **分隔方式不统一**：`-txbuffer` / `-rxbuffer` 后面**有空格**，而 `-port` / `-console` / `-id` 是**紧贴式**。
（此处为本文作者在协议研究阶段的一处笔误，已在协议文档中勘误，实现按源码原样拼接。）

`-console{port}` 是整条链路的开关；`-heartbeat` 只在 `EnableHeartbeat` 为真时附加；
`-disableAnsiColors` 在 Windows 上默认附加（控制台颜色由协议的颜色码承载，不需要游戏端再输出 ANSI 转义）。

### 3.2 上行帧（游戏 → 机器人）

读 1 字节 `code` 后分两种情况：

```
code < 0x10（输出行）:  [code 1B][length int32 小端 4B][UTF-8 文本 length B]
code >= 0x10（控制）  :  [code 1B]                                    仅此一字节
```

`code` 即 `System.ConsoleColor`（0–15），决定这一行的颜色；控制码从 `0x10` 起。

| 码 | 名称 | 本实现行为 |
|---|---|---|
| `0x10` | RoundRestart | 控制台提示「回合重启」 |
| `0x11` / `0x12` | IdleEnter / IdleExit | 控制台提示空闲态变化 |
| `0x13` | ExitActionReset | 记为 **Crash** → 按 `RestartOnCrash` 决定是否拉起 |
| `0x14` | ExitActionShutdown | 记为 Shutdown → 不重启 |
| `0x15` | ExitActionSilentShutdown | 记为 SilentShutdown → 不重启 |
| `0x16` | ExitActionRestart | 记为 Restart → 重启（**不计入**限流） |
| `0x17` | Heartbeat | 刷新心跳时间戳 |

### 3.3 下行帧（机器人 → 游戏）

```
[length int32 小端 4B][UTF-8 文本 length B]      ← 无类型码
```

发送前校验 `length + 4 > LaToSlBufferSize` 则**拒绝发送并返回错误**（官方同款行为）。
UTF-8 编码使用 `new UTF8Encoding(false, true)` 严格模式（与官方一致，遇到非法字节直接报错而非静默丢包）。

### 3.4 控制台缓冲与增量读取

每个实例维护一个环形缓冲（默认 2000 行），每行带**单调递增序号**。
面板以 `?after=<已收到的最大序号>&limit=N` 增量拉取，因此轮询不会重复传输历史输出。

响应中同时返回 `firstSeq` / `lastSeq` / `truncated`；当客户端落后于缓冲窗口时
`truncated=true`，面板会提示「更早的输出已被丢弃」。

每行的颜色在**服务端**就换算成 `colorHex` 下发，前端只做渲染，不重复实现映射逻辑。

---

## 4. 心跳与静默崩溃检测

心跳的用途是检出**进程还活着、但游戏主循环已经卡死**的情况 —— 这种场景下
`Process.HasExited` 永远是 `false`，只有游戏主动上报心跳才能发现。

状态机（与官方一致）：

```
Disabled ──(启动时 EnableHeartbeat=true / hbctrl enable)──▶ AwaitingFirstHeartbeat
                                                                  │
                                                          收到 0x17 │
                                                                  ▼
                                                               Active
                                                                  │
                              超过 HeartbeatSpanMaxThreshold 秒无心跳 │
                                                                  ▼
                                                        每 1 秒推进一级倒计时
                                                                  │
                                       倒计时满 HeartbeatRestartInSeconds 秒 │
                                                                  ▼
                                                    强制结束进程并按「崩溃」重启
```

- **等待首个心跳超 80 秒**时提示「静默崩溃检测未生效」（与官方同样的耐心期）。
- 倒计时期间只要收到一个心跳，立即清零并提示「心跳已恢复，重启流程已中止」。
- `hbc` 可手动中止：状态回到 `AwaitingFirstHeartbeat`，下次收到心跳才恢复监护。
- `hbctrl enable/disable` 只切换**监护状态**，不改变启动参数；若启动时没带 `-heartbeat`，
  指令会被拒绝并提示需要改配置重启（与官方行为一致）。

---

## 5. 退出动作协商与重启限流

这是整条协议里最精妙的设计，也是本实现完整保留的部分。

**问题**：游戏进程没了，机器人怎么知道该「就此收工」还是「立刻拉起一个新的」？

**解法**：游戏在退出**之前**先发一个控制码声明意图；机器人据此决策：

| 来源 | 判定 | 是否重启 | 是否计入限流 |
|---|---|---|---|
| 游戏发 `0x13` Reset | 崩溃 | 由 `RestartOnCrash` 决定 | ✅ 计入 |
| 游戏发 `0x16` Restart | 重启 | 总是重启 | ❌ 不计入（游戏自己要求的） |
| 游戏发 `0x14` / `0x15` | 正常/静默关服 | 不重启 | — |
| 面板点「重启」 | 主动 | 重启 | ❌ 不计入 |
| 面板点「停止」 | 主动 | **不重启** | — |
| 心跳超时触发 | 静默崩溃 | 重启 | ✅ 计入 |
| 用户输入 `exit` / `quit` / `stop` | 主动关服 | **不重启** | — |

配套的两个保护：

1. **主动操作时置位「忽略退出信号」** —— 面板发的停止/重启会先屏蔽游戏随后的退出声明，
   否则「我让你停的」会被自己的崩溃判定再拉起来。用户在控制台敲 `exit` 时同样处理
   （官方原版行为：先置 `DisableExitActionSignals` 再透传）。
2. **重启限流** —— 默认 `RestartLimit=4` 次 / `RestartTimeWindowSeconds=480` 秒窗口。
   超限后置位 `restartBudgetExhausted`，**停止自动拉起**并在面板顶出红色告警，
   避免崩溃循环把机器打满。手动点「启动」不受限流约束。
   （`RestartLimit=0` 表示完全禁用自动重启，只告警不动作。）

---

## 6. 配置

`appsettings.json` 的 `LocalAdmin` 段（环境变量同样可用，如 `LocalAdmin__Enabled=true`）。

### 6.1 全局

| 键 | 默认 | 说明 |
|---|---|---|
| `Enabled` | 本仓库 `appsettings.json` 为 `true`；`appsettings.Example.json` 模板为 `false` | 总开关。关闭时不绑端口、不起进程，面板对应页面显示未启用提示 |
| `DefaultExecutablePath` | 本仓库已填真实路径；模板为空 | 「添加服务器」时预填/优先推荐的 `SCPSL.exe` 路径。留空则只靠自动探测 |
| `ConsoleBufferLines` | `2000` | 单实例控制台环形缓冲行数 |
| `LogDirectory` | `localadmin-logs` | 日志落盘目录（相对 ContentRoot 或绝对路径） |
| `WriteLogFiles` | `true` | 是否写日志文件 |
| `LogExpirationDays` | `0` | 日志保留天数，**0 = 不清理**（默认安全） |

### 6.2 单实例（`Servers[]`）

| 键 | 默认 | 说明 |
|---|---|---|
| `Id` | 由 `Name` 推导 | API 路径里的实例标识 |
| `Name` | `本地服-N` | 展示名 |
| `ExecutablePath` | — | `SCPSL.exe` 完整路径 |
| `WorkingDirectory` | 可执行文件所在目录 | 工作目录 |
| `GamePort` | `7777` | 游戏服务端口 |
| `ExtraArguments` | 空 | 透传参数，支持双引号包裹含空格的值 |
| `AutoStart` | `false` | 机器人启动时是否自动拉起 |
| `EnableHeartbeat` | `true` | 是否附加 `-heartbeat`（改了要重启进程才生效） |
| `HeartbeatSpanMaxThreshold` | `30` | 心跳间隔超过该秒数视为异常 |
| `HeartbeatRestartInSeconds` | `11` | 异常后的重启倒计时秒数 |
| `RestartOnCrash` | `true` | 崩溃后是否自动重启 |
| `RestartLimit` | `4` | 时间窗口内最大重启次数（0 = 禁用自动重启） |
| `RestartTimeWindowSeconds` | `480` | 限流窗口秒数 |
| `GracefulStopTimeoutSeconds` | `30` | 优雅停止等待秒数，超时强制结束 |
| `LaToSlBufferSize` | `25000` | 机器人→游戏缓冲区，映射 `-rxbuffer` |
| `SlToLaBufferSize` | `200000` | 游戏→机器人缓冲区，映射 `-txbuffer` |
| `DisableAnsiColors` | `true` | 附加 `-disableAnsiColors` |
| `RedirectStandardStreams` | `true` | 把游戏 stdout/stderr 也汇入面板控制台 |
| `ConsoleLevel` | `all` | 「服务器进程」页控制台的**捕获级别**：`all` / `normal` / `warn` / `error` / `off`（见 §7.3） |

---

## 7. 面板

菜单新增 **「服务器进程」**（只要账号有 `server.control` 就可见）。

> **为什么不再用 `capabilities.localAdminGateway` 控制菜单可见性**：早期写法在网关未启用时
> 会让菜单**静默消失**，使用者完全不知道有这个功能、也不知道为什么没有。
> 现在改为「菜单常驻，未启用时页面内给出黄色提示条 + 开启方法」，把「配置没开」变成可自解释的状态。

- **未启用状态**：`capabilities.localAdmin` 为 false 时，页面顶部显示提示条
  （说明需在 `appsettings.json` 设 `"LocalAdmin": { "Enabled": true }` 并重启，
  并复述安全前提），同时**不启动轮询**，避免每秒打无效请求。

- **新增/修改/删除**：工具栏的「添加服务器 / 编辑 / 删除」+ 配置弹窗（见 §7.1）。
- **控制台显示级别**：工具栏的下拉（见 §7.3），运行中也能切换。
- **状态看板**：运行状态、PID、运行时长、游戏端口、控制台端口、控制台连接、静默崩溃检测、
  最近心跳、窗口内重启次数 `n / limit`。
- **控制按钮**：启动 · 停止（优雅/强制）· 重启 · 强制重启 · 中止重启倒计时 · 静默崩溃检测开关。
- **终端**：深色等宽、16 色映射、时间戳、来源标记（命令/面板/输出/错误）、自动滚动、清空。
- **命令输入框**：回车发送；下拉提示内置命令。
- **增量轮询**：1 秒一次 `/poll?after=<seq>`，同时取回状态与新增输出（一次请求拿两样，省往返）。
- **配置来源提示**：工具栏下方显示当前用的是**配置文件**还是 **appsettings 种子**（见 §7.2）。

### 7.1 在面板里增删改服务器（不必手改配置文件）

工具栏「添加服务器」打开配置弹窗，字段与 `LocalServerDefinition` 一一对应：

| 分组 | 字段 |
|---|---|
| 基本 | 名称、标识（Id）、可执行文件路径、工作目录、游戏端口、额外参数 |
| 行为 | 机器人启动时自动拉起、崩溃后自动重启、静默崩溃检测（心跳）、时间窗口内重启次数上限 |
| 高级（折叠） | 心跳超时阈值、异常后重启倒计时、限流窗口、优雅停止等待、双向缓冲、ANSIColor、stdout 重定向 |

**校验规则**（后端 `LocalAdminManager.Save/Remove`，全部返回 400 + 中文原因）：

1. 名称不能为空（留空时后端会自动命名为 `本地服-N`）；
2. 可执行文件路径不能为空；
3. 游戏端口必须是 1–65535；
4. **Id 不可重复**；
5. **游戏端口不可与其它实例重复**（两个进程无法绑同一端口）；
6. **正在运行的实例不可修改 / 删除**，必须先停止；
7. Id **不可通过编辑改名**（它是接口路径的一部分，改了会让前端选中项与接口失效）；
8. 可执行文件当前不存在 → **不拦截**，保存成功但返回 `warning`，前端以黄条提示。

### 7.1.1 可执行文件路径的自动查找

新增服务器时不必手抄路径 —— 打开弹窗会自动调 `GET /api/local/executables` 预填，
旁边也有「自动查找」按钮可随时重扫；探测到多个路径时会出现下拉框供切换。

探测顺序（`LocalAdmin/ScpslLocator.cs`，**只读，不改任何文件**）：

| 优先级 | 来源 | 说明 |
|---|---|---|
| 1 | `LocalAdmin:DefaultExecutablePath` | 配置里显式指定的默认地址 |
| 2 | **本机正在运行的 SCPSL 进程** | 最可信 —— 就是本机在用的那一份 |
| 3 | Steam 各库目录 | `<库>\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL.exe` |

Steam 库的发现方式刻意**不读注册表**：本项目 NuGet 源不可达，引入
`Microsoft.Win32.Registry` 会有还原风险。改为从
`%ProgramFiles(x86)%\Steam`、`%ProgramFiles%\Steam`、以及各固定磁盘的
`\Steam`、`\SteamLibrary`、`\Program Files (x86)\Steam`、`\Games\Steam` 出发，
再解析各自的 `steamapps\libraryfolders.vdf`（正则取 `"path"`，并还原 VDF 的 `\\` 转义）补全其它库。

本机实测：识别出 6 个 Steam 库，正确定位到
`C:\Program Files (x86)\Steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL.exe`。

结果为空时前端会提示手动填写，并告知已扫描的 Steam 库数量，不会静默失败。

### 7.2 配置存到哪：种子 vs 配置文件（重要）

| 状态 | 事实来源 |
|---|---|
| `localadmin-servers.json` **不存在** | 用 `appsettings.json` 的 `LocalAdmin:Servers` 作**种子**（只读） |
| 在面板里保存过任意一次 | 生成 `localadmin-servers.json`，此后**它就是唯一事实来源**，appsettings 里的 Servers 不再被读取 |

- 文件位于 **ContentRoot** 下（本项目即 `bot/src/Server_Qcha.Bot/localadmin-servers.json`），
  与 `appsettings.json` **同为 PascalCase**，可直接互相拷贝。
- 写入是**原子**的（先写 `.tmp` 再替换）；若文件损坏，会先备份成 `.bad-<时间戳>` 再回退到种子，
  避免面板一保存就把用户数据覆盖掉。
- 面板会明确显示当前来源与文件路径 —— 这正是为了消除「我改了 appsettings 怎么没生效」的困惑。

### 7.3 控制台显示级别（服务器进程页）

工具栏「控制台显示」下拉，运行中也能切换，**只影响之后的新输出**（已被过滤的行不会恢复）。

| 级别 | 采集哪些行 |
|---|---|
| `all` 全部（含 stdout） | 所有输出 —— **默认，与本功能引入前行为一致** |
| `normal` 常规（不含 stdout） | 排除游戏 stdout（这类输出通常是控制台协议通道的重复，最吵） |
| `warn` 警告与错误 | 黄色/红色行 |
| `error` 仅错误 | 红色行与 stderr |
| `off` 关闭 | 不采集任何输出 |

判定规则（`LocalAdmin/ConsoleCaptureLevel.cs`）——**统一按「类型 + 游戏给的色码」**，没有特例：

| 输入 | 严重度 |
|---|---|
| `Stderr` | 错误 |
| `Stdout` | 调试（最容易被 `normal` 排除掉） |
| 其余按颜色 | 红 `12`/`9` → 错误；黄 `14`/`6` → 警告；其它 → 信息 |

面板自身的提示（`Info`/`Warn`/`Error`）也走同一套颜色，因此规则是自洽的：
选「仅错误」时看到的就是红色行与 stderr，包括面板自己报的错。

**这是服务端过滤（采集时丢弃），不是前端隐藏** —— 好处是有限的控制台缓冲容量能留给真正有用的行；
代价是被过滤的历史不会因为调宽级别而回来（调宽后只影响新输出）。

级别持久化在服务器定义的 `ConsoleLevel` 字段里，重启后仍生效；
但**切换动作本身不重建实例**（重建会 `Kill` 掉运行中的进程），只改运行时开关 + 回写配置文件。

> **对新增代码的硬约束**：增删改**只动被改的那一台**实例，绝不整体重建。
> 因为 `LocalServerInstance.DisposeAsync()` 会 `Kill(entireProcessTree: true)` ——
> 整体重建会把**其它正在运行的服务器一并杀掉**。这条有专门的回归断言守着（见 §12）。

### 控制台来源标记

| 标记 | 含义 |
|---|---|
| （无） | 控制台协议通道的正常输出（带游戏指定的颜色） |
| `[命令]` | 本端下发的命令回显 |
| `[面板]` | 机器人侧的事件/提示（连接、心跳、重启决策等） |
| `[输出]` / `[错误]` | 游戏进程的 stdout / stderr |

---

### 7.4 与官方 LocalAdmin 自带指令的对应关系

官方 LocalAdmin 的本地命令集取自其源码 `Commands/` 目录（共 9 个命令 + 插件管理器 6 个子命令），
逐个对照如下。面板用「按钮/开关」等价实现，语义保持一致。

| 官方指令 | 语义 | 面板对应 | 状态 |
|---|---|---|---|
| `hbctrl enable/disable/status` | 心跳开关与状态 | 状态看板 +「静默崩溃检测」开关 | ✅ |
| `hbc` | 取消重启倒计时 | 「中止重启倒计时」按钮 | ✅ |
| `restart` | 置 `ExitAction=Restart` 后发 `exit` | 「重启」按钮 | ✅ |
| `forcerestart` | 直接杀进程并重启 | 「强制重启」按钮 | ✅ |
| `exit` | 透传给游戏 | 「停止 → 优雅停止」 | ✅ |
| `lacfg` | 打印当前配置与配置文件路径 | 「查看配置」 | ✅ |
| `resave` | 把内存中的配置重写回配置文件 | 「重写配置文件」 | ✅ |
| `help` | 列出所有可用命令 | 「内置命令说明」 | ✅ |
| `license` | 打印许可信息 | 「许可与致谢」 | ✅ |
| `pluginmanager`（`p`）`list`/`check`/`install`/`update`/`maintenance`/`token` | 从 GitHub 仓库（按别名映射）下载并管理游戏插件 | **未实现** | ❌ |

官方 `help` 输出的**「LocalAdmin Commands」一节只有 10 条**（实机核对，非源码推断）：

```
EXIT  RESTART  FORCERESTART  HBC  HBCTRL  HELP  LACFG  RESAVE  LICENSE  P
```

其中 **9 条已实现**，仅 `P`（Plugin Manager）未实现。

> **别把 `help` 的第二节当成 LocalAdmin 的能力。** 实机输出里 `help` 会打印**两块**：
> `---- LocalAdmin Commands ----`（上面 10 条，属 LocalAdmin）
> 和 `---- Game Commands ----`（几十条，属**游戏与插件**：`roundrestart`/`players`/`srvcfg`/
> `reload`/`labapi`/`forcestart`…，以及 LabAPI 的 `pluginmanager`、EXILED 的 `customroles`、
> 还有服主自己插件注册的命令如 `hmnbq`/`HMSMEOW`/`ac` 等）。
>
> **游戏侧命令不需要我们"实现"** —— 面板的命令框会把未命中本地命令的输入**原样透传**给游戏控制台，
> 换句话说那一整节**本来就是可用的**。而且它随已装插件变化，硬编码进面板必然过时。
> 所以面板「内置命令说明」里提供了「在游戏控制台执行 help」按钮：
> 让游戏自己打印当前真实清单，输出落在控制台终端里。

> **`P`（Plugin Manager）为什么没做**：它是**给游戏侧装插件**的（拉 GitHub release 资产、写进游戏插件目录、
> 记录版本、清理孤儿文件、保存 GitHub PAT），不是 LocalAdmin 自身的控制能力 —— 与本模块
> 「托管进程」的职责正交。要做的话是一个独立功能（GitHub API + 下载解压 + 版本比对 + 令牌存储），
> 工程量与整个 LocalAdmin 复刻相当。而且它的插件源来自 `PluginAliases` 配置
> （默认是 `ChaosInsurgency/pluginA-D` 这类占位值），需要先配好才有意义。
> 若确有必要，建议单独开一轮实现，并明确：目标插件框架（本机是 **LabAPI**，插件目录
> `%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<端口>\`）、GitHub PAT 的存放与保护方式。
>
> 注意别把两个 `pluginmanager` 弄混：LocalAdmin 侧的 `P`（未实现）与**游戏侧** LabAPI 的
> `pluginmanager`（启用/禁用/查看插件，属 Game Commands，**透传即可用**）是两个不同东西。

### 7.5 「查看配置」/「重写配置文件」的细节

- **查看配置（lacfg）** → `GET /api/local/config`：返回生效中的全局配置
  （缓冲行数、日志目录、是否落盘、保留天数、默认可执行文件）、
  各路径（服务器定义文件、内容根目录）、配置来源、以及托管服务器一览。
- **重写配置文件（resave）** → `POST /api/local/config/resave`：把内存中的定义**规范化后**
  写回 `localadmin-servers.json`，因此也能用来「清洗」被手改乱的配置。
  审计写入 `localadmin.config-resave`。
  ⚠️ 若该文件原本不存在，这一步会**创建它**，此后配置来源即从 appsettings 种子切换为该文件 ——
  面板上做了二次确认提示，避免误触。

---

## 8. 安全边界（务必读完）

**这条通道等价于把服务器控制台搬到浏览器** —— 能执行任意服务端控制台命令。因此：

1. **权限**：全部接口要求 `server.control`。该权限原本就是预留位，现已启用其语义
   （预设「管理员」「所有者」已包含；「运营」「只读」不含）。
2. **审计**：所有副作用操作都会写审计日志 —— `localadmin.start` / `stop` / `restart` /
   `console` / `console-clear` / `heartbeat` / `cancel-restart`，控制台命令的**原文**也会入库。
3. **只绑回环**：控制台监听严格限定 `IPAddress.Loopback`，端口随机，沿用官方设计。
   这也是「必须同机」的原因。
4. **未做命令白名单**（有意）：官方 LocalAdmin 的核心价值就是任意命令透传，
   加白名单会让 `pluginmanager`、自定义插件命令等全部失效。风险控制放在权限与审计上。
5. **面板自身的暴露面**：如果面板被直接暴露到公网，那么拿到面板账号就等于拿到服务器控制台。
   建议反向代理 + HTTPS + 强密码（交接文档已把「面板明文 HTTP 且无防爆破」列为既有技术债）。

---

## 9. 代码结构

```
bot/src/Server_Qcha.Bot/
├── Configuration/
│   └── LocalAdminOptions.cs          配置模型（全局 + 单实例，含参数夹取）
├── LocalAdmin/
│   ├── ConsoleProtocol.cs            控制码枚举 / 帧编解码 / 颜色映射 / 长度上限
│   ├── ConsoleCaptureLevel.cs        控制台捕获级别：色码→严重度判定与过滤规则
│   ├── LocalServerInstance.cs        单实例核心：监听 / 读帧 / 进程控制 / 心跳 / 重启策略
│   ├── LocalServerStore.cs           服务器定义的持久化（localadmin-servers.json，原子写 + 损坏备份）
│   ├── ScpslLocator.cs               自动探测本机 SCPSL.exe（运行中进程 / Steam 各库，只读）
│   └── LocalAdminManager.cs          IHostedService：构建实例、自动启动、运行时增删改、日志清理
└── Web/
    ├── LocalAdminEndpoints.cs        /api/local/* 端点（权限 + 审计）
    └── PanelEndpoints.GetMeta        capabilities 置位
```

运行时数据文件（均在 ContentRoot 下，不进版本库）：

| 文件 | 说明 |
|---|---|
| `localadmin-servers.json` | 面板增删改后的服务器定义（首次保存时生成，此后为唯一事实来源） |
| `localadmin-servers.json.bad-*` | 解析失败时的自动备份（不会覆盖你的数据） |
| `localadmin-logs/<实例Id>/yyyy-MM-dd.log` | 控制台日志落盘（`WriteLogFiles` 开启时） |

---

## 10. 部署

1. **确认没有别的进程在托管同一个游戏实例**
   （官方 `LocalAdmin.exe`、手动开的服务端窗口都要先关掉）。
2. 配 `LocalAdmin:Enabled = true`（本仓库 `appsettings.json` 已默认置 `true`；
   从 `appsettings.Example.json` 复制配置时需要手动改），并把 `Servers[0].ExecutablePath` 指向
   `<SCPSL Dedicated Server>\SCPSL.exe`。可按需设 `AutoStart`。
   > 改完记得让**实际运行目录**（`bin/<Config>/net8.0/appsettings.json`）也是新值 ——
   > 直接跑 `dotnet run` 会自动拷贝，跑现成 exe 则要手动同步或重新构建。
3. 重启机器人，浏览器打开面板 → 左侧「服务器进程」。
4. 给需要的账号勾上 `server.control`（预设「管理员」「所有者」已含）。
5. 点「启动」。

> 机器人进程**不要**用管理员权限运行没有必要时——它只是 `Process.Start` 一个同用户进程。

---

## 11. 与原版的三处刻意偏离

| # | 官方做法 | 本实现 | 原因 |
|---|---|---|---|
| 1 | `DataAvailable` 轮询（10ms 忙等）+ `while (Available < length) await Delay(20)` 满包等待 | `ReadExactlyAsync` 真异步读 | 官方满包等待**没有超时**：对端发了长度头却没把正文发全时会无限空转。忙等也白耗 CPU |
| 2 | 长度字段不校验，直接 `ArrayPool.Rent(length)` | 上限 4 MB，超限断开并告警 | 损坏/恶意的长度会导致大内存分配 |
| 3 | 接收侧 UTF-8 用严格模式，一个坏字节会打断整条连接 | 接收侧宽松（替换为 U+FFFD），**发送侧仍严格** | 面板场景下优先保证链路不中断；坏字节只污染单行并留痕 |

另外两处纯观感调整：

- **颜色映射**：`Black(0)` / `DarkBlue(1)` 在 Web 深色终端上几乎不可见，做了提亮；
  其余 14 色语义与官方一致。映射表在 `ConsoleProtocol.ColorToHex`。
- **日志清理**：官方默认保留 90 天并自动删除；本实现默认 `LogExpirationDays=0`（不清理），
  需要时显式开启，避免默认行为里含破坏性动作。

还有一处行为差异：官方 `TcpServer` 只在 `Start()` 里 `BeginAcceptTcpClient` **一次**；
本实现是**持续 accept 循环**，因此游戏重启后新进程能自动重连（已验证）。

---

## 12. 验证情况

`bot/tests/LocalAdmin.SmokeTest/` 提供一个可复跑的一键冒烟测试：用复刻官方协议的
**假游戏进程**冒充 `SCPSL.exe`，实跑整条链路。

```bash
bash bot/tests/LocalAdmin.SmokeTest/run-smoke.sh
```

**结果：47 / 47 断言通过**（机器人口令与面板 8099 端口隔离运行，结束后自动清理进程）。

覆盖：能力探针、四档颜色码映射、多行拆行、`0x10` 控制码、stdout 重定向、
下行帧往返、心跳 `awaiting→active`、`hbctrl` 三个子命令、
崩溃自动重启且计入限流、面板主动重启不计入、`exit` 不误判、
限流置位后停止拉起、优雅停止、五类审计记录。

### 12.1 服务器增删改（§7.1 / §7.2）的验证

隔离实例 + 假游戏进程实跑，**45 / 45 断言通过**，覆盖：

- 初始来源为 `appsettings` 种子且**尚未生成**配置文件；
- 新增成功 → 配置文件被创建、来源切为 `config-file`、新项追加在末尾；
- 落盘为 **PascalCase**（可与 `appsettings.json` 互相拷贝）、自动补全 `WorkingDirectory`；
- 校验拦截：重复端口 / 重复 Id / 空路径 / 非法端口 / 修改或删除不存在的 → 均 400 且中文原因；
- 空名称自动命名为 `本地服-N`；路径不存在 → 保存成功但带 `warning`；
- 修改：改名与布尔项生效、**顺序不变**、**Id 不可篡改**；
- 删除：成功、回到 1 台、已落盘；删除不存在的 → 400；
- 审计含 `localadmin.server-add` / `-update` / `-delete`；
- **运行中的实例不可改、不可删**（400）；
- **★ 关键回归：修改 A 时，正在运行的 B 依旧存活**（守住「不得整体重建」这条约束）；
- 停止之后恢复可改可删。

### 12.2 可执行文件自动查找（§7.1.1）的验证

隔离实例实跑，**7 / 7 断言通过**：真实路径被探测到、`recommended` 与之相同、
Steam 库根目录 ≥4 个（本机实测 6 个）、候选均去重且文件真实存在、
VDF 的 `\\` 转义被正确还原（路径中无双反斜杠）。

### 12.3 控制台显示级别（§7.3）的验证

用假游戏实跑（它收到任何命令都会回一条**青色** `[ECHO]`，正好是信息级），**21 / 21 断言通过**：

- 默认 `all` 下 `[ECHO]` 与 `[STDOUT]` 都能看到；
- 切 `error` / `warn` / `off` 后 `[ECHO]` **不再出现**（`off` 下新增行为 0）；
- 切 `normal` 后 `[ECHO]` 恢复、而 `[STDOUT]` 被排除 —— 精确验证了「常规不含 stdout」；
- 非法级别被拒并给出可选值；
- 级别已落盘到 `localadmin-servers.json`、状态实时反映、审计含 `localadmin.console-level`。

### 12.4 lacfg / resave（§7.4、§7.5）的验证

隔离实例实跑，**26 / 26 断言通过**：

- `GET /config` 正确返回能力状态、内容根目录、服务器定义文件路径、来源、服务器数量，
  以及全部全局配置项（缓冲行数、日志保留天数、默认可执行文件等）；
- **重写前文件不存在** → `POST /config/resave` 成功且**文件被创建**，落盘为 PascalCase 且含 `ConsoleLevel`；
- 重写后 `source` 与 `/local/servers` 的来源**同步切换为 `config-file`**；
- 重复重写幂等，服务器数不变；
- 审计含 `localadmin.config-resave`，且 `target` 为配置文件完整路径。

前端另做两项静态校验：内联脚本 `node --check` 通过、Vue 模板可被页面自带的
`vue.global.prod.js` 编译（并用故意写坏的模板做反向验证，确认检查不是空过）。

---

## 13. 未验证项与已知限制

1. **未对真实 `SCPSL.exe` 做过运行时启动验证。** 参数拼接、可执行文件路径、
   控制台端口回连这些都已按源码与协议核对，但「真实游戏是否接受这套参数并回连」
   只能在实跑一次后确认。建议先在一个测试端口验证，确认无误再切生产。
   假游戏验证的是**本实现的协议收发与状态机**，不能替代真实游戏的行为验证。
2. **stdout 重定向的编码**：游戏子进程的 stdout 若以系统 ANSI 代码页写出，
   非 ASCII 内容在面板上可能显示为乱码（机器人按 UTF-8 解码）。权威通道是
   `TcpConsole`（协议规定 UTF-8），stdout 只是补充信息。若要彻底解决，
   需改成按字节读取再嗅探编码。
3. **同机限制**：控制台通道只绑回环，跨机部署用不了 —— 这是官方协议的固有限制，不是实现缺陷。
4. **`-id`（机器人 PID）的实际用途未定位到游戏端调用点**，按官方原样传参。
5. 未做**多实例端口冲突预检**：若两个实例配了同一个 `GamePort`，会由游戏端自己报端口占用。
6. 面板终端最多保留 1500 行渲染（缓冲仍是 2000 行），极端高频输出下更早的行会被滚掉。

---

## 附：API 清单

所有接口均在 `/api/local` 下，均要求 `server.control` 权限。

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/servers` | 列出全部托管实例：`servers`（状态）+ `definitions`（可编辑定义，同序）+ `source`/`configPath` |
| GET | `/executables` | 探测本机可能的 `SCPSL.exe`（`recommended`/`candidates`/`steamRoots`/`probed`），供自动预填 |
| GET | `/config` | 对应官方 `lacfg`：当前生效配置 + 各配置文件路径 + 来源 + 服务器一览 |
| POST | `/config/resave` | 对应官方 `resave`：把内存中的配置规范化后写回配置文件（文件不存在则创建） |
| POST | `/servers` | **新增**一台托管服务器（见 §7.1） |
| GET | `/servers/{id}` | 单实例状态 |
| PUT | `/servers/{id}` | **修改**一台托管服务器（整体替换） |
| DELETE | `/servers/{id}` | **删除**一台托管服务器 |
| GET | `/servers/{id}/poll?after=&limit=` | 状态 + 增量控制台输出（面板主轮询） |
| POST | `/servers/{id}/start` | 启动进程 |
| POST | `/servers/{id}/stop` | `{force:bool}` 优雅/强制停止 |
| POST | `/servers/{id}/restart` | `{force:bool}` 重启 |
| POST | `/servers/{id}/console` | `{command:string}` 下发控制台命令（优先匹配内置命令） |
| POST | `/servers/{id}/console/clear` | 清空控制台缓冲 |
| POST | `/servers/{id}/heartbeat` | `{enabled:bool}` 切换静默崩溃检测 |
| POST | `/servers/{id}/console-level` | `{level:string}` 切换控制台捕获级别（`all`/`normal`/`warn`/`error`/`off`），运行中可改 |
| POST | `/servers/{id}/cancel-restart` | 中止重启倒计时（等价 `hbc`） |

> 沿用既有的**两种返回约定**，改动时别混：
> - **动作类**（start/stop/restart/console/heartbeat/cancel-restart）→ 一律 `200` + `{success,response,error}`，前端看 `success` 字段；
> - **资源类**（新增/修改/删除）→ 失败返回 **4xx** + `{success:false,error}`，前端走异常分支。
>   新增/修改成功时还可能带 `warning`（例如可执行文件路径当前不存在）。

`GET /api/meta` 的 `capabilities` 新增：

```json
{ "localAdminGateway": true, "serverControl": true, "localServerCount": 1 }
```
