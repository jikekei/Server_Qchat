# 快速上手与全平台部署指南

本文档将引导您在 5 分钟内完成 **Server_Qcha (Qridge) v2.0** 的基础环境搭建、配置与运行。

---

## 目录
- [运行环境要求](#运行环境要求)
- [核心架构组件说明](#核心架构组件说明)
- [快速部署流程](#快速部署流程)
  - [第一步：获取程序包](#第一步获取程序包)
  - [第二步：配置主程序](#第二步配置主程序)
  - [第三步：启动服务与登录 Web 面板](#第三步启动服务与登录-web-面板)
  - [第四步：安装游戏服务端插件](#第四步安装游戏服务端插件)
- [启动方式与脚本推荐](#启动方式与脚本推荐)
- [典型部署架构场景](#典型部署架构场景)
  - [单机同机部署（推荐方案）](#单机同机部署推荐方案)
  - [分布式多机集群部署](#分布式多机集群部署)

---

## 运行环境要求

在开始部署前，请确保您的机器满足以下基础环境：

| 组件 | 要求 | 说明 |
|---|---|---|
| **操作系统** | Windows 10/11 / Windows Server 2016+ 或 Linux x64 | 推荐 Windows 或 Linux 容器 |
| **.NET 环境** | .NET 8.0 Runtime 或 SDK | 运行主程序与独立守护进程所必需 |
| **SCPSL 服务端** | SCP: Secret Laboratory Dedicated Server | 支持 EXILED 8+ 或 LabAPI 1.1+ |
| **QQ 框架（可选）** | NapCatQQ (OneBot 11) 或 QQ 官方机器人开放平台 | 仅在使用社群机器人联动时需要 |

---

## 核心架构组件说明

在 v2.0 版本中，Server_Qcha 采用了**解耦的独立守护架构**：

1. **`Server_Qcha.Daemon.exe`（独立守护进程）**：
   - 负责托管 `SCPSL.exe` 游戏服进程生命周期；
   - 建立原生控制台管道 TCP 长连接、维持心跳状态机与崩溃重启自愈；
   - **特点**：轻量、低内存占用、高可用常驻。
2. **`Server_Qcha.Bot.exe`（Web 面板与社群机器人）**：
   - 提供 Web 管理后台服务（默认端口 8080）与 QQ 机器人双引擎（NapCat / 官方）；
   - 通过内部安全 HTTP 接口管理守护进程；
   - **核心优势**：即使 Bot 进行代码升级、重启或异常退出，**守护进程和游戏服依然在线，玩家零掉线！**

---

## 快速部署流程

### 第一步：获取程序包

前往项目的 [Releases 发布页面](https://github.com/jikekei/Server_Qchat/releases) 下载最新版本的发行包：
- `Server_Qcha.Bot-v2.0.0.zip`（完整发行包，包含 Bot、Daemon、Web 静态资源与启动脚本）
- `Server_Qcha-EXILED.dll`（若您的游戏服使用 EXILED 框架）
- `Server_Qcha-LabAPI.dll`（若您的游戏服使用 LabAPI 框架）

解压 `Server_Qcha.Bot-v2.0.0.zip` 到任意英文目录，例如 `C:\Server_Qcha\`。

---

### 第二步：配置主程序

进入解压后的程序目录：
1. 复制配置文件模板 `appsettings.Example.json` 并重命名为 `appsettings.Local.json`（或直接修改 `appsettings.json`）：
   - Windows PowerShell 命令：
     ```powershell
     Copy-Item appsettings.Example.json appsettings.Local.json
     ```
   - Linux Bash 命令：
     ```bash
     cp appsettings.Example.json appsettings.Local.json
     ```

2. 用文本编辑器打开 `appsettings.Local.json`，根据实际情况修改关键字段：
   ```json
   {
     "WebPanel": {
       "Enabled": true,
       "Host": "0.0.0.0",
       "Port": 8080
     },
     "LocalAdmin": {
       "Enabled": true,
       "Mode": "Daemon",
       "DaemonUri": "http://127.0.0.1:10090",
       "DaemonToken": "QchaSecret_123",
       "AutoStartDaemon": true,
       "Servers": [
         {
           "Id": "main",
           "Name": "1号主服",
           "ExecutablePath": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\SCP Secret Laboratory Dedicated Server\\SCPSL.exe",
           "GamePort": 7777,
           "AutoStart": false
         }
       ]
     },
     "SocketServer": {
       "Host": "127.0.0.1",
       "Ports": [ 10087 ],
       "NotificationHost": "0.0.0.0",
       "NotificationPort": 10088,
       "AuthToken": "SetYourCustomTokenHere"
     }
   }
   ```
   > **安全提示**：`SocketServer:AuthToken` 为双向通信安全鉴权密钥，请设置为自定义强密码，稍后在游戏服务端插件中需配置完全相同的值。

---

### 第三步：启动服务与登录 Web 面板

针对不同运维习惯，系统提供灵活的启动方式：

#### 推荐启动方式
- **方式一：一键双击启动（最便捷）**：
  直接双击运行目录下的 **`一键启动(守护+机器人).bat`**。
  脚本会先后拉起守护进程窗口与机器人控制台窗口。
- **方式二：免脚本自拉起**：
  直接双击运行 **`Server_Qcha.Bot.exe`**。
  因为默认配置了 `"AutoStartDaemon": true`，Bot 会自动探测并在后台静默拉起守护进程，开箱即用！

#### 获取初始管理员密码
首次启动时，控制台将自动打印类似如下的信息：
```text
===== Web 面板 Qridge =====
  登录地址 : http://<本机IP>:8080/
  用户名   : admin
  密　码   : aB3#dE9$kL
  （密码每次启动随机生成；首次登录后请在「账号管理」中修改）
=========================================
```

#### 访问 Web 控制面板
1. 使用浏览器打开 `http://127.0.0.1:8080/`（远程访问时替换为服务器公网或内网 IP）；
2. 输入用户名 `admin` 与控制台打印的初始密码登录；
3. 进入「服务器进程」页面，即可查看守护进程的实时监控（内存、PID、存活时长），并自由启停与管理 SCPSL 游戏服！

---

### 第四步：安装游戏服务端插件

根据您的游戏服框架，选择以下任一方式安装：

#### 方式 A：EXILED 框架
1. 将下载的 `Server_Qcha-EXILED.dll` 重命名为 `Server_Qcha.dll`；
2. 放置到 EXILED 插件目录：
   - Windows: `%APPDATA%\EXILED\Plugins\`
   - Linux: `~/.config/EXILED/Plugins/`
3. 启动一次服务器生成配置，并在生成的配置文件中修改：
   ```yaml
   tcp_port: 10087
   bot_ip: "127.0.0.1"
   bot_port: 10088
   auth_token: "SetYourCustomTokenHere" # 务必与 appsettings.Local.json 保持一致
   ```

#### 方式 B：LabAPI 框架
1. 将下载的 `Server_Qcha-LabAPI.dll` 重命名为 `Server_Qcha.dll`；
2. 放置到 LabAPI 插件目录：
   ```text
   %APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<游戏端口>\Server_Qcha.dll
   ```
3. 启动一次服务器生成配置，并在生成的 `config.yml` 中配置相同的端口与 `auth_token`。

---

## 启动方式与脚本推荐

发行包中附带了实用的管理脚本：

| 脚本名称 | 用途 | 适用场景 |
|---|---|---|
| **`一键启动(守护+机器人).bat`** | 依次拉起独立守护进程与 Bot 控制台 | 日常开机、全面启动运维环境 |
| **`启动守护进程(Daemon).bat`** | 仅拉起守护进程（常驻维护游戏服） | 只需要保持游戏服运行，不需要开启 Web 面板时 |
| **`停止守护进程.bat`** | 一键安全终止后台所有守护进程 | 完全关机、维护停服或整体退出时 |

---

## 典型部署架构场景

### 单机同机部署（推荐方案）
- SCPSL 游戏服务端、`Server_Qcha.Daemon` 与 `Server_Qcha.Bot` 运行在同一台物理机或云服务器上；
- `SocketServer:Host` 与插件 `bot_ip` 均设置为 `127.0.0.1`；
- 所有通信走本地回环，无需在系统防火墙开放游戏与机器人间的内部通信端口；
- 机器人随时更新或重启，游戏服绝不掉线。

### 分布式多机集群部署
- `Server_Qcha.Bot` 部署在独立管理机上，各游戏服分布在多台独立游戏宿主机上；
- 各宿主机上分别运行独立的 `Server_Qcha.Daemon`；
- 各游戏服插件配置中的 `bot_ip` 指向管理机 IP，`bot_port` 指向管理机的 `NotificationPort`（默认 10088）；
- 管理机防火墙开放 `10088` 端口入站权限，游戏宿主机防火墙开放对应 `tcp_port`；
- 双方强制使用相同的 `AuthToken` 进行端到端加密鉴权。

---

Copyright 2025 hmyhserver.top
