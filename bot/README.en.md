# Server_Qcha.Bot

[简体中文](README.md) · [English](README.en.md)

The bot connects QQ communities to SCP: Secret Laboratory servers. It includes the Web operations panel and can manage game servers through the standalone `Server_Qcha.Daemon` process.

## Features

- Connect through NapCat (OneBot 11) or the official QQ Bot OpenAPI v2 integration.
- Query online players and server status, and send supported game-server commands from QQ.
- Switch bot integrations from the Web panel.
- Manage accounts, server settings, daemon state, and audit records in the browser.
- Optionally query player history and statistics from a separately configured MySQL database.

## Requirements

- .NET 8 SDK for development, or the .NET 8 ASP.NET Core Runtime for the release bundle.
- A supported QQ connection: NapCat with a forward WebSocket endpoint, or an official QQ Bot application.
- A matching EXILED or LabAPI plugin installed on each managed SCP:SL server.
- Optional: MySQL for player binding and statistics.

## Quick start

1. Download the latest `Server_Qcha.Bot-v2.0.1.zip` bundle from [GitHub Releases](https://github.com/jikekei/Server_Qchat/releases/latest) and extract it.
2. Copy `appsettings.Example.json` to `appsettings.Local.json` and configure the QQ connection and game servers. Keep machine-specific settings and credentials in the local file or environment variables.
3. Set matching random authentication tokens in the bot and game plugin. If using a separate daemon, also set the same `LocalAdmin:DaemonToken` on the bot and daemon.
4. Start the daemon and bot with `一键启动(守护+机器人).bat`, or launch `Server_Qcha.Bot.exe`.
5. Open `http://127.0.0.1:8080/`. The initial administrator credentials are printed in the bot console.

The panel binds to loopback by default. For remote browser access, set `WebPanel:Host` to `0.0.0.0` and allow only trusted clients through your firewall. The startup log prints detected IPv4 panel addresses.

## Configuration

The main configuration is `appsettings.json`; use the ignored `appsettings.Local.json` for per-machine overrides. Environment variables can override settings using the `Section__Property` form, for example `MySql__ConnectionString`.

Set `Bot:AllowedGroupIds` to restrict which groups the bot handles, and configure `Bot:AdminUserIds` or `OfficialQq:AdminOpenIds` for explicit administrator allowlists. Empty administrator lists preserve legacy role-based behavior for compatibility, so configure these lists when strict separation is required.

Never commit tokens, passwords, API secrets, or database connection strings. Live database tests are skipped by default and obtain connection settings from environment variables.

## Build and test

```powershell
dotnet build Server_Qcha.Bot.sln -c Release
dotnet test Server_Qcha.Bot.sln -c Release
```

See the [Chinese bot guide](../docs/bot-guide.md), [configuration reference](../docs/configuration.md), and [English deployment guide](../docs/getting-started.en.md) for more detail.
