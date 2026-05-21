# Server_Qcha

把 QQ 群里的指令转发到你的 **SCP: Secret Laboratory (SCPSL)** 服务器，实现查询在线、广播、封禁、重启回合等联动。同时支持游戏内玩家通过 `.ac` 指令将消息**实时推送至 QQ 群**，双向通信全程鉴权加密。

仓库结构（已规范化命名）：

- `server/`：SCPSL 服务器端 EXILED 插件（TCP 命令服务端 + 主动推送客户端）
- `bot/`：QQ 机器人（.NET 8），连接 OneBot 11 的正向 WebSocket，并通过 TCP 控制 `server/` 插件

---

## 🆕 v1.2.0 更新亮点

- **游戏内 `.ac` 推送指令**：玩家在游戏控制台输入 `.ac <消息>`，即可实时推送到指定 QQ 群
- **管理员同步广播**：`.ac` 触发时，所有在线管理员屏幕同步显示广播提示
- **双向通信鉴权（AuthToken）**：所有 TCP 通信强制 Token 校验，防止未授权访问与伪造推送
- **独立通知监听服务**：机器人端新增独立端口（默认 `10088`）接收游戏服推送

---

## 🚀 小白一键教程（照着做就能跑）

👉 **推荐优先使用 API 接口版本：**  
[📖 点击查看 API 使用文档](https://github.com/jikekei/Server_Qchat/blob/main/API%E8%B0%83%E7%94%A8%E7%89%88%E6%9C%AC.md)  
不希望使用 API 的话，再选择本 README 的「本地插件方式」。

---

### 0. 你需要准备

- 一台能跑 **SCPSL Dedicated Server** 的机器
- 已安装 **EXILED**（服务器端需要）
- 一个能登录 QQ 的环境（同一台机器也可以）
- 安装 **.NET 8 SDK**（只给 `bot/` 用）

---

## 第 1 步：安装 QQ 框架（推荐 NapCatQQ）

推荐使用 NapCatQQ（OneBot 11）：

```text
https://github.com/NapNeko/NapCatQQ
```

你要做的事情只有两件：

1. 按 NapCatQQ 官方文档安装并登录 QQ
2. 在 OneBot 11 配置里开启 **WebSocket 服务端（正向 WS）**，例如开在 `127.0.0.1:6700`

配置字段名通常叫 `websocketServers`，里面会有 `host/port/token`。

---

## 第 2 步：安装服务器端插件（server）

### 2.1 releases下载插件

releases下载插件

### 2.2 放到 EXILED 插件目录

把 `Server_Qcha.dll` 放进 EXILED 插件目录（示例）：

- Windows：`...\EXILED\Plugins\`
- Linux：`~/.config/EXILED/Plugins/`

重启服务器，让插件加载一次并生成配置。

### 2.3 配置插件

在 EXILED 的插件配置里找到本插件配置项，设置：

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `tcp_port` | `10087` | TCP 命令监听端口 |
| `i_p` | `127.0.0.1` | TCP 命令监听 IP |
| `server_name` | `1服` | 显示用名字 |
| `bot_i_p` | `127.0.0.1` | 机器人通知监听服务的 IP |
| `bot_port` | `10088` | 机器人通知监听服务的端口 |
| `auth_token` | `QchaSecret_123` | 🔒 安全验证 Token，须与机器人端一致 |

> ⚠️ **安全提醒**：部署前请务必修改 `auth_token` 的默认值！

---

## 第 3 步：运行 QQ 机器人（bot）

### 3.1 创建本地配置文件

复制示例配置：

- 从：`bot/src/Server_Qcha.Bot/appsettings.Example.json`
- 到：`bot/src/Server_Qcha.Bot/appsettings.Local.json`

然后编辑 `appsettings.Local.json`：

```json
{
  "GoCqHttp": {
    "WsBaseUri": "ws://127.0.0.1:6700"
  },
  "SocketServer": {
    "Host": "127.0.0.1",
    "Ports": [ 10087 ],
    "NotificationHost": "0.0.0.0",
    "NotificationPort": 10088,
    "AuthToken": "YourSecretTokenHere"
  },
  "Bot": {
    "AllowedGroupIds": [ 123456789 ],
    "NotifyGroupIds": [ 123456789 ],
    "AcTargetGroupId": 123456789
  }
}
```

**关键配置说明：**

| 配置项 | 说明 |
|--------|------|
| `GoCqHttp:WsBaseUri` | NapCatQQ 的正向 WS 地址 |
| `SocketServer:Host` | 游戏服务端插件 TCP 地址 |
| `SocketServer:Ports` | 游戏服务端插件 TCP 端口（多服就写多个） |
| `SocketServer:NotificationHost` | 通知监听服务绑定 IP（`0.0.0.0` = 所有网卡） |
| `SocketServer:NotificationPort` | 通知监听服务端口 |
| `SocketServer:AuthToken` | 🔒 安全验证 Token，须与游戏服端一致 |
| `Bot:AcTargetGroupId` | 接收 `.ac` 推送的目标 QQ 群号 |

> `appsettings.Local.json` 已在 `.gitignore` 中忽略，不会提交到 GitHub。

### 3.2 启动机器人

```powershell
dotnet run --project bot/src/Server_Qcha.Bot -c Release
```

保持窗口不要关闭。

---

## 第 4 步：在 QQ 群里怎么用

把 NapCatQQ 登录的 QQ 号拉进群，然后在群里发送：

### 普通指令（所有人可用）

| 指令 | 说明 |
|------|------|
| `help` | 显示帮助 |
| `cx` | 查询所有服务器在线人数 |
| `info` | 查询服务器详细信息 |
| `#1` | 查看第 1 个服务器玩家列表（`#2`、`#3` 同理） |
| `/bd <Steam64>` | 绑定 QQ 到 Steam64 |
| `/me` | 查询自己绑定的玩家数据 |

### 管理指令（需要群管理员/群主权限）

| 指令 | 说明 |
|------|------|
| `/bc 1 内容` | 向第 1 个服务器广播 |
| `/round 1` | 重启第 1 个服务器回合 |
| `/ban 1 <ID> <时间> <原因>` | 封禁玩家 |

---

## 第 5 步：游戏内 `.ac` 推送（新功能）

玩家在游戏中按 `~` 打开控制台，输入：

```
.ac 你想推送到QQ群的消息内容
```

**效果：**
- ✅ QQ 群内立刻收到推送消息（格式：`来自服务器 [1服]: 玩家 [昵称] 发送了：内容`）
- ✅ 当前对局所有在线管理员屏幕上显示 10 秒广播提示
- 🔒 通信全程经过 AuthToken 鉴权，防止伪造

---

## 🔒 安全：通信鉴权说明

本项目为所有 TCP 通信提供了**双向 Token 鉴权**机制：

- **QQ 群 → 游戏服**（查询/广播/踢人等指令）：机器人发送命令时附带 Token，游戏服验证通过后才执行
- **游戏服 → QQ 群**（`.ac` 推送）：游戏服推送时附带 Token，机器人验证通过后才转发到群

**使用方法：**
1. 在游戏服端和机器人端分别配置**完全相同**的 `AuthToken`
2. Token 留空（`""`）则自动回退为无鉴权模式（不推荐用于生产环境）

> ⚠️ 默认 Token `QchaSecret_123` 仅供演示，**请务必修改为自定义强密码！**

---

## 常见问题（小白排错）

1. **机器人没反应**
   - NapCatQQ 是否成功登录
   - NapCatQQ 的正向 WS 是否开启，端口是否是 6700
2. **机器人连接 WS 失败**
   - `GoCqHttp:WsBaseUri` 写错
   - 端口被占用/防火墙拦截
3. **服务器不执行命令**
   - `server/` 插件是否加载成功
   - 插件 `tcp_port` 是否和 `bot` 的 `SocketServer:Ports` 对得上
4. **指令返回 Unauthorized**
   - 游戏服端和机器人端的 `AuthToken` 不一致
   - 请确保两边配置完全相同的 Token
5. **`.ac` 推送没有到达 QQ 群**
   - 检查机器人端 `Bot:AcTargetGroupId` 是否已配置群号
   - 检查游戏服端 `bot_i_p` 和 `bot_port` 是否能连通机器人的通知监听端口
   - 检查防火墙是否放行了 `10088` 端口

---

## 📬 联系方式

如在使用过程中遇到问题或有建议，欢迎联系作者：

- QQ : 3037240065  
- 📧 邮箱：[liseximt@outlook.com](mailto:liseximt@outlook.com)

---

## ✅ TODO 清单

- [x] 游戏内 `.ac` 实时推送到 QQ 群
- [x] 双向通信鉴权（AuthToken）
- [x] 管理员同步广播
- [ ] 完善数据库查询功能  
- [ ] 优化 API 兼容性  
- [ ] 增加控制台图形界面  
- [ ] 支持更多插件扩展  
- [ ] 提供多语言支持（含英文国际化）  
- [ ] 完善管理指令 `/setadmin` 权限联动功能  

---

感谢使用 **Server_Qchat**！  
如果你觉得这个项目对你有帮助，欢迎点个 ⭐️Star 支持一下！
