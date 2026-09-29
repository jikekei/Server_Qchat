<div align="center">

<img src="../../../assets/readme/doc-guides.svg" width="100%" alt="Server_Qcha 文档：使用与运维指南。">

</div>

[文档中心](../../../docs/README.zh-CN.md) · [项目主页](../../../docs/zh-CN/README.md) · [English](../../en/guides/plugin-guide.md)

# 游戏服务端插件指南 (EXILED & LabAPI)

本项目同时为 **EXILED** 与 **LabAPI** 两大主流 SCPSL 插件框架提供了原生插件支持，通信协议与配置规范完全统一。

---

## 目录
- [插件平台与选型](#插件平台与选型)
- [EXILED 插件安装与配置](#exiled-插件安装与配置)
  - [安装步骤](#安装步骤)
  - [配置说明](#配置说明)
- [LabAPI 插件安装与配置](#labapi-插件安装与配置)
  - [安装步骤-1](#安装步骤-1)
  - [配置说明-1](#配置说明-1)
- [核心机制与通信协议](#核心机制与通信协议)
  - [双向通信架构](#双向通信架构)
  - [AuthToken 安全鉴权](#authtoken-安全鉴权)
  - [游戏内 .ac 一键求助与广播](#游戏内-ac-一键求助与广播)
  - [Dedicated Server 自动过滤](#dedicated-server-自动过滤)
  - [游戏管理员权限支持（QGA1）](#游戏管理员权限支持qga1)

---

## 插件平台与选型

根据您的服务器当前使用的插件框架，选择对应的插件构建产物（二选一即可）：

| 框架 | 最低兼容版本 | 产物文件名 | 部署路径 |
|---|---|---|---|
| **EXILED** | 仓库依赖 EXILED 9.5.0 | `Server_Qcha-EXILED.dll` | `%APPDATA%\EXILED\Plugins\` |
| **LabAPI** | LabAPI 1.1.7+ | `Server_Qcha-LabAPI.dll` | `%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<端口>\` |

> 注意：EXILED 与 LabAPI 互不兼容，同一个服务器端口切勿同时加载两款插件。
> EXILED 插件当前按仓库中的 `ExMod.Exiled` 9.5.0 依赖构建；其他 EXILED 版本（包括 8.x）尚未在本仓库验证，不承诺兼容。构建所需的 `PluginAPI.dll` 是 EXILED 依赖包中的引用，不表示本插件面向已归档的独立 NwPluginAPI 框架。

---

## EXILED 插件安装与配置

### 安装步骤
1. 从 Releases 下载 `Server_Qcha-EXILED.dll`，重命名为 `Server_Qcha.dll`；
2. 将文件移动到 EXILED 插件目录：
   - **Windows**：`%APPDATA%\EXILED\Plugins\`
   - **Linux**：`~/.config/EXILED/Plugins/`
3. 重启服务器生成默认配置文件。

### 配置说明
打开生成的插件配置文件，根据实际情况配置以下关键字段：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `is_enabled` | `true` | 是否启用本插件 |
| `tcp_port` | `10087` | 插件监听的 TCP 命令端口（多服时各服需不同） |
| `ip` | `127.0.0.1` | TCP 命令监听地址 |
| `server_name` | `1服` | 服务器展示名称（会显示在面板与 QQ 回显中） |
| `bot_ip` | `127.0.0.1` | `Server_Qcha.Bot` 所在服务器 IP |
| `bot_port` | `10088` | `Server_Qcha.Bot` 的通知接收端口 |
| `auth_token` | 空 | 安全鉴权密钥，**请自行设置并与主程序一致**。留空或仍使用默认密钥 `QchaSecret_123` 时插件会在启动日志中输出安全警告，但仍继续运行 |

---

## LabAPI 插件安装与配置

### 安装步骤
1. 从 Releases 下载 `Server_Qcha-LabAPI.dll`，重命名为 `Server_Qcha.dll`；
2. 将文件放置到按端口划分的 LabAPI 插件目录：
   ```text
   %APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<服务器端口>\Server_Qcha.dll
   ```
   （若希望所有端口共用，可放入 `plugins\global\` 目录）
3. 启动一次服务器，LabAPI 会自动生成配置目录：
   ```text
   %APPDATA%\SCP Secret Laboratory\LabAPI\configs\<服务器端口>\Server_Qcha\config.yml
   ```
4. LabAPI 版引用了 Lib.Harmony（用于游戏管理员配置的热应用），构建产物已把 `0Harmony.dll` 放在 `dependencies` 子目录，部署时请保持该结构：
   ```text
   LabAPI\plugins\<服务器端口>\
   ├── Server_Qcha.dll
   └── dependencies\
       └── 0Harmony.dll
   ```
   缺少该依赖时，涉及管理员配置热应用的代码路径会因找不到程序集而失败。

### 配置说明
打开 `config.yml`，字段含义与 EXILED 版完全对应：

```yaml
tcp_port: 10087
ip: 127.0.0.1
server_name: 1服
bot_ip: 127.0.0.1
bot_port: 10088
auth_token: YourSecretTokenHere
debug: false
```

---

## 核心机制与通信协议

### 双向通信架构
插件与主程序之间采用两组独立的连接：
1. **命令通道（下行）**：
   - 插件在游戏服监听 `tcp_port`（默认 10087）；
   - 主程序作为客户端按需连入下发指令（如 `cx`、`list`、`bc`、`kick` 等），执行完毕返回结果。
2. **通知通道（上行）**：
   - 插件主动外连主程序的 `bot_ip:bot_port`（默认 10088）；
   - 用于上报服务器注册 (`register`)、周期心跳 (`heartbeat`) 与玩家求助 (`ac`)。
   - **NAT 友好**：由于是由插件主动向外连出，游戏服无需放开入站映射即可完成实时通知推送。

### AuthToken 安全鉴权
所有 TCP 通信报文均强制要求附带 `AuthToken`：
- 主程序下发指令时需携带 Token，插件校验通过后才执行控制台命令；
- 插件上报 `.ac` 消息时携带 Token，主程序校验通过后才转发至 QQ 群；
- 若两端配置的 Token 不匹配，通信将被立即拒绝，杜绝未授权访问与恶意伪造。

### 游戏内 .ac 一键求助与广播
- **触发方式**：玩家在游戏内按 `~` 打开客户端控制台，输入：
  ```text
  .ac <反馈或求助内容>
  ```
- **联动效果**：
  1. 插件立即向主程序推送带有玩家昵称、SteamID、所在服名的结构化消息，由主程序转发至目标 QQ 管理群；
  2. 当前对局内所有拥有管理权限的在线管理员，屏幕正中央同步显示 10 秒醒目广播提示，便于第一时间处理违规行为。

### Dedicated Server 自动过滤
在 SCPSL 某些版本与插件环境下，服务端控制台会生成 `Dedicated Server` 占位玩家。
本插件在执行在线查询 (`cx`)、返回玩家列表 (`list`) 时，已在底层源码中统一过滤掉 `Dedicated Server`，确保 Web 面板、机器人回显及历史采样数据 100% 为真实玩家。

### 游戏管理员权限支持（QGA1）
插件是「游戏管理员」功能的执行端：面板通过命令通道下发读写请求，插件负责解析游戏服的 `config_remoteadmin.txt`、落盘，并在运行时热应用。

- **协议**：在旧 `v2|` 封套之外增加一层带长度前缀的 `QGA1` 帧（**帧正文仍是同一个封套**），单帧上限 2 MiB，用于承载数百 KB 的配置文件；两种格式在同一端口并存，老客户端不受影响。字段、操作与错误码见[通信协议文档](../reference/communication-protocol.md)。
- **能力探测**：面板先发旧文本命令 `game-admin-capabilities`；返回 `QGA1` 表示支持。**未升级的旧插件会返回其它内容，面板据此提示升级插件**，而不是静默失败。
- **双框架一致**：EXILED 与 LabAPI 两个版本共用同一套共享实现，权限语义与校验规则完全一致；响应中的 `Framework` 字段会标明当前框架。
- **写入安全**：所有参与文件在替换前先写持久化事务日志，中途异常可在下次启动自动回滚，不会留下半写的权限配置。
- **LabAPI 依赖**：见上文安装步骤第 4 步，需保持 `dependencies\0Harmony.dll` 的目录结构。
