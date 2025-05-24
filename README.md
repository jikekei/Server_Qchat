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
