# Server_Qcha — Getting Started

[简体中文](getting-started.md) · [English](getting-started.en.md) · [Project overview](../README.md)

This guide covers the release bundle, basic configuration, startup, and plugin installation for Server_Qcha (Qridge), a web control panel, standalone SCP:SL server supervisor, and QQ bot integration.

## Requirements

| Component | Requirement |
| --- | --- |
| Operating system | Windows 10/11, Windows Server 2016+, or Linux x64 |
| Runtime | .NET 8 ASP.NET Core Runtime for the release bundle; .NET 8 SDK for building from source |
| Game server | SCP: Secret Laboratory Dedicated Server |
| QQ integration | Optional: NapCat with OneBot 11, or an official QQ Bot application |
| Game plugin | Install either the EXILED or LabAPI build that matches your server |

## Download and upgrade

Download the latest `Server_Qcha.Bot-v2.0.1.zip` bundle and the plugin matching your server framework from [GitHub Releases](https://github.com/jikekei/Server_Qchat/releases/latest). Extract the bundle to a deployment directory.

**Before upgrading, back up `data/` and `appsettings.json`.** Preserve these files when replacing program files so that panel accounts, server definitions, and local settings remain available.

## Configure the bot and daemon

Copy `appsettings.Example.json` to `appsettings.Local.json` and edit the local copy. Keep passwords, API secrets, and other machine-specific settings out of tracked files. The application also supports standard .NET environment-variable overrides such as `MySql__ConnectionString`.

Configure the following values for your deployment:

- QQ connection details for NapCat/OneBot or the official QQ Bot API.
- `WebPanel:Host` and `WebPanel:Port`. The default is loopback on port `8080`. To allow remote clients, bind to `0.0.0.0` and restrict access with a firewall.
- One or more `LocalAdmin:Servers` entries, including the executable path and working directory for each game server.
- A random `SocketServer:AuthToken` shared with the selected game plugin.
- If using a separate daemon, a random `LocalAdmin:DaemonToken` shared by the bot and daemon.
- Explicit bot administrator and allowed-group lists where access should be restricted.

Empty or legacy-default tokens produce a startup security warning and retain compatibility behavior. Set strong random values before exposing services. The TCP protocol authenticates requests but does not encrypt traffic; use a VPN or firewall allowlist between machines.

## Start and sign in

Run `一键启动(守护+机器人).bat` to start the daemon and bot, or start `Server_Qcha.Bot.exe` directly. When automatic daemon startup is enabled, the bot can start the daemon for you.

Open `http://127.0.0.1:8080/` on the host. On first initialization, the bot console prints the administrator login credentials and the detected panel address. Remote browser access requires a non-loopback `WebPanel:Host` setting and firewall access.

## Install a game plugin

Choose one plugin framework for each server instance:

- **EXILED**: Install `Server_Qcha-EXILED.dll` in the EXILED plugin directory. The current project references target EXILED 9.5.0.
- **LabAPI**: Install `Server_Qcha-LabAPI.dll` in the per-port LabAPI plugin directory, or its global plugin directory. The current project references target LabAPI 1.1.7.

Configure the plugin's `auth_token` to match the bot's `SocketServer:AuthToken`. By default, the plugin listens on `127.0.0.1:10087` for commands and connects to the bot on `127.0.0.1:10088` for notifications. For split-machine deployments, use reachable addresses and firewall restrictions.

Do not load both EXILED and LabAPI versions on the same server instance. See the [EXILED README](../server/README.en.md), [LabAPI README](../server-labapi/README.en.md), and [Chinese configuration reference](configuration.md).

## Launch scripts

- `一键启动(守护+机器人).bat`: Starts the daemon and bot.
- `启动守护进程(Daemon).bat`: Starts only the daemon.
- `停止守护进程.bat`: Requests a safe daemon shutdown.

For deeper feature and troubleshooting guides, see the [Chinese Web panel guide](web-panel.md), [bot guide](bot-guide.md), [plugin guide](plugin-guide.md), and [FAQ](faq.md).
