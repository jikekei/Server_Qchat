# 更新日志 (Changelog)

## v1.2.0 — 2026-05-21

### 🚀 新功能

#### 1. 游戏内 `.ac` 实时推送指令
玩家可在游戏中按 `~` 打开控制台，输入 `.ac <消息内容>`，消息将**实时推送至指定 QQ 群**。

- **主动推送架构**：游戏服务端在指令触发时，异步建立 TCP 连接将消息发往 QQ 机器人，发送完毕立即断开。全程在后台线程执行，**不阻塞游戏主线程**，不会造成任何卡顿。
- **管理员同步广播**：当玩家使用 `.ac` 发送消息后，当前对局内所有拥有管理权限（`RemoteAdminAccess`）的在线管理员屏幕上会同时显示一条持续 10 秒的广播提示，格式为：
  ```
  [AC推送] 玩家 XXX 发送了：
  <消息内容>
  ```
- **目标群聊后端统一配置**：推送目标 QQ 群号在机器人端的配置文件中管理，游戏服务端无需关心具体群号。

#### 2. 双向通信鉴权系统 (AuthToken)
为游戏服务端插件与 QQ 机器人之间的**所有 TCP 通信**增加了安全校验层。

- **预共享密钥机制**：双端通过在配置文件中设置相同的 `AuthToken` 进行身份认证。所有通信数据在传输时会自动附带 Token 签名。
- **双向强制校验**：
  - **机器人 → 游戏服**：机器人发送指令（如查询人数、重启回合、踢人封禁等）时，游戏服端会验证 Token，不匹配则返回 `Unauthorized` 并立即断开连接。
  - **游戏服 → 机器人**：`.ac` 推送消息到机器人时，机器人端同样会验证 Token，防止第三方伪造推送向 QQ 群发送垃圾信息。
- **向下兼容**：若将 `AuthToken` 留空（`""`），则自动回退为无鉴权模式，保持与旧版行为一致。

#### 3. 后台通知监听服务
QQ 机器人端新增独立的 TCP 监听服务，专门用于接收游戏服务端的主动推送。

- 支持自定义监听 IP（`NotificationHost`，默认 `0.0.0.0`）和端口（`NotificationPort`，默认 `10088`）。
- 与现有的命令通信端口（默认 `10087`）完全独立，互不影响。

---

### ⚙️ 配置说明

#### 游戏服务端插件配置（EXILED `config.yml`）

新增以下配置项：

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `bot_i_p` | string | `"127.0.0.1"` | QQ 机器人后台通知监听服务的 IP 地址 |
| `bot_port` | int | `10088` | QQ 机器人后台通知监听服务的端口号 |
| `auth_token` | string | `"QchaSecret_123"` | 安全验证 Token，须与机器人端一致 |

示例：
```yaml
server_qcha:
  # ... (原有配置保持不变)
  bot_i_p: '127.0.0.1'
  bot_port: 10088
  auth_token: 'YourSecretTokenHere'
```

#### QQ 机器人端配置（`appsettings.json`）

`SocketServer` 节点新增：

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `NotificationHost` | string | `"0.0.0.0"` | 后台通知监听服务绑定的 IP 地址 |
| `NotificationPort` | int | `10088` | 后台通知监听服务绑定的端口号 |
| `AuthToken` | string | `"QchaSecret_123"` | 安全验证 Token，须与游戏服端一致 |

`Bot` 节点新增：

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `AcTargetGroupId` | long | `0` | 接收 `.ac` 推送消息的目标 QQ 群号 |

示例：
```json
{
  "SocketServer": {
    "Host": "127.0.0.1",
    "Ports": [ 10087 ],
    "NotificationHost": "0.0.0.0",
    "NotificationPort": 10088,
    "AuthToken": "YourSecretTokenHere",
    "ConnectTimeoutMs": 10000,
    "ReadTimeoutMs": 2000,
    "Retries": 3,
    "RetryDelayMs": 1000
  },
  "Bot": {
    "AllowedGroupIds": [],
    "NotifyGroupIds": [],
    "NotifyPrivateUserIds": [],
    "AcTargetGroupId": 123456789
  }
}
```

---

### 📦 涉及文件变更

#### 游戏服务端插件（server/）
| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Main.cs` | 修改 | 新增 `BotIP`、`BotPort`、`AuthToken` 配置项；添加 `Instance` 单例 |
| `TcpCommandServer.cs` | 修改 | 接入 AuthToken 鉴权逻辑 |
| `AcCommand.cs` | **新增** | 实现 `.ac` 控制台指令 |
| `BotNotificationClient.cs` | **新增** | 异步 TCP 推送客户端 |
| `Server_Qcha.csproj` | 修改 | 引入新增的编译文件 |

#### QQ 机器人端（bot/）
| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Configuration/BotOptions.cs` | 修改 | 新增 `AcTargetGroupId` |
| `Configuration/SocketServerOptions.cs` | 修改 | 新增 `NotificationHost`、`NotificationPort`、`AuthToken` |
| `Socket/SocketCommandClient.cs` | 修改 | 发送命令时附带 AuthToken |
| `Socket/BotNotificationListenerService.cs` | **新增** | 后台 TCP 通知监听服务 |
| `Program.cs` | 修改 | 注册 `BotNotificationListenerService` |
| `appsettings.json` | 修改 | 新增配置字段 |
| `appsettings.Example.json` | 修改 | 新增配置字段 |

---

### 🔒 安全须知

> **重要**：部署前请务必修改默认的 `AuthToken`！默认值 `QchaSecret_123` 仅用于演示，请替换为您自己的强密码，并确保游戏服端与机器人端的 Token **完全一致**。

---

### ✅ 部署步骤

1. **更新游戏服务端**：将新编译的 `Server_Qcha.dll` 复制到游戏服的 `EXILED/Plugins/` 目录，覆盖旧版本。
2. **更新机器人端**：将新编译的 `bot/` 输出目录部署到机器人运行环境。
3. **配置双端 Token**：在两端配置文件中设置相同的 `AuthToken`。
4. **配置目标群号**：在机器人端的 `Bot:AcTargetGroupId` 填入接收推送的 QQ 群号。
5. **重启双端程序**。
6. **测试验证**：
   - 在 QQ 群内发送 `info` → 验证命令下发鉴权正常。
   - 在游戏内输入 `.ac 测试消息` → 验证推送上报鉴权正常，QQ 群收到消息，同时游戏内管理员看到广播。
