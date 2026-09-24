# Server_Qchat_API 轻量级查服机器人

Server_Qchat_API 是本仓库提供的 **独立轻量级部署版本**。该版本专为仅需在 QQ 群中快速查询 SCP: Secret Laboratory 服务器在线人数的服主或管理员设计。

与主程序（包含 LocalAdmin 独立守护、Web 面板、游戏端插件与双向 TCP 通信的完整版 Qridge）不同，轻量 API 版本**无需在游戏服务器上安装任何服务端插件**，亦无需配置复杂的网络通信，仅需接入 OneBot 11 机器人框架并配置服务器 ID 即可即刻投入使用。

---

## 核心特性

- **零插件无侵入**：无需在 SCPSL 游戏服部署任何插件，直接通过列表节点 API 查询人数，对游戏服务器性能与稳定性零影响。
- **OneBot 11 标准协议**：采用标准正向 WebSocket 协议，全面兼容 NapCatQQ、Lagrange、LLOneBot 等主流 OneBot 11 接入端。
- **双重配置支持**：支持启动时交互式动态输入服务器 ID（支持 `*`、`,` 或空格多服分隔），亦支持在配置文件中固化默认服务器列表。
- **断线自愈重连**：内置 WebSocket 心跳维系与异常自动重连机制，当框架重启或网络抖动时自动每 3 秒尝试重连，无需人工干预。
- **高并发异步拉取**：多服务器并发异步请求与聚合，毫秒级响应群聊查询请求。
- **极低资源开销**：单进程控制台运行，内存占用通常低于 30MB，适合部署在低配 VPS 或本地电脑。

---

## 运行要求

