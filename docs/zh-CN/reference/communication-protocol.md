<div align="center">

<img src="../../../assets/readme/doc-reference.svg" width="100%" alt="Server_Qcha 文档：技术参考。">

</div>

[文档中心](../../../docs/README.zh-CN.md) · [项目主页](../../../docs/zh-CN/README.md)

# Server_Qcha 通信协议文档

本文档描述了 QQ 机器人（Bot 端）与 SCPSL 游戏服务器插件端（Server 端）之间的通信协议。  
如果你希望开发**第三方工具**、**Web 面板**或**自定义机器人**与插件端进行交互，请参照本文档。

---

## 目录

- [架构概览](#架构概览)
- [通道一：命令通道（Bot → Server）](#通道一命令通道bot--server)
  - [连接参数](#连接参数)
  - [鉴权协议](#鉴权协议)
  - [支持的命令列表](#支持的命令列表)
  - [命令详细说明](#命令详细说明)
- [通道一扩展：游戏管理员权限协议（QGA1）](#通道一扩展游戏管理员权限协议qga1)
  - [帧格式](#帧格式)
  - [交互流程](#交互流程)
  - [请求与响应](#请求与响应)
  - [操作与约束](#操作与约束)
  - [权限解析规则](#权限解析规则)
  - [配置文件与事务](#配置文件与事务)
- [通道二：通知通道（Server → Bot）](#通道二通知通道server--bot)
  - [JSON 信封协议](#json-信封协议)
  - [消息类型详细说明](#消息类型详细说明)
  - [响应机制](#响应机制)
- [鉴权机制说明](#鉴权机制说明)
- [心跳与注册生命周期](#心跳与注册生命周期)
- [代码示例](#代码示例)
  - [Python 示例](#python-示例)
  - [C# 示例](#c-示例)
  - [Node.js 示例](#nodejs-示例)
- [错误处理与最佳实践](#错误处理与最佳实践)

---

## 架构概览

系统有两条独立的 TCP 通信通道：

```
┌──────────────────┐                         ┌──────────────────┐
│                  │   通道一：命令通道        │                  │
│   QQ 机器人       │ ──────────────────────→ │  游戏服插件端      │
│   (Bot 端)       │   TCP → 127.0.0.1:10087 │  (Server 端)     │
│                  │ ←────────────────────── │                  │
│                  │   响应（命令执行结果）      │                  │
│                  │                         │                  │
│                  │   通道二：通知通道        │                  │
│                  │ ←────────────────────── │                  │
│  0.0.0.0:10088   │   TCP → 注册/心跳/推送    │                  │
│                  │ ──────────────────────→ │                  │
│                  │   响应 "OK"              │                  │
└──────────────────┘                         └──────────────────┘
```

| 通道 | 方向 | 默认端口 | 用途 |
|------|------|----------|------|
| **命令通道** | Bot → Server | `10087`（自动探测） | 查询在线人数、服务器信息、广播、踢人封禁等 |
| **通知通道** | Server → Bot | `10088` | 服务器注册/注销、心跳保活、`.ac` 消息推送 |

两条通道均使用 **TCP 短连接**模式：建立连接 → 发送数据 → 读取响应 → 关闭连接。

---

## 通道一：命令通道（Bot → Server）

### 连接参数

| 参数 | 说明 |
|------|------|
| 协议 | TCP |
| 目标地址 | 插件配置的 `IP`（默认 `127.0.0.1`） |
| 目标端口 | 插件配置的 `tcp_port`（默认 `10087`，若被占用会自动递增） |
| 编码 | UTF-8 |
| 连接模式 | 短连接（一次请求一次响应后关闭） |
| 读取超时 | 建议 2~10 秒 |

### 鉴权协议

每条命令都必须带 HMAC 封套，旧的 `Token||正文` 明文格式不再接受。Token 为空或仍是默认密钥 `QchaSecret_123` 时，两端照常按同样的格式封装和校验，但会在启动日志中输出安全警告，提示尽快修改。

```
v2|{unix秒}|{nonce}|{base64(hmac-sha256)}|{命令内容}
```

HMAC 的密钥是 Token 的 UTF-8，原文是 `unix秒`、`nonce`、命令内容三行。时间戳允许偏差 120 秒，同一 nonce 不能重复使用。这条链路只鉴权，不加密内容；跨机器部署请走 VPN，或用防火墙限制来源。

**鉴权失败时**，服务器返回 `Unauthorized` 字符串。

### 支持的命令列表

| 命令 | 参数 | 说明 | 返回值示例 |
|------|------|------|-----------|
| `cx` | 无 | 查询在线人数 | `1服\r\n在线人数:5/40\r\n在线管理:1人\r\n` |
| `info` | 无 | 查询服务器详细信息 | `服务器#1服 - 查询Success!!\r\nDD人数:2\r\n...` |
| `list` | 无 | 获取在线玩家列表 | `\r\n玩家A-1\r\n玩家B-2` |
| `start` | 无 | 强制开始回合 | `回合启动成功` 或 `回合已经开启了` |
| `rest` | 无 | 重启回合（需回合开始 <60 秒） | `回合重启成功` 或 `拒绝：回合开始超过60秒` |
| `allrest` | 无 | 重启整个游戏服务器进程 | `服务器重启成功` |
| `bc&{消息}` | 消息内容 | 全服广播 | `bc发送成功` |
| `ychhe&{消息}` | 消息内容 | 自动广播（同时锁定大厅） | `bc发送成功` |
| `kick&{ID}&{原因}&{时间}` | 玩家ID、封禁原因、时长(秒) | 封禁玩家 | `封禁成功\r\n封禁ID:xxx\r\n...` |
| `ac` | 无 | 获取并消费 AC 推送内容（已废弃，使用通知通道替代） | `来自服务器:\r\n1服{payload}` |

### 命令详细说明

#### `cx` — 查询在线人数

**请求**：`cx`

**返回示例**：
```
1服
在线人数:12/40
在线管理:2人
查询时间 2026-05-24 19:30:00
```

返回内容与插件配置的 `DisplayMode` 有关：
- `DisplayMode = 0`：末尾显示查询时间
- `DisplayMode = 1`：末尾显示 `ContentText` 自定义文本
- `DisplayMode = 2`：末尾留空

---

#### `info` — 查询服务器详细信息

**请求**：`info`

**返回示例**：
```
服务器#1服 - 查询Success!!
DD人数:3
博士人数:2人
SCP人数:1
回合进行时间：00:05:32
回合次数：4
下一波刷新时间：00:02:15
查询时间2026-05-24 19:30:00
```

---

#### `list` — 在线玩家列表

**请求**：`list`

**返回示例**：
```
玩家Alpha-1
玩家Beta-2
玩家Gamma-3
```

格式为 `{玩家昵称}-{玩家ID}`，每行一个。

---

#### `bc&{消息}` — 全服广播

**请求**：`bc&维护通知：服务器将在5分钟后重启`

**返回**：`bc发送成功`

广播会在所有玩家屏幕上显示 15 秒，前缀为 `[管理员消息]`。

---

#### `kick&{ID}&{原因}&{时间}` — 封禁玩家

**请求**：`kick&3&使用外挂&3600`

| 参数 | 说明 |
|------|------|
| 第 1 个 `&` 后 | 游戏内玩家 ID（通过 `list` 命令获取） |
| 第 2 个 `&` 后 | 封禁原因 |
| 最后一个 `&` 后 | 封禁时长（秒） |

**返回示例**：
```
封禁成功
封禁ID:76561198xxxxxxxxx@steam
封禁时间:3600
原因:使用外挂
```

---

#### `rest` — 重启回合

**请求**：`rest`

**安全限制**：仅在回合开始 60 秒内允许执行。超过 60 秒返回 `拒绝：回合开始超过60秒`。

---

#### `allrest` — 重启服务器

**请求**：`allrest`

⚠️ **危险操作**：会直接重启整个 SCPSL 服务器进程。

---

## 通道一扩展：游戏管理员权限协议（QGA1）

面板的「游戏管理员」功能需要对游戏服的 `config_remoteadmin.txt` 做完整的读—改—写，报文可达数百 KB；而旧的文本命令最多读取 4096 字节一次，无法承载。因此在命令通道之上增加了一层**带长度前缀的报文帧**。

> 这一层只解决「报文有多长」，不改变安全模型：**帧正文仍是 `v2|` HMAC 封套**，时间戳、nonce 与重放校验规则与旧通道完全相同。响应为明文（该链路始终只鉴权、不加密）。

### 帧格式

```
┌────────┬────────────────┬──────────────────────┐
│ QGA1   │ 长度（4B 大端） │ UTF-8 正文            │
└────────┴────────────────┴──────────────────────┘
```

| 字段 | 长度 | 说明 |
|------|------|------|
| 魔数 | 4 字节 | ASCII `QGA1`（`0x51 0x47 0x41 0x31`） |
| 长度 | 4 字节 | 大端序，正文字节数 |
| 正文 | 变长 | UTF-8；请求方向上是 `v2|` 封套字符串 |

- 入站请求正文（包含 `v2|` 鉴权封套）上限 **300 KiB**，读取长度头后先校验，再分配正文缓冲区；面板变更 JSON 上限为 **256 KiB UTF-8 字节**；
- 响应正文上限 **2 MiB**，用于完整权限快照；
- 响应同样以帧格式写回；
- 服务端读到首字节 `Q`（`0x51`）时按帧解析，否则回落到旧的文本通道——两种格式在同一端口并存，老客户端不受影响；
- 帧内命令仍要经过封套校验；校验失败时在帧内返回 `Unauthorized`，客户端据此提示「游戏服鉴权失败，请检查共享 Token」。

两套游戏插件均限制最多 **32 个活动命令连接**，达到上限的新连接立即关闭，且不会创建处理任务。整个请求读取（含首字节、长度头和正文）共享 **2 秒总截止时间**，持续零星发送字节不会续期；响应写入截止时间为 **15 秒**。超时或插件停止时主动关闭 socket，不依赖 Mono 的异步读取是否响应 `CancellationToken`。

### 交互流程

1. **能力探测（仍走旧文本通道）**：发送文本命令 `game-admin-capabilities`。
   - 返回 `QGA1` → 支持该协议；
   - 返回其它内容 → 插件过旧，面板提示「该插件不支持游戏权限管理，请升级对应的 EXILED / LabAPI 插件」；
   - 无响应 → 视为服务器离线。
2. **读写（走帧）**：新建一条短连接（超时 20 秒），把 `game-admin&{JSON}` 经 `v2|` 封套封装后写入 QGA1 帧，再读取同样格式的响应。

### 请求与响应

请求对象（`AdminRequest`）：

| 字段 | 说明 |
|------|------|
| `Operation` | 操作名，缺省为 `read` |
| `Revision` | 客户端读取快照时拿到的版本，用于并发控制 |
| `RequestId` | 请求标识，必填且不超过 80 字符；同一标识不可复用 |
| `Key` | 目标权限组或管理员的标识 |
| `Group` / `Member` | 要写入的权限组 / 成员对象 |

`Operation` 取值（其它值一律返回「不支持的权限操作」）：

| 操作 | 作用 |
|------|------|
| `read` / `capabilities` | 只读，返回当前快照 |
| `create-group` | 新建权限组 |
| `update-group` | 修改现有权限组 |
| `delete-group` | 删除权限组 |
| `set-member` | 新增或修改管理员的权限分配 |
| `delete-member` | 删除管理员 |

响应对象（`AdminReply`）：

| 字段 | 说明 |
|------|------|
| `Success` | 是否成功 |
| `Error` | 失败原因（中文，可直接展示给用户） |
| `Code` | 失败时的机器可读码 |
| `Persisted` | 配置是否已落盘 |
| `Applied` | 是否已热应用（在线管理员立即生效） |
| `Snapshot` | 操作后的最新快照 |

面板侧区分处理的 `Code`：

| `Code` | 含义 |
|--------|------|
| `conflict` | 提交时 `Revision` 与磁盘不一致（配置已被他人改动），需重新读取后再编辑 |
| `unauthorized` | 共享 Token 不匹配 |
| `upgrade` | 插件不支持该协议 |
| `offline` | 游戏服无响应或连接超时 |

快照（`AdminSnapshot`）包含：

| 字段 | 说明 |
|------|------|
| `Revision` | 参与文件内容与路径的哈希，任一文件变化即改变 |
| `Framework` | `EXILED` 或 `LABAPI` |
| `Catalog` | 该框架的权限目录（键 + 显示名） |
| `Groups` / `Members` | 权限组与管理员列表 |

### 操作与约束

写入类操作带一组业务校验，违反时通过 `Error` 返回可读原因：

- **组标识不可更改**：`update-group` 的 `Key` 与 `Group.Key` 必须一致；
- **共享组只读**：来自游戏共享配置的权限组（`Shared`）不能直接修改或删除，需先「复制为本服权限组」；
- **删除前先解除引用**：仍有成员归属或被其它组继承的权限组不能删除；共享组与框架默认权限组不可删除；
- **管理员同理**：来自共享配置的管理员不能在面板中删除或改派；指派时必须选本服游戏权限组，不能指向共享组或框架默认组。

### 权限解析规则

- **原生权限**：`组权限 ∪ 个人允许 − 个人拒绝`，个人拒绝优先级最高；
- **插件权限**：先看插件权限总开关（成员级覆盖组级，任一为 `false` 即整体拒绝），再由个人拒绝一票否决，最后匹配个人允许与继承链上的节点；
- **通配符**：支持 `*` 与 `.*`（全部）、精确匹配、`前缀.*` 三种形式，匹配不区分大小写；
- **继承**：继承链展开后去重；出现循环或引用了不存在的组时直接报错，不会静默忽略。

### 配置文件与事务

- 主配置写入游戏服的 `config_remoteadmin.txt`；共享组相关条目以 `qga_` 前缀写入；
- 写入采用**持久化事务日志**：先记录所有待替换文件的原始内容，全部替换成功后才删除日志；
- 若进程在替换中途退出，**下次启动会依据日志自动回滚**到写入前的状态，避免留下半写配置；
- 存在未恢复的事务时会拒绝新的写入，需先完成恢复。

---

## 通道二：通知通道（Server → Bot）

通知通道用于游戏服插件端**主动向机器人端**发送消息，包括服务器注册、心跳保活和 `.ac` 推送。

### 连接参数

| 参数 | 说明 |
|------|------|
| 协议 | TCP |
| 目标地址 | 机器人配置的 `NotificationHost`（默认 `127.0.0.1`） |
| 目标端口 | 机器人配置的 `NotificationPort`（默认 `10088`） |
| 编码 | UTF-8 |
| 连接模式 | 短连接 + **半关闭**：发送完数据后调用 `Shutdown(Send)`，然后读取响应 |

### JSON 信封协议

通知通道统一使用 JSON 信封格式。整体报文结构与命令通道相同，负载换成 JSON：

```
v2|{unix秒}|{nonce}|{base64(hmac-sha256)}|{JSON信封}
```

Token 为空或仍是默认密钥时，机器人照常监听、插件照常发送通知，两端都会在启动日志中输出安全警告。

JSON 信封结构如下：

```json
{
  "type": "register | unregister | heartbeat | ac",
  "data": { ... }
}
```

> **重要**：发送完毕后必须调用 `Socket.Shutdown(SocketShutdown.Send)` 执行 TCP 半关闭，告知机器人端数据已发完，然后再读取 `"OK"` 响应。否则机器人端会一直等待数据到达。

### 消息类型详细说明

#### `register` — 服务器注册

游戏服启动时发送，告知机器人端自己的存在。

```json
{
  "type": "register",
  "data": {
    "name": "1服",
    "connectHost": "192.168.1.100",
    "port": 10087,
    "gamePort": 7777,
    "sortOrder": 1
  }
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `name` | string | 是 | 服务器显示名称 |
| `connectHost` | string | 是 | 机器人回连本服务器使用的 IP |
| `port` | int | 是 | TCP 命令端口（机器人用此端口下发指令） |
| `gamePort` | int | 否 | 游戏运行端口（用于自动排序） |
| `sortOrder` | int | 否 | 排序权重，>0 手动排序，=0 自动排序 |

---

#### `unregister` — 服务器注销

游戏服正常关闭时发送。

```json
{
  "type": "unregister",
  "data": {
    "connectHost": "192.168.1.100",
    "port": 10087
  }
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `connectHost` | string | 是 | 与注册时相同 |
| `port` | int | 是 | 与注册时相同 |

---

#### `heartbeat` — 心跳保活

每 30 秒发送一次，携带完整注册信息（兼作断线重注册）。

```json
{
  "type": "heartbeat",
  "data": {
    "name": "1服",
    "connectHost": "192.168.1.100",
    "port": 10087,
    "gamePort": 7777,
    "sortOrder": 1
  }
}
```

`data` 字段与 `register` 完全一致。如果机器人重启导致注册表清空，收到心跳后会自动重新注册。

---

#### `ac` — 消息推送到 QQ 群

玩家在游戏内使用 `.ac` 指令时触发。

```json
{
  "type": "ac",
  "data": {
    "message": "来自服务器 [1服]:\n玩家 [TestPlayer] 发送了：大家好"
  }
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `message` | string | 是 | 推送到 QQ 群的完整文本内容 |

---

### 响应机制

机器人端成功处理消息后，通过**同一 TCP 连接**回写两个字节的 ASCII 字符串：

```
OK
```

发送端应在半关闭后等待读取该响应（建议超时 5 秒）：
- 收到 `"OK"` → 处理成功
- 超时 / 连接断开 / 内容不是 `"OK"` → 处理失败

---

## 鉴权机制说明

| 场景 | 格式 | Token 为空或仍是默认密钥 |
|------|------|---------------|
| 命令通道（Bot → Server） | `v2\|unix秒\|nonce\|hmac\|命令` | 照常通信，启动日志输出安全警告 |
| 通知通道（Server → Bot） | `v2\|unix秒\|nonce\|hmac\|JSON` | 照常通信，启动日志输出安全警告 |

时间戳允许偏差 120 秒，nonce 不可重放。HMAC 用 SHA-256，密钥是 Token 的 UTF-8。信道不加密。

**鉴权失败的响应**：

| 通道 | 失败时 |
|------|-----------|
| 命令通道 | 返回 `"Unauthorized"` |
| 通知通道 | 静默丢弃（无响应） |

---

## 心跳与注册生命周期

```
游戏服启动
    │
    ▼
探测可用端口（从 tcp_port 开始，最多尝试 100 个）
    │
    ▼
绑定 TCP 命令服务
    │
    ▼
发送 register → 机器人
    │
    ├─ 收到 "OK" → 注册成功
    └─ 超时/失败 → 继续进入心跳循环重试
    │
    ▼
╔══════════════════════════════════╗
║  心跳循环（每 30 秒）              ║
║                                  ║
║  发送 heartbeat → 机器人           ║
║    ├─ 成功 → 重置失败计数           ║
║    └─ 失败 → 指数退避               ║
║         30s → 60s → 120s          ║
║         → 240s → 300s（封顶）      ║
║         连续 10 次 → 慢速模式       ║
╚══════════════════════════════════╝
    │
    ▼ (插件关闭)
发送 unregister → 机器人
    │
    ▼
关闭 TCP 命令服务
```

**机器人端清理机制**：
- 每 60 秒扫描注册表
- 超过 90 秒未收到心跳的**动态注册**条目被自动移除
- 手动配置的 `Ports` 静态条目**不受**心跳超时影响

---

## 代码示例

以下示例只用各语言标准库，封套算法与 `server/TcpAuthEnvelope.cs` 的 `TrySeal` 逐字节一致（机器人侧发命令见 `SocketCommandClient`，插件侧发通知见 `BotNotificationClient`）。不要再拼接 `Token||正文`。要点：

- **报文**：`v2|{unix秒}|{nonce}|{mac}|{负载}`，整条按 UTF-8 编码写入，末尾不加换行。
- **unix秒**：UTC 秒级时间戳（十进制整数），与接收端偏差不能超过 120 秒。
- **nonce**：每条报文重新生成，16 字节随机数转成 32 位小写十六进制；接收端只接受 16~128 位字母或数字，且不可重复使用。
- **mac**：HMAC-SHA256，密钥是去掉首尾空白后的 Token 的 UTF-8；原文是 `"{unix秒}\n{nonce}\n{负载}"` 的 UTF-8（分隔符为单个 `\n`）；结果用标准 Base64（带 `=` 填充）。
- **命令通道**：插件只读一次、最多 4096 字节，并会先去掉整条报文首尾的空白，所以报文要一次写完，命令末尾不要带空格或换行。插件回写明文响应后即关闭连接。
- **通知通道**：写完后半关闭，机器人读到 EOF 才开始校验（上限 64 KB，5 秒内传完），成功回 `OK`；鉴权失败直接断开，不回任何内容。
- **响应不带 HMAC**：两条通道的响应都是明文，不能用来证明对方身份。
- 示例里的 Token 只是占位符，实际使用请从配置文件或环境变量读取，并与两端的 `AuthToken` 完全一致。

### Python 示例

需要 Python 3.6+。

```python
import base64
import hashlib
import hmac
import json
import secrets
import socket
import time

TOKEN = "请替换为你自己的随机Token"  # 与机器人/插件的 AuthToken 完全一致


def seal(token, payload, unix=None, nonce=None):
    """封装为 v2|unix秒|nonce|mac|负载，与 TcpAuthEnvelope.TrySeal 一致"""
    token = token.strip()
    if not token:
        raise ValueError("Token 不能为空")
    unix = int(time.time()) if unix is None else unix
    nonce = nonce or secrets.token_hex(16)  # 32 位小写十六进制
    material = f"{unix}\n{nonce}\n{payload}".encode("utf-8")
    digest = hmac.new(token.encode("utf-8"), material, hashlib.sha256).digest()
    mac = base64.b64encode(digest).decode("ascii")
    return f"v2|{unix}|{nonce}|{mac}|{payload}"


def send_command(host, port, command, timeout=5.0):
    """命令通道：发送一条命令，返回插件的明文响应"""
    wire = seal(TOKEN, command).encode("utf-8")
    if len(wire) > 4096:
        raise ValueError("报文超过 4096 字节，插件只读取一次")
    with socket.create_connection((host, port), timeout=timeout) as s:
        s.sendall(wire)  # 一次写完，不加换行
        chunks = []
        while True:
            data = s.recv(4096)
            if not data:  # 插件写完响应后关闭连接
                break
            chunks.append(data)
    return b"".join(chunks).decode("utf-8")


def send_notification(host, port, msg_type, data, timeout=5.0):
    """通知通道：发送 JSON 信封，机器人处理成功后回 OK"""
    envelope = json.dumps({"type": msg_type, "data": data}, ensure_ascii=False)
    with socket.create_connection((host, port), timeout=timeout) as s:
        s.sendall(seal(TOKEN, envelope).encode("utf-8"))
        s.shutdown(socket.SHUT_WR)  # 半关闭：机器人读到 EOF 才开始校验
        resp = s.recv(128)  # 鉴权失败时机器人直接断开，resp 为空
    return resp.decode("utf-8").strip() == "OK"


if __name__ == "__main__":
    # 命令通道：查询在线人数、广播
    print(send_command("127.0.0.1", 10087, "cx"))
    print(send_command("127.0.0.1", 10087, "bc&服务器将在5分钟后维护"))

    # 通知通道：注册 + 心跳（data 字段两者相同）
    server = {"name": "测试服", "connectHost": "127.0.0.1", "port": 10087, "gamePort": 7777, "sortOrder": 1}
    print("注册结果:", send_notification("127.0.0.1", 10088, "register", server))
    print("心跳结果:", send_notification("127.0.0.1", 10088, "heartbeat", server))
```

---

### C# 示例

需要 .NET 6+（顶级语句）。在 .NET 项目里也可以直接链接 `server/TcpAuthEnvelope.cs`，调用 `TcpAuthEnvelope.TrySeal`。

```csharp
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string Token = "请替换为你自己的随机Token"; // 与机器人/插件的 AuthToken 完全一致

// 命令通道：查询玩家列表
Console.WriteLine(await SendCommandAsync("127.0.0.1", 10087, "list"));

// 通知通道：心跳
bool ok = await SendNotificationAsync("127.0.0.1", 10088, "heartbeat", new
{
    name = "测试服",
    connectHost = "127.0.0.1",
    port = 10087,
    gamePort = 7777,
    sortOrder = 1
});
Console.WriteLine($"心跳结果: {ok}");

// 封装为 v2|unix秒|nonce|mac|负载，与 TcpAuthEnvelope.TrySeal 一致
static string Seal(string token, string payload, long? unix = null, string? nonce = null)
{
    token = token.Trim();
    if (token.Length == 0)
        throw new ArgumentException("Token 不能为空");
    long ts = unix ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    string n = nonce ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token));
    string mac = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}\n{n}\n{payload}")));
    return $"v2|{ts}|{n}|{mac}|{payload}";
}

static async Task<string> SendCommandAsync(string host, int port, string command)
{
    using var cts = new CancellationTokenSource(5000);
    using var client = new TcpClient();
    await client.ConnectAsync(host, port, cts.Token);
    using var stream = client.GetStream();

    byte[] wire = Encoding.UTF8.GetBytes(Seal(Token, command));
    await stream.WriteAsync(wire, cts.Token); // 一次写完（≤4096 字节）

    using var ms = new MemoryStream();
    await stream.CopyToAsync(ms, cts.Token); // 插件写完响应后关闭连接
    return Encoding.UTF8.GetString(ms.ToArray());
}

static async Task<bool> SendNotificationAsync(string host, int port, string type, object data)
{
    using var cts = new CancellationTokenSource(5000);
    using var client = new TcpClient();
    await client.ConnectAsync(host, port, cts.Token);
    using var stream = client.GetStream();

    string json = JsonSerializer.Serialize(new { type, data });
    await stream.WriteAsync(Encoding.UTF8.GetBytes(Seal(Token, json)), cts.Token);
    client.Client.Shutdown(SocketShutdown.Send); // 半关闭

    var buffer = new byte[128];
    int count = await stream.ReadAsync(buffer, cts.Token); // 鉴权失败时为 0
    return Encoding.UTF8.GetString(buffer, 0, count).Trim() == "OK";
}
```

---

### Node.js 示例

只用内置的 `crypto` 与 `net` 模块。

```javascript
const crypto = require('crypto');
const net = require('net');

const TOKEN = '请替换为你自己的随机Token'; // 与机器人/插件的 AuthToken 完全一致

// 封装为 v2|unix秒|nonce|mac|负载，与 TcpAuthEnvelope.TrySeal 一致
function seal(token, payload, unix = Math.floor(Date.now() / 1000), nonce = crypto.randomBytes(16).toString('hex')) {
    token = token.trim();
    if (!token) throw new Error('Token 不能为空');
    const mac = crypto.createHmac('sha256', Buffer.from(token, 'utf8'))
        .update(`${unix}\n${nonce}\n${payload}`, 'utf8')
        .digest('base64');
    return `v2|${unix}|${nonce}|${mac}|${payload}`;
}

// 命令通道：发送一条命令，返回插件的明文响应
function sendCommand(host, port, command) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        const client = net.connect(port, host, () => client.write(seal(TOKEN, command), 'utf8')); // 一次写完
        client.setTimeout(5000);
        client.on('data', (chunk) => chunks.push(chunk));
        client.on('end', () => resolve(Buffer.concat(chunks).toString('utf8'))); // 插件写完响应后关闭连接
        client.on('timeout', () => { client.destroy(); reject(new Error('Timeout')); });
        client.on('error', reject);
    });
}

// 通知通道：发送 JSON 信封，机器人处理成功后回 OK
function sendNotification(host, port, type, data) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        const client = net.connect(port, host, () => {
            client.end(seal(TOKEN, JSON.stringify({ type, data })), 'utf8'); // 写完即半关闭
        });
        client.setTimeout(5000);
        client.on('data', (chunk) => chunks.push(chunk));
        client.on('end', () => resolve(Buffer.concat(chunks).toString('utf8').trim() === 'OK')); // 鉴权失败时为空
        client.on('timeout', () => { client.destroy(); reject(new Error('Timeout')); });
        client.on('error', reject);
    });
}

(async () => {
    console.log(await sendCommand('127.0.0.1', 10087, 'cx'));

    const ok = await sendNotification('127.0.0.1', 10088, 'ac', {
        message: '来自服务器 [测试服]: 这是一条测试推送'
    });
    console.log('推送结果:', ok);
})().catch(console.error);
```

---

## 错误处理与最佳实践

### 常见错误

| 现象 | 可能原因 | 解决方法 |
|------|---------|---------|
| 返回 `Unauthorized` | Token 不一致、时间偏差超过 120 秒、报文重放，或仍在使用旧的明文格式 | 机器人与插件一起升级，并确认 `AuthToken` 完全一致 |
| 返回 `empty command` | 发送了空数据 | 检查是否发送了有效命令字符串 |
| 返回 `unknown command` | 命令拼写错误 | 参照命令列表检查拼写（区分大小写） |
| 连接被拒绝 | 插件未启动或端口错误 | 检查插件日志确认实际绑定端口 |
| 通知无响应 | 未执行 TCP 半关闭 | 发送数据后必须调用 `Shutdown(Send)` |
| 通知返回非 `OK` | JSON 格式错误或缺少必要字段 | 检查 JSON 格式和必填字段 |

### 最佳实践

1. **始终设置超时**：命令通道建议 5~10 秒，通知通道建议 5 秒
2. **使用短连接**：每次请求创建新的 TCP 连接，不要复用
3. **通知通道必须半关闭**：发完数据后调用 `Shutdown(Send)` / `socket.SHUT_WR` / `client.end()`，否则机器人端不知道数据已发完
4. **命令区分大小写**：`cx` ≠ `Cx`，所有命令均为**全小写**
5. **参数分隔符是 `&`**：不是空格，例如 `bc&消息内容`、`kick&ID&原因&时间`
6. **Token 不要硬编码**：使用配置文件或环境变量管理
7. **自动端口发现**：如果你的工具需要发现游戏服的实际端口，可以向机器人的通知端口发送一次 `register`，或者读取 EXILED 插件的日志输出
