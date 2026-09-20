# Server_Qcha (Qridge)

## SCP: Secret Laboratory 现代化 Web 运维控制面板与集群管理系统

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![SCPSL](https://img.shields.io/badge/Game-SCPSL-red?logo=steam&logoColor=white)](https://scpslgame.com/)
[![EXILED](https://img.shields.io/badge/Plugin-EXILED-blue)](https://github.com/Exiled-Team/EXILED)
[![LabAPI](https://img.shields.io/badge/Plugin-LabAPI-darkgreen)](https://github.com/northwood-studios/LabAPI)
[![Vue 3](https://img.shields.io/badge/Frontend-Vue%203-4FC08D?logo=vue.js&logoColor=white)](https://vuejs.org/)
[![Element Plus](https://img.shields.io/badge/UI-Element%20Plus-409EFF?logo=element&logoColor=white)](https://element-plus.org/)
[![OneBot 11](https://img.shields.io/badge/Protocol-OneBot%2011-orange)](https://github.com/botuniverse/onebot-11)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## 项目概述

在传统的 SCP: Secret Laboratory (SCPSL) 服务器运营中，服主通常依赖官方提供的命令行工具 **LocalAdmin (LA)** 来运行和管理游戏服务端。然而，官方 LocalAdmin 存在诸多痛点：
- 只能在本机命令行黑框中运行，多开服务器时桌面充斥着大量终端窗口；
- 远程管理严重依赖 Windows 远程桌面或 SSH，无法直接通过网页或移动设备操作；
- 缺少多管理员分权机制，任何能接触控制台的人员均具备最高破坏性权限；
- 缺乏数据可视化与历史统计，无法直观掌握在线走势与服务器负载。

**Server_Qcha (Qridge)** 的核心定位是**深度取代官方 LocalAdmin**。系统内置了完整的 LocalAdmin 协议栈与进程守护引擎，并在此基础上构建了一套开箱即用、功能完备的现代化 Web 运维控制面板（同时提供手机专属自适应界面）。通过本系统，服主与管理团队可以在任意设备上通过浏览器全面接管服务器的日常运维、监控与调度。

此外，系统还提供了 **QQ 机器人与群服联动** 作为附属功能，方便玩家在群内查服、绑定数据，以及在游戏内通过快捷指令向管理群实时报警求助。

---

## 核心特性：全面取代官方 LocalAdmin

本项目并非简单调用外部脚本，而是从底层实现了官方 LocalAdmin 与游戏主程序之间的原生通信协议（包含 `-console`、`-id`、`-heartbeat` 通道），具备更加稳定、强大的托管能力。

### 官方 LocalAdmin 与 Qridge Web 面板对比

| 功能维度 | 官方 LocalAdmin (LA) | Qridge Web 管理面板 |
|---|---|---|
| **管理界面** | 单机命令行黑框 (CLI) | 现代化 Web 控制台，支持 PC 端与手机端专属 UI |
| **多服管理** | 每个服占用一个独立终端窗口，极易混淆 | 单一面板集中管理多个游戏服实例，状态一览无余 |
| **远程运维** | 必须登录远程桌面 (RDP) 或 SSH | 任意浏览器输入网址即可管理，无需暴露服务器桌面 |
| **进程守护与自愈** | 基础崩溃重启 | 完备的心跳状态机、静默崩溃检测、防死循环重启限流、正常/异常退出意图识别 |
| **控制台交互** | 原始文本流输出 | 交互式 Web 控制台、环形日志缓冲区、ANSI 颜色解析与动态过滤 |
| **权限控制** | 无权限分级，全员同权 | 基于 RBAC 的细粒度权限体系，支持子管理员账号与自定义权限集 |
| **数据监控** | 无图表，仅纯文本显示 | 24 小时在线玩家趋势折线图、全服在线与今日峰值统计、资源监控 |
| **操作审计** | 无操作记录，无法追溯责任 | 全方位审计日志，记录每项关键操作的操作人、时间、来源 IP 与执行结果 |
| **社区联动** | 无扩展能力 | 内置附属 QQ 机器人，打通游戏内与群内双向交互 |

---

## Web 管理面板功能详解

面板前端基于 Vue 3 + Element Plus 构建，采用纯原生 ES Modules 架构，**零编译依赖**，无需安装 Node.js 或执行前端打包构建，随主程序即开即用。

### 1. 服务器总览 (Overview)
- **核心运营指标 (KPI)**：实时展示全服当前在线玩家数、今日在线峰值、在线服务器数量与健康状态。
- **24 小时在线趋势折线图**：后台自动周期采样真实玩家数据（自动剔除 Dedicated Server 虚报），直观反映服务器每日的人流高峰与低谷。
- **服务器状态矩阵**：以卡片和表格形式展示所有纳管服务器的运行状态、实时人数/上限进度条、通信延迟。

### 2. 服务器进程管理 (LocalAdmin 托管)
- **进程生命周期管控**：支持在网页端对本地 SCPSL 服务端实例执行一键启动、安全关服、重启与强制终止。
- **进程性能监控**：实时监控各游戏实例的进程 ID、运行时长、CPU 占用、内存消耗与控制台连接状态。
- **高级守护策略**：
  - 心跳超时检测与自愈重连；
  - 崩溃自动拉起，可配置时间窗口内的重启次数上限，防止异常死循环；
  - 自动归档与轮转 LocalAdmin 日志，支持日志过期自动清理。

### 3. 服务器交互与在线管控 (Servers)
- **Web 交互控制台**：在网页端直接向下辖服务器发送原生控制台指令（如 `roundrestart`、`bc`、`ban` 等），并实时接收返回回显。
- **在线玩家穿透查看**：点击任意服务器即可弹出该服当前在线玩家列表，显示玩家昵称、游戏内角色、延迟及 SteamID。
- **快捷管理操作**：支持从界面直接向指定服务器下发全局广播、踢出玩家或封禁玩家。

### 4. 手机专属 UI 界面 (Mobile Responsive)
- **自适应移动端布局**：针对屏幕宽度小于等于 768px 的移动设备自动激活专属样式。
- **移动端抽屉导航 (Drawer)**：隐藏桌面固定侧边栏，通过顶栏汉堡菜单按钮滑出原生 App 风格的侧滑抽屉，集成用户资料与全部功能导航。
- **触控与流式排版优化**：KPI 卡片在移动端自动切换为流式网格，折线图与搜索工具栏自动折行，所有对话框自适应为 94vw 视口宽度并支持平滑触控滚动。

### 5. 细粒度多账号权限体系 (RBAC)
- **多管理员协同**：支持创建多个面板账号，各账号会话相互独立，支持在多台设备（如电脑和手机）同时在线操作。
- **精确权限分配**：提供模块级权限控制，包括但不限于：
  - `servers.view`：查看服务器状态与在线情况
  - `server.control`：向服务器发送指令、重启与关服
  - `bot.manage`：配置与管理 QQ 机器人
  - `database.manage`：数据库管理（待完成，规划中）
  - `logging.manage`：切换系统运行时日志级别
  - `accounts.manage`：增删改管理员账号与权限
  - `audit.view`：查阅系统操作审计日志
- **高安全保障**：密码采用 PBKDF2-SHA256 加密存储（100,000 次迭代 + 独立随机 Salt）。当账号密码被修改时，系统会自动将该账号在其他所有设备上的会话强制下线。

### 6. 全方位操作审计 (Audit)
- 系统自动捕获并记录所有管理员通过面板下发的关键指令与状态变更。
- 审计记录包含：操作时间、操作人账号与显示名称、客户端真实 IP、操作类型、目标服务器或对象、执行结果与详细参数，确保所有运维操作有据可查。

### 7. 动态日志管理 (Logging)
- **动态日志级别调整**：在面板中可实时调整系统日志输出级别（Trace / Debug / Info / Warning / Error），排查线上问题无需重启服务。
- **多级别日志实时查看**：支持在界面中快速筛选过滤并查看详细运行时日志。

---

## 附属功能：QQ 机器人与群服联动

作为 Web 管理面板的辅助模块，系统内置了对 OneBot 11 协议（推荐使用 NapCatQQ）的支持，打通游戏服与玩家社群之间的实时沟通链路。

### 1. 游戏内快捷求助与报警 (.ac)
- 玩家在游戏控制台输入 `.ac <消息内容>`，即可将消息直接推送到绑定的 QQ 管理群。
- 推送内容包含玩家昵称、SteamID、所在服名与具体文本。
- 当前对局内的所有在线管理员屏幕上方会同步出现 10 秒提示广播。
- 通信全程基于 TCP 并附带 AuthToken 鉴权，杜绝伪造与刷屏。

### 2. QQ 群查询与管理指令

#### 普通玩家指令
| 指令 | 说明 | 示例 |
|---|---|---|
| `help` | 查看群内可用指令说明 | `help` |
| `cx` | 汇总查询所有服务器在线玩家与负载 | `cx` |
| `info` | 查询服务器基础配置信息 | `info` |
| `#1` | 查看 1 号服务器当前在线玩家列表（`#2`、`#3` 同理） | `#1` |
| `/bd <Steam64>` | 将发送者的 QQ 号与 Steam64 ID 绑定 | `/bd 76561198xxxxxxxx` |
| `/me` | 查询自己绑定的 Steam 游戏统计数据 | `/me` |

#### 群管理指令（需群主或管理员权限）
| 指令 | 说明 | 示例 |
|---|---|---|
| `/bc <服号> <内容>` | 向指定服务器发送全局全屏广播 | `/bc 1 5分钟后服务器将进行维护` |
| `/round <服号>` | 强制重启指定服务器当前回合 | `/round 1` |
| `/ban <服号> <ID> <时长> <原因>` | 踢出或封禁指定玩家 | `/ban 1 2 60m 违规行为` |
| `/setadmin <服号> <ID> <组名>` | 为指定玩家临时授予管理权限组 | `/setadmin 1 2 admin` |

---

## 系统架构

```mermaid
flowchart TD
    subgraph Management["运维管理端"]
        PC["PC 浏览器 (Web 面板)"]
        Mobile["手机浏览器 (移动专属 UI)"]
    end

    subgraph Community["玩家与社群"]
        Client["游戏客户端 (玩家/管理员)"]
        QQGroup["QQ 群 (玩家/管理员)"]
    end

    subgraph CoreService["Server_Qcha.Bot (.NET 8 核心服务)"]
        subgraph WebLayer["Web 服务层"]
            StaticFiles["Web 静态站点 (Vue 3 + Element Plus)"]
            Apis["Minimal APIs (鉴权 / 审计 / RBAC)"]
        end

        subgraph LocalAdminModule["LocalAdmin 托管引擎 (取代官方 LA)"]
            ProcessSupervisor["进程守护器 (生命周期 / 崩溃自愈)"]
            HeartbeatFsm["心跳状态机 & 静默崩溃检测"]
            ConsoleIo["控制台协议解包 & 环形日志缓冲"]
        end

        subgraph BotModule["QQ 机器人模块 (附属)"]
            OneBotClient["OneBot 11 WS 客户端"]
            CommandRouter["群指令路由 & 玩家数据绑定"]
            NotificationListener["TCP 通知接收服务 (接收 .ac)"]
        end

        subgraph DataLayer["数据层"]
            Database[(SQLite / MySQL)]
            Tracker["历史在线与峰值采样器"]
        end
    end

    subgraph GameInstances["SCPSL 服务端实例"]
        Server1["SCPSL Dedicated Server 1"]
        Server2["SCPSL Dedicated Server 2"]
        Plugin["Server_Qcha 插件 (EXILED / LabAPI)"]
    end

    subgraph Framework["QQ 框架"]
        NapCat["NapCatQQ / LLOneBot (OneBot 11)"]
    end

    PC <--> Apis
    Mobile <--> Apis

    ProcessSupervisor -->|拉起并监控进程| Server1
    ProcessSupervisor -->|拉起并监控进程| Server2
    ConsoleIo <-->|内部控制台通信端口| Server1
    ConsoleIo <-->|内部控制台通信端口| Server2
    HeartbeatFsm <-->|心跳检测通道| Server1

    Plugin <-->|TCP 命令与推送 (AuthToken)| Apis
    Plugin -->|推送 .ac 消息| NotificationListener
    NotificationListener --> BotModule

    OneBotClient <-->|正向 WebSocket| NapCat
    NapCat <--> QQGroup
    Client -->|游戏内控制台 .ac| Plugin

    Apis --> DataLayer
    Tracker --> DataLayer
```

---

## 快速开始

### 1. 运行环境准备
- **游戏服务端**：已部署 SCPSL Dedicated Server（支持 EXILED 8+ 或 LabAPI 1.1+）；
- **主程序环境**：安装 [.NET 8 Runtime 或 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)；
- **QQ 框架（可选）**：若需启用机器人联动，部署 [NapCatQQ](https://github.com/NapNeko/NapCatQQ) 并开启正向 WebSocket（默认端口 `6700`）。

### 2. 配置并运行主程序 (Server_Qcha.Bot)
1. 进入 `bot/src/Server_Qcha.Bot/` 目录；
2. 复制配置模板：
   ```bash
   cp appsettings.Example.json appsettings.Local.json
   ```
3. 在 `appsettings.Local.json` 中配置 Web 面板与服务参数：
   ```json
   {
     "WebPanel": {
       "Enabled": true,
       "Host": "0.0.0.0",
       "Port": 8080,
       "SessionMinutes": 480
     },
     "SocketServer": {
       "Host": "127.0.0.1",
       "Ports": [ 10087 ],
       "NotificationHost": "0.0.0.0",
       "NotificationPort": 10088,
       "AuthToken": "YourSecretToken"
     },
     "LocalAdmin": {
       "Enabled": true,
       "Servers": [
         {
           "Id": "main",
           "Name": "1号主服",
           "ExecutablePath": "C:\\SCPSL\\SCPSL.exe",
           "GamePort": 7777,
           "AutoStart": true
         }
       ]
     }
   }
   ```
4. 启动服务：
   ```bash
   dotnet run --project bot/src/Server_Qcha.Bot -c Release
   ```
   控制台启动时会输出初始随机密码，请妥善保存。

### 3. 安装游戏服务端插件（二选一）

根据所使用的插件框架将插件放入对应目录：

- **EXILED 平台**：将 `server/` 编译生成的 `Server_Qcha.dll` 放置在 `%APPDATA%\EXILED\Plugins\`。
- **LabAPI 平台**：将 `server-labapi/` 编译生成的 `Server_Qcha.dll` 放置在 `%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<端口号>\`。

启动游戏服一次以生成配置文件，确保插件配置中的 `auth_token` 与主程序中的 `SocketServer:AuthToken` 一致。

### 4. 访问 Web 管理面板
在浏览器中打开：
```text
http://<服务器IP>:8080/
```
输入默认账号 `admin` 与控制台打印的初始密码即可登录。推荐首次登录后在「账号管理」中修改密码并建立子管理员账号。

---

## 配置文件详析 (`appsettings.json`)

```json
{
  "WebPanel": {
    "Enabled": true,                         // 是否启用 Web 控制面板
    "Host": "0.0.0.0",                       // 面板监听地址 (0.0.0.0 允许远程访问)
    "Port": 8080,                            // 面板 HTTP 端口
    "SessionMinutes": 480,                   // 登录会话超时时间 (分钟)
    "DatabasePath": "panel.db"               // 面板 SQLite 数据库文件路径
  },
  "LocalAdmin": {
    "Enabled": true,                         // 是否启用 LocalAdmin 托管引擎
    "ConsoleBufferLines": 2000,              // Web 控制台回滚缓冲区行数
    "LogDirectory": "localadmin-logs",       // 控制台日志存储目录
    "WriteLogFiles": true,                   // 是否写入本地日志文件
    "Servers": [
      {
        "Id": "main",                        // 实例唯一标识
        "Name": "1号主服",                   // 实例展示名称
        "ExecutablePath": "C:\\SCPSL\\SCPSL.exe", // 游戏服务端可执行文件路径
        "GamePort": 7777,                    // 游戏服务端口
        "AutoStart": false,                  // 主程序启动时是否自动拉起该实例
        "EnableHeartbeat": true,             // 是否启用心跳监控
        "RestartOnCrash": true,              // 异常崩溃时是否自动重启
        "RestartLimit": 4,                   // 时间窗口内最大允许重启次数
        "RestartTimeWindowSeconds": 480      // 重启计数时间窗口 (秒)
      }
    ]
  },
  "SocketServer": {
    "Host": "127.0.0.1",                     // 游戏插件监听 IP
    "Ports": [ 10087 ],                      // 游戏插件监听端口
    "NotificationHost": "0.0.0.0",           // 接收 .ac 推送的本地绑定 IP
    "NotificationPort": 10088,               // 接收 .ac 推送的本地端口
    "AuthToken": "YourSecretToken"           // 双向加密鉴权 Token
  },
  "GoCqHttp": {
    "WsBaseUri": "ws://127.0.0.1:6700"       // OneBot 11 正向 WebSocket 地址
  },
  "Bot": {
    "AllowedGroupIds": [],                   // 允许响应指令的群号列表 (空代表所有群)
    "AcTargetGroupId": 0                     // 接收游戏内 .ac 推送的目标群号
  }
}
```

---

## 目录结构

```text
.
├── bot/                         # 核心主服务 (.NET 8)
│   └── src/Server_Qcha.Bot/
│       ├── LocalAdmin/          # LocalAdmin 进程托管、协议解析与守护引擎
│       ├── Web/                 # Minimal APIs、安全鉴权与审计中间件
│       ├── wwwroot/             # Web 控制面板前端源码 (Vue 3 + Element Plus)
│       │   ├── css/             # 桌面端与手机端专属自适应样式
│       │   └── js/              # ES Modules 前端视图组件与状态管理
│       ├── Bot/                 # OneBot 11 机器人协议对接与指令处理 (附属)
│       ├── Services/            # 历史数据采样、通知监听与后台守护任务
│       └── Data/                # 数据库访问与数据持久层
├── server/                      # SCPSL 游戏服务端插件 (EXILED 版本)
├── server-labapi/               # SCPSL 游戏服务端插件 (LabAPI 版本)
└── README.md                    # 本文档
```

---

## 待完成功能与开发计划 (TODO)

- [ ] **数据库深度管理功能（开发中）**：
  - Web 面板对 MySQL / SQLite 数据的可视化表结构浏览与多条件搜索；
  - 玩家绑定数据与历史封禁记录的导入、导出及批量维护；
  - 数据库连接健康诊断与自动化备份。
- [ ] 更多游戏内高级控制指令扩展（如动态角色分配、自定义物品刷取等）。
- [ ] 多语言国际化（i18n）支持（包含完整英文界面）。
- [ ] 更长时间维度的玩家留存与服务器运营图表深度分析。

---

## 常见问题与排错 (FAQ)

### Q1: 为什么完全不需要官方 LocalAdmin 了？
本项目内嵌了与 SCPSL 游戏主程序直接对接的 LocalAdmin 原生通信协议。系统通过命令行参数 `-console`、`-id`、`-heartbeat` 拉起游戏进程并建立内部 TCP 通信，实现与官方 LocalAdmin 完全一致甚至更严格的心跳守护、崩溃捕获与控制台交互。因此无需再运行任何官方 LocalAdmin 程序。

### Q2: 手机端访问 Web 面板无法打开？
1. 检查 `appsettings.Local.json` 中 `WebPanel:Host` 是否配置为 `"0.0.0.0"`，若配置为 `"127.0.0.1"` 则只能从本机访问；
2. 检查云服务器控制台的安全组以及系统防火墙是否已放行 Web 面板端口（默认 `8080`）；
3. 确保手机与服务器处于可互相连通的网络环境。

### Q3: 多个管理员同时登录面板会互相影响吗？
不会。每个登录会话均持有独立生成的访问令牌（Token）并基于内存字典隔离，支持同一账号或不同账号在多台设备（电脑/手机）同时在线操作。所有管理员的操作均会带有独立身份标签记录在审计日志中。若某个账号修改了登录密码，系统会自动将该账号在其他设备上的会话踢下线以保障安全。

### Q4: 游戏内输入 `.ac` 后 QQ 群未收到通知？
1. 检查 `Bot:AcTargetGroupId` 是否正确配置了目标 QQ 群号；
2. 检查游戏服插件配置中的 `bot_ip` 与 `bot_port` 是否指向主程序的通知接收端口（默认 `10088`）；
3. 检查双方的 `auth_token` 是否完全一致。

---

## 开源协议

本项目基于 [MIT License](LICENSE) 协议开源。
