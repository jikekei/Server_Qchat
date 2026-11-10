<div align="center">

<img src="../../../assets/readme/doc-guides-en.svg" width="100%" alt="Server_Qcha documentation: Operating guides.">

</div>

[Documentation index](../../../docs/README.md) · [Project homepage](../../../README.md) · [简体中文](../../zh-CN/guides/getting-started.md)

# Server_Qcha — Getting Started and Deployment Guide

[English documentation index](../README.md) · [简体中文](../../zh-CN/guides/getting-started.md) · [Project homepage](../../../README.md)

This guide walks through the initial setup, configuration, and operation of Server_Qcha (Qridge). The project uses a decoupled daemon so that updating or restarting the bot does not interrupt game servers supervised by that daemon.

## Table of contents

- [Requirements](#requirements)
- [Architecture](#architecture)
- [Deployment](#deployment)
- [Launch scripts](#launch-scripts)
- [Deployment scenarios](#deployment-scenarios)

## Requirements

| Component | Requirement | Notes |
| --- | --- | --- |
| Operating system | Windows 10/11, Windows Server 2016+, or Linux x64 | Windows or a Linux container is recommended |
| .NET | .NET 8 runtime or SDK | Required to run the bot and daemon |
| Game server | SCP: Secret Laboratory Dedicated Server | EXILED plugin uses the repository's EXILED 9.5.0 dependency; LabAPI plugin targets 1.1.7+ |
| QQ integration (optional) | NapCatQQ (OneBot 11) or the official QQ Bot platform | Only needed for QQ community integration |

## Architecture

Since v2.0, Server_Qcha uses a decoupled standalone daemon:

1. **`Server_Qcha.Daemon.exe`** supervises `SCPSL.exe`, maintains native console pipes and heartbeats, and recovers from crashes. It is designed to stay resident with a small memory footprint.
2. **`Server_Qcha.Bot.exe`** hosts the web panel on port 8080 by default and runs either QQ bot engine. It manages the daemon through an internal HTTP API.

The daemon and its managed game servers remain online when the bot is upgraded, restarted, or exits unexpectedly.

## Deployment

### 1. Download the release

From [GitHub Releases](https://github.com/jikekei/Server_Qchat/releases/latest), download:

- `Server_Qcha.Bot-v2.0.1.zip` — Bot, daemon, web assets, and launch scripts.
- `Server_Qcha-EXILED.dll` — for servers using EXILED.
- `Server_Qcha-LabAPI.dll` — for servers using LabAPI.

Extract the bundle to a directory such as `C:\Server_Qcha\`. Before upgrading, back up and retain `data/` and `appsettings.json`.

### 2. Configure the application

Copy `appsettings.Example.json` to `appsettings.Local.json`, or edit `appsettings.json`:

```powershell
Copy-Item appsettings.Example.json appsettings.Local.json
```

On Linux:

```bash
cp appsettings.Example.json appsettings.Local.json
```

Set the paths and tokens for your installation. For example:

```json
{
  "WebPanel": { "Enabled": true, "Host": "127.0.0.1", "Port": 8080 },
  "LocalAdmin": {
    "Enabled": true,
    "Mode": "Daemon",
    "DaemonUri": "http://127.0.0.1:10090",
    "DaemonToken": "replace-with-a-random-secret",
    "AutoStartDaemon": true,
    "Servers": [{
      "Id": "main",
      "Name": "Main server",
      "ExecutablePath": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\SCP Secret Laboratory Dedicated Server\\SCPSL.exe",
      "GamePort": 7777,
      "AutoStart": false
    }]
  },
  "SocketServer": {
    "Host": "127.0.0.1",
    "Ports": [10087],
    "NotificationHost": "127.0.0.1",
    "NotificationPort": 10088,
    "AuthToken": "SetYourCustomTokenHere"
  }
}
```

`SocketServer:AuthToken` is shared with the game plugin and must be replaced with a strong random value. The example binds the web panel and notification listener to loopback. To access the panel remotely or connect across machines, bind the relevant listener to a reachable interface (often `0.0.0.0`) and restrict sources with a firewall. `0.0.0.0` listens on every interface.

### 3. Start the services and sign in

**Recommended: start both processes** by running `一键启动(守护+机器人).bat`. It opens the daemon and bot consoles in dependency order.

Alternatively, run `Server_Qcha.Bot.exe`. When `AutoStartDaemon` is enabled, the bot checks for the daemon and starts it in the background.

On first initialization the console prints the panel URL, username, and initial administrator password. Open `http://127.0.0.1:8080/` in a browser. The startup log also displays detected local IPv4 addresses; a LAN address is reachable only when `WebPanel:Host` allows access on that interface. Sign in with the printed credentials, then change the password in account management. The password is not reset on later starts unless the reset option is enabled.

Open the **Server Processes** page to inspect daemon memory, PID, uptime, and managed SCPSL servers, and to start, stop, or manage them.

### 4. Install one game-server plugin

Choose the plugin framework used by your server.

**EXILED**

1. Rename `Server_Qcha-EXILED.dll` to `Server_Qcha.dll`.
2. Install it in `%APPDATA%\EXILED\Plugins\` on Windows or `~/.config/EXILED/Plugins/` on Linux.
3. Start the server once and configure the generated file. Match the `auth_token` to the bot's `SocketServer:AuthToken`:

```yaml
tcp_port: 10087
bot_ip: "127.0.0.1"
bot_port: 10088
auth_token: "SetYourCustomTokenHere"
```

**LabAPI**

1. Rename `Server_Qcha-LabAPI.dll` to `Server_Qcha.dll`.
2. Install it under `%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<game-port>\Server_Qcha.dll`.
3. Start the server once and configure the generated `config.yml` with the matching ports and token.

Do not load both plugin variants on the same server instance.

## Launch scripts

| Script | Purpose | Typical use |
| --- | --- | --- |
| `一键启动(守护+机器人).bat` | Starts the daemon, then the bot | Normal operation |
| `启动守护进程(Daemon).bat` | Starts only the daemon | Keep game servers online without the web panel |
| `停止守护进程.bat` | Requests a safe daemon shutdown | Maintenance or full shutdown |

## Deployment scenarios

### Single-machine deployment (recommended)

Run the game server, daemon, and bot on the same host. Keep the bot's `SocketServer:Host` and the plugin's `bot_ip` at `127.0.0.1`. Use loopback for the panel and notification listener when local access is sufficient. The bot can then be updated or restarted without disconnecting players.

### Distributed multi-machine deployment

Run the bot on a management host and a daemon beside each group of game servers. Configure each game plugin's `bot_ip` to the management host and `bot_port` to its notification port (10088 by default). Allow the notification port into the management host and each plugin's `tcp_port` into the respective game host. Configure matching tokens on both sides. The protocol uses HMAC authentication but does not encrypt traffic; connect hosts through a VPN or restrict source addresses with firewalls. Empty or legacy-default tokens trigger warnings but do not block startup for compatibility; replace them before deployment.

Copyright 2025 hmyhserver.top
