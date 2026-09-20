# 快速上手与部署指南

本文档将引导您在 5 分钟内完成 **Server_Qcha (Qridge)** 的基础环境搭建、配置与运行。

---

## 目录
- [运行环境要求](#运行环境要求)
- [快速部署流程](#快速部署流程)
  - [第一步：获取程序包](#第一步获取程序包)
  - [第二步：配置主程序](#第二步配置主程序)
  - [第三步：启动主服务并登录 Web 面板](#第三步启动主服务并登录-web-面板)
  - [第四步：安装游戏服务端插件](#第四步安装游戏服务端插件)
- [典型部署架构场景](#典型部署架构场景)
  - [单机同机部署（推荐初学者）](#单机同机部署推荐初学者)
  - [分布式多机部署（多服集群）](#分布式多机部署多服集群)

---

## 运行环境要求

在开始部署前，请确保您的机器满足以下基础环境：

| 组件 | 要求 | 说明 |
|---|---|---|
| **操作系统** | Windows 10/11 / Windows Server 2016+ 或 Linux x64 | 支持跨平台运行 |
| **.NET 环境** | .NET 8.0 Runtime 或 SDK | 运行主程序所必需 |
| **SCPSL 服务端** | SCP: Secret Laboratory Dedicated Server | 支持 EXILED 8+ 或 LabAPI 1.1+ |
| **QQ 框架（可选）** | NapCatQQ / LLOneBot / go-cqhttp | 仅在使用 QQ 机器人联动时需要 |

---

## 快速部署流程

### 第一步：获取程序包

前往项目的 [Releases 发布页面](https://github.com/jikekei/Server_Qchat/releases) 下载最新版本的发行包：
- `Server_Qcha.Bot-vX.X.X.zip`（主程序与 Web 控制面板静态资源）
- `Server_Qcha-EXILED.dll`（若您的游戏服使用 EXILED 框架）
- `Server_Qcha-LabAPI.dll`（若您的游戏服使用 LabAPI 框架）

解压 `Server_Qcha.Bot-vX.X.X.zip` 到任意目录，例如 `C:\Server_Qcha\`。

---

### 第二步：配置主程序

进入解压后的程序目录：
1. 复制配置文件模板 `appsettings.Example.json` 并重命名为 `appsettings.Local.json`：
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
     "SocketServer": {
       "Host": "127.0.0.1",
       "Ports": [ 10087 ],
       "NotificationHost": "0.0.0.0",
       "NotificationPort": 10088,
       "AuthToken": "SetYourCustomTokenHere"
     },
     "LocalAdmin": {
       "Enabled": true,
       "Servers": [
         {
           "Id": "main",
           "Name": "1号主服",
           "ExecutablePath": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\SCP Secret Laboratory Dedicated Server\\SCPSL.exe",
           "GamePort": 7777,
           "AutoStart": false
         }
       ]
     }
   }
   ```
   > 注意：`SocketServer:AuthToken` 为双向通信安全鉴权密钥，请设置为自定义强密码，稍后在游戏服务端插件中需配置完全相同的值。

---

### 第三步：启动主服务并登录 Web 面板

1. **启动程序**：
   - 在 Windows 上可直接双击运行 `Server_Qcha.Bot.exe`，或在命令行运行：
     ```powershell
     .\Server_Qcha.Bot.exe
     ```
   - 在 Linux 上运行：
     ```bash
     dotnet Server_Qcha.Bot.dll
     ```

2. **获取初始管理员密码**：
   - 首次启动时，控制台将自动打印类似如下的信息：
     ```text
     ===== Web 面板 Qridge =====
       登录地址 : http://<本机IP>:8080/
       用户名   : admin
       密　码   : aB3#dE9$kL
       （密码每次启动随机生成；首次登录后请在「账号管理」中修改）
     =========================================
     ```

3. **访问 Web 控制面板**：
   - 使用浏览器打开 `http://127.0.0.1:8080/`（远程访问时替换为服务器公网或内网 IP）；
   - 输入用户名 `admin` 与控制台打印的初始密码登录；
   - 建议登录后立即前往左侧导航的「账号管理」修改初始密码。

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
   auth_token: "SetYourCustomTokenHere" # 与 appsettings.Local.json 保持一致
   ```

#### 方式 B：LabAPI 框架
1. 将下载的 `Server_Qcha-LabAPI.dll` 重命名为 `Server_Qcha.dll`；
2. 放置到 LabAPI 插件目录：
   ```text
   %APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<游戏端口>\Server_Qcha.dll
   ```
3. 启动一次服务器生成配置，并在生成的 `config.yml` 中配置相同的端口与 `auth_token`。

---

## 典型部署架构场景

### 单机同机部署（推荐初学者）
- SCPSL 游戏服务端与 `Server_Qcha.Bot` 运行在同一台物理机或云服务器上；
- `SocketServer:Host` 与插件 `bot_ip` 均设置为 `127.0.0.1`；
- 所有通信走本地回环，无需在系统防火墙开放游戏与机器人间的内部通信端口。

### 分布式多机部署（多服集群）
- `Server_Qcha.Bot` 部署在独立管理机上，各游戏服分布在不同的服务器上；
- 各游戏服插件配置中的 `bot_ip` 指向管理机 IP，`bot_port` 指向管理机的 `NotificationPort`（默认 10088）；
- 管理机防火墙需开放 `10088` 端口入站权限，各游戏服防火墙需开放对应的 `tcp_port` 入站权限；
- 双方强制使用相同的 `AuthToken` 进行端到端加密鉴权。
