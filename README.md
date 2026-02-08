# Server_Qcha

把 QQ 群里的指令转发到你的 **SCP: Secret Laboratory (SCPSL)** 服务器，实现查询在线、广播、封禁、重启回合等联动。

仓库结构（已规范化命名）：

- `server/`：SCPSL 服务器端 EXILED 插件（TCP 命令服务端）
- `bot/`：QQ 机器人（.NET 8），连接 OneBot 11 的正向 WebSocket，并通过 TCP 控制 `server/` 插件

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

配置字段名通常叫 `websocketServers`，里面会有 `host/port/token`。 citeturn0open2

---

## 第 2 步：安装服务器端插件（server）

### 2.1 releases下载插件

releases下载插件

### 2.2 放到 EXILED 插件目录

把 `Server_Qcha.dll` 放进 EXILED 插件目录（示例）：

- Windows：`...\EXILED\Plugins\`
- Linux：`~/.config/EXILED/Plugins/`

重启服务器，让插件加载一次并生成配置。

### 2.3 配置插件监听端口

在 EXILED 的插件配置里找到本插件配置项，设置：

- `TcpPort`：默认 `10087`
- `IP`：同机一般用 `127.0.0.1`
- `ServerName`：显示用名字，比如 `1服`

---

## 第 3 步：运行 QQ 机器人（bot）

### 3.1 创建本地配置文件

复制示例配置：

- 从：`bot/src/Server_Qcha.Bot/appsettings.Example.json`
- 到：`bot/src/Server_Qcha.Bot/appsettings.Local.json`

然后编辑 `appsettings.Local.json`：

1. NapCatQQ 的正向 WS 地址：
   - `GoCqHttp:WsBaseUri` = `ws://127.0.0.1:6700`
2. 服务器端插件 TCP 地址/端口：
   - `SocketServer:Host` = `127.0.0.1`
   - `SocketServer:Ports` = `[10087]`

> `appsettings.Local.json` 已在 `.gitignore` 中忽略，不会提交到 GitHub。

### 3.2 启动机器人

```powershell
dotnet run --project bot/src/Server_Qcha.Bot -c Release
```

保持窗口不要关闭。

---

## 第 4 步：在 QQ 群里怎么用

把 NapCatQQ 登录的 QQ 号拉进群，然后在群里发送：

- `help`：显示帮助
- `cx`：查询所有服务器在线人数
- `info`：查询服务器信息
- `#1`：查看第 1 个服务器玩家列表（`#2`、`#3` 同理）

管理指令（需要群管理员/群主权限）：

- `/bc 1 内容`：向第 1 个服务器广播
- `/round 1`：重启第 1 个服务器回合
- `/ban 1 <ID> <时间> <原因>`：封禁

---

## 常见问题（小白排错）

1. 机器人没反应
   - NapCatQQ 是否成功登录
   - NapCatQQ 的正向 WS 是否开启，端口是否是 6700
2. 机器人连接 WS 失败
   - `GoCqHttp:WsBaseUri` 写错
   - 端口被占用/防火墙拦截
3. 服务器不执行命令
   - `server/` 插件是否加载成功
   - 插件 `TcpPort` 是否和 `bot` 的 `SocketServer:Ports` 对得上

## 📬 联系方式

如在使用过程中遇到问题或有建议，欢迎联系作者：

- QQ : 3037240065  
- 📧 邮箱：[liseximt@outlook.com](mailto:liseximt@outlook.com)

---

## ✅ TODO 清单

- [ ] 完善数据库查询功能  
- [ ] 优化 API 兼容性  
- [ ] 增加控制台图形界面  
- [ ] 支持更多插件扩展  
- [ ] 提供多语言支持（含英文国际化）  
- [ ] 完善管理指令 `/setadmin` 权限联动功能  

---

感谢使用 **Server_Qchat**！  
如果你觉得这个项目对你有帮助，欢迎点个 ⭐️Star 支持一下！