- **操作系统**：Windows 10 / 11 / Server 2016+ 或 Linux（x64 / arm64）
- **运行环境**：[.NET 6.0 Runtime](https://dotnet.microsoft.com/download/dotnet/6.0) 或更高版本（.NET 8.0 亦兼容）
- **机器人框架**：推荐使用 [NapCatQQ](https://github.com/NapNeko/NapCatQQ)（启用正向 WebSocket 端口 6700）
- **网络连接**：运行环境需可正常访问 `https://api.scplist.kr/`

---

## 快速上手步骤

### 第一步：配置并启动 QQ 机器人框架 (NapCatQQ)

1. 部署并登录 NapCatQQ（参考 [NapCatQQ 官方文档](https://github.com/NapNeko/NapCatQQ)）；
2. 打开 NapCatQQ 配置目录中的 `config/onebot11_<你的QQ号>.json`；
3. 找到 `ws`（正向 WebSocket）配置段，确保启用并监听在本地 `6700` 端口：

```json
{
  "ws": {
    "enable": true,
    "host": "127.0.0.1",
    "port": 6700
  }
}
```

4. 保存配置文件并重启 NapCatQQ，确认控制台输出正向 WebSocket 监听成功。

---

### 第二步：配置 Server_Qchat_API

打开 `Server_Qchat_API/appsettings.json`：

```json
{
  "App": {
    "WsBaseUri": "ws://localhost:6700",
    "CommandKeyword": "cx",
    "DefaultServers": [ 31146, 31150, 31160 ]
  }
}
```

#### 配置参数详解

| 参数项 | 数据类型 | 默认值 | 说明 |
|---|---|---|---|
| `App:WsBaseUri` | String | `ws://localhost:6700` | NapCatQQ 正向 WebSocket 的连接地址与端口 |
| `App:CommandKeyword` | String | `cx` | 群聊中触发查服响应的关键词（不区分大小写，包含匹配） |
| `App:DefaultServers` | Array<Integer> | `[]` | 默认监控的服务器 ID 列表（若启动未交互输入则使用此列表） |

> 说明：亦支持通过环境变量覆盖配置，变量前缀为 `SERVER_QCHAT_API_`，例如设置 `SERVER_QCHAT_API_App__WsBaseUri=ws://127.0.0.1:6700`。

---

### 第三步：获取服务器 ID (Server ID)

本程序通过 SCPSL 列表接口进行查询，需要填入目标服务器在官方列表中的唯一数字 ID：

1. 打开浏览器访问 SCPSL 公共服务器列表网页或第三方统计站；
2. 找到您的服务器，查看该服务器对应的唯一数字 ID（通常由 5 位数字组成，例如 `31146`）；
3. 记录该数字 ID 用于后续启动配置。

---

### 第四步：启动并运行

#### 方式 A：直接运行并交互指定服务器

双击启动 `Server_Qchat_API.exe`，控制台将提示输入服务器 ID：

```text
开发者：yiming 3037240065 liseximt@outlook.com
[DIRSystem]: 使用前请确保打开正向WebSocket 6700 端口，服务器端开放对应 API 端口

请输入服务器查询id(多个用'*'或','隔开，如:31140*31150*31160):
```

- 若您希望临时查询特定服务器，直接输入 ID 并回车，例如：`31146*31150*31160`；
- 若已在 `appsettings.json` 中配置了 `DefaultServers`，可直接敲击回车键，程序将自动载入默认服务器列表。

#### 方式 B：无无人值守后台启动

在已配置好 `appsettings.json` 的情况下，可直接编写批处理或加入系统守护后台静默启动。

---

## 群聊指令与效果展示

### 触发指令

在机器人所在的任意 QQ 群中，发送包含触发关键词（默认为 `cx`）的消息即可：

```text
cx
```

### 机器人响应效果

机器人将自动发起 API 异步查询，并在群内回复如下格式的内容：

```text
当前 - 1服
在线人数：18

当前 - 2服
在线人数：24

当前 - 3服
在线人数：0
```

---

## 版本功能对比：API 轻量版 vs Qridge 完整版

| 功能维度 | API 轻量版 (Server_Qchat_API) | Qridge 完整版 (Server_Qcha.Bot + Daemon) |
|---|---|---|
| **定位场景** | 仅需群内人数快捷查询，免维护 | 全功能综合运维、守护集群、社群联动与面板控制 |
| **游戏端插件** | **无需安装任何插件** | 需安装 EXILED 或 LabAPI 服务端插件 |
| **LocalAdmin 守护** | 不支持 | 支持独立守护 (Daemon) 与进程异常自愈 |
| **Web 运维面板** | 无 | 内置 Vue 3 + Element Plus 现代化控制台 |
| **群内游戏交互** | 仅支持查询人数 | 广播、踢人、封禁、换边、开局、重启回合等全量指令 |
| **游戏内联动** | 不支持 | 支持游戏内输入 `.ac <内容>` 一键报警呼叫群管理 |
| **机器人接入方式** | 仅支持 OneBot 11 正向 WebSocket | 双模式支持：NapCat (OneBot 11) + 腾讯官方 Bot OpenAPI v2 |
| **部署成本** | 极低（1 分钟开箱即用） | 中等（需配置端口与服务端插件） |

---

## 常见问题与排错指南

### 1. 启动提示 `WebSocket connect/run failed; retrying in 3s`
- **原因**：无法连接到 NapCatQQ 提供的 WebSocket 端口。
- **解决方案**：
  - 检查 NapCatQQ 是否已启动并登录成功；
  - 检查 NapCatQQ 的配置文件中是否已将 `ws.enable` 设为 `true`；
  - 检查 `appsettings.json` 中的 `WsBaseUri` 端口是否与 NapCatQQ 的监听端口一致（默认 6700）；
  - 检查本机防火墙是否拦截了对应本地端口的访问。

### 2. 群内发送 `cx` 后机器人没有回复
- **原因与排查**：
  - 检查机器人 QQ 是否已被禁言或在黑名单中；
  - 检查群消息中是否确实包含了关键词（不区分大小写）；
  - 检查控制台是否有网络请求异常日志。若运行机器无法访问海外或公共 API，可能导致请求超时。

### 3. 查询人数显示为 0 或不准确
- **原因**：公共列表 API 通常存在 1 分钟左右的数据更新与缓存窗口。若服务器刚启动或刚刚加入玩家，可能会有短暂的延迟。

---

## 开源协议与版权声明

本项目基于 [MIT License](LICENSE) 协议开源。

Copyright 2025 hmyhserver.top
