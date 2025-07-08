# 简化版服务器人数查询机器人

这是一个 **简化部署版本**，只保留了 **核心的服务器人数查询功能**，方便快速上手。

## ✨ 功能简介

- 基于 OneBot 协议的 QQ 机器人功能  
- 支持通过服务器 ID 查询玩家人数  
- 国内高速低延迟 API 接入  
- 查询结果自动缓存 1 分钟

---

## 🚀 快速开始

### 1. 登录 QQ

推荐使用 [NapCatQQ](https://github.com/NapNeko/NapCatQQ) 框架（支持 OneBot 协议），当然也可使用其他兼容 OneBot 协议的框架。

登录方法请参考 NapCatQQ 官方文档：[NapCatQQ 登录教程](https://github.com/NapNeko/NapCatQQ)

#### 修改配置文件启用 WebSocket 功能：

编辑 `config/onebot11_你的QQ号` 中的配置项：

```json
"ws": {
  "enable": true,
  "host": "127.0.0.1",
  "port": 6700,
  "reverseWs": {
    "enable": true,
    "urls": [
      ...
    ]
  }
}
```

确保 WebSocket 功能已启用，并使用端口 `6700`。

---

### 2. 启动本项目

运行 `SocketServer-GoHttpQQBOT` 后，会提示输入服务器 ID，用 `*` 分隔多个 ID，例如：

```
11*22*33
```

**服务器 ID 可前往 [服务器列表](https://scp.manghui.net/list/) 查看（即 srvID 栏）**

---

## 📡 API 状态监控

---

## 📄 许可证

本项目遵循 **[GNU GPL v3.0 License](https://www.gnu.org/licenses/gpl-3.0.html)** 开源协议。请严格遵守协议条款。
