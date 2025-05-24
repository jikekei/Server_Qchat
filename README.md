# Server_Qchat

**Server_Qchat** 是一个桥接工具，可将 [SCP: Secret Laboratory](https://store.steampowered.com/app/700330/SCP_Secret_Laboratory/) 服务器连接至 QQ 群，支持多种实用的管理和互动功能。

---

## ✨ 功能概览

- **在线状态查询**
  - `cx`：查询服务器在线人数和管理。
  - `info`：查询服务器信息。
  - `#1` / `#xx`：查询服务器在线玩家列表。

- **智能关键词响应**
  - 包含“炸了？”或“服务器炸了？”的消息：自动返回服务器状态及延迟信息。
  - 包含“赞助”的消息：自动返回详细的赞助信息。

- **预览功能**
  - 控制台输入 `.ac`，通过 SocketServerAsync 自动同步消息到 QQ 群。
  - 支持数据库连接，可配合插件查询游玩时间、击杀数量等（功能仍在完善中）。
  - 提供 API 接口，可直接通过 HTTP 查询服务器信息，无需部署插件。

---

## 📦 使用说明

如果不希望使用 API，可选择使用本地插件方式。

👉 **推荐使用 API 接口：** [点击查看 API 使用文档](https://github.com/jikekei/Server_Qchat/blob/main/API%E8%B0%83%E7%94%A8%E7%89%88%E6%9C%AC.md)

---

### 1. 登录 QQ 机器人

推荐使用 [NapCatQQ](https://github.com/NapNeko/NapCatQQ) 框架，其他兼容 OneBot 协议的框架也可使用。

按照 NapCatQQ 的说明进行登录，并修改配置文件（路径：`config/onebot11_你的QQ号.json`）：

```json
"ws": {
  "enable": true,
  "host": "127.0.0.1",
  "port": 6700,
  "reverseWs": {
    "enable": true,
    "urls": [...]
  }
}


确保 ws 功能开启，监听端口为 6700。

2. 安装 CX 查询插件
下载 CX 查询插件。

将插件放入 Exiled 的插件目录中。

按照说明修改插件配置文件，以适配你的服务器环境。

3. 启动中继程序：Server_Qchat_exe
本程序基于 .NET 6.0 框架开发。

下载并安装 .NET 6.0 运行时

若程序运行时出现闪退，可尝试安装运行库合集：
点击下载（52pojie论坛）

程序启动后输入：

服务器端口号

QQ 群号

即可完成接入。

❓ 常见问题
Q: 程序闪退怎么办？
A: 请确认已安装 .NET 6.0 运行库，如仍失败，尝试运行库合集。

Q: 插件未响应指令？
A: 检查插件是否正确放置、是否与服务器版本兼容，并确认配置文件无误。

Q: API 不工作？
A: 检查端口占用情况，确认未被防火墙拦截，并重启程序。

📬 联系方式
如在使用过程中遇到问题或有建议，欢迎联系作者：

QQ：3037240065

邮箱：liseximt@outlook.com

✅ TODO 清单
 完善数据库查询功能

 优化 API 兼容性

 增加控制台图形界面

 支持更多插件扩展

 提供多语言支持（含英文国际化）

感谢使用 Server_Qchat！
如果你觉得这个项目对你有帮助，欢迎点个 ⭐Star 支持一下！
