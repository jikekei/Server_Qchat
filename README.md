<div align="center">

<img src="./assets/readme/hero-en.svg" width="100%" alt="Server_Qcha (Qridge) — SCP:SL server operations in the browser; panel or bot restarts never drop players. The panel mock on the right shows a nine-module navigation list, online-player and daemon metric cards, a 24-hour player trend, and live console output.">

[English](README.md) · [简体中文](docs/zh-CN/README.md)

[![Release](https://img.shields.io/github/v/release/jikekei/Server_Qchat?color=blue&logo=github)](https://github.com/jikekei/Server_Qchat/releases)
[![CI](https://img.shields.io/github/actions/workflow/status/jikekei/Server_Qchat/ci.yml?label=CI)](https://github.com/jikekei/Server_Qchat/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](LICENSE)

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![SCPSL](https://img.shields.io/badge/Game-SCPSL-red?logo=steam&logoColor=white)](https://scpslgame.com/)
[![EXILED](https://img.shields.io/badge/Plugin-EXILED-blue)](https://github.com/Exiled-Team/EXILED)
[![LabAPI](https://img.shields.io/badge/Plugin-LabAPI-darkgreen)](https://github.com/northwood-studios/LabAPI)
[![Vue 3](https://img.shields.io/badge/Frontend-Vue%203-4FC08D?logo=vue.js&logoColor=white)](https://vuejs.org/)
[![Element Plus](https://img.shields.io/badge/UI-Element%20Plus-409EFF?logo=element&logoColor=white)](https://element-plus.org/)
[![QQ Bot](https://img.shields.io/badge/QQ%20Bot-Official%20%26%20OneBot%2011-12B7F5?logo=tencent-qq&logoColor=white)](https://bot.q.qq.com/)

[English Docs](docs/README.md) · [中文文档](docs/README.zh-CN.md) · [Latest Release](https://github.com/jikekei/Server_Qchat/releases/latest) · [Report an Issue](https://github.com/jikekei/Server_Qchat/issues)

</div>

---

**Server_Qcha** (also known as **Qridge**) is a browser-based operations panel and QQ bot bridge for **SCP: Secret Laboratory** dedicated servers. By decoupling process supervision from the web panel and bot, **restarting, updating, or crashing the panel never drops active game servers or players**.

It combines a standalone daemon supervisor, a dual-engine QQ bot, a mobile-ready web console, and official plugins for both EXILED and LabAPI.

## Key Features

### Server Lifecycle & Operations
- **Decoupled LocalAdmin Supervisor (`Server_Qcha.Daemon`)**: Isolates game-server lifecycles from the panel and bot. The bot can auto-start the daemon, while daemon health—PID, working-set/private memory, uptime, thread count, and managed-server load—can be monitored and controlled live from the panel.
- **Zero-Interruption SteamCMD Updates**: Updates managed SCP:SL dedicated servers (App `996560`, optional beta branch) from the panel into a staging copy while the server stays online. Swaps in automatically at round end (`restartnextround`), immediately when idle, or on demand (*update now* / *cancel*). Retains the previous build for one-click rollback and auto-rolls back if a new build fails to start (via `LocalAdmin:SteamCmdPath`).
- **Per-Server Memory Recovery**: Automatically triggers a round-end restart (`rnr`) when sustained physical memory exceeds the configured limit (default `2 GiB`), or restarts immediately if idle. Set to `0` per instance to disable.

### Web Panel & Access Governance
- **Qridge Web Console**: Built with Vue 3 and Element Plus via native ES modules (no front-end build step). Streams live logs with ANSI color decoding, circular log buffering, log-level filtering, scroll-position preservation while reading history, and jump-to-latest following.
- **Operations Dashboard & Mobile Layout**: Tracks 24-hour online-player trends, daily peaks, and sampled stats (filtering out the Dedicated Server placeholder). Automatically switches to a touch-friendly drawer layout below `768 px`.
- **Live In-Game Admin & Group Management**: Edits each server's `config_remoteadmin.txt` directly in the panel—granularity down to individual permissions, badge text/color, plugin permission nodes (master switch + allow/deny rules), and per-user overrides. Supports cross-server group cloning and applies changes live via the plugin (e.g., revoking an online admin takes effect immediately without restarting). Requires the `game-admin.manage` panel permission.
- **Multi-Account RBAC & Audit Trail**: Supports concurrent operator sessions, PBKDF2-SHA256 password hashing, granular panel permissions, and full audit logs recording actor, timestamp, source IP, and outcome.

### QQ Bot & Game Plugins
- **Dual QQ Bot Engines**: Supports **NapCat (OneBot 11)** and **Tencent Official QQ Bot OpenAPI v2**, with runtime engine switching and official command-panel sync (`/v2/panels`).
- **EXILED & LabAPI Plugins**: Built for `ExMod.Exiled` `9.15.2` (other versions unverified) and `LabAPI` `1.1.7+`. Includes in-game `.ac <message>` help alerts routed to QQ groups and optional MySQL integration for player statistics and account-ID ban enforcement ([configuration (ZH)](docs/zh-CN/guides/plugin-guide.md#玩家统计与数据库封禁)).

## The Panel at a Glance

<img src="./assets/readme/panel-modules-en.svg" width="100%" alt="The nine modules of the Qridge panel, grouped into monitoring and runtime, community and permissions, and data and traceability.">

*Nine core modules grouped into **Monitoring & Runtime**, **Community & Permissions**, and **Data & Traceability**. See the [web panel guide](docs/en/guides/web-panel.md) for details.*

## Qridge vs. Official LocalAdmin

| Capability | Official LocalAdmin | Qridge |
| --- | --- | --- |
| **Interface** | Local CLI window | Desktop & mobile web console |
| **Process Lifecycle** | Closing the window terminates the server | Independent daemon keeps game servers online across panel/bot restarts |
| **Multi-Server Management** | One window per instance | Unified panel for the daemon and all managed servers |
| **Remote Operations** | Requires RDP or SSH | Browser access from authorized devices |
| **Server Updates** | Manual SteamCMD update & downtime | Staged SteamCMD download, round-end swap, and auto-rollback |
| **Recovery & Watchdog** | Basic restart behavior | Heartbeats, silent-hang detection, memory recovery, and restart-rate limits |
| **Monitoring** | None | Live memory, PID, uptime, and 24-hour player trends |
| **Console** | Basic terminal output | Buffered web console with ANSI colors and live log-level filtering |
| **Panel Access Control** | Full host access = full control | Multi-account RBAC with audit logging |
| **In-Game Permissions** | Manual edits to `config_remoteadmin.txt` | Visual panel editor with live plugin hot-apply |
| **Community Integration** | None | NapCat / Official QQ Bot commands and in-game `.ac` help alerts |

## Architecture

```mermaid
graph TD
  subgraph Clients["Clients"]
    Browser["Desktop or mobile browser"]
    QQ["QQ group or direct message"]
  end
  subgraph BotHost["Server_Qcha.Bot"]
    Web["Web APIs, authentication, audit, RBAC"]
    Bot["NapCat or official QQ bot engine"]
    Data[("data/ persistent files")]
  end
  subgraph Supervisor["Server_Qcha.Daemon"]
    API["Local daemon API"]
    Manager["Server manager, heartbeat, recovery, logs"]
  end
  subgraph Servers["SCP:SL dedicated servers"]
    Game["SCPSL.exe"]
    Plugin["EXILED or LabAPI plugin"]
  end
  Browser <--> Web
  Web <--> Data
  QQ <--> Bot
  Web <--> API
  API --> Manager
  Manager --> Game
  Plugin <-->|Authenticated TCP| Bot
```

`Server_Qcha.Daemon` supervises the game processes and retains their console pipes. `Server_Qcha.Bot` communicates with the daemon over an internal HTTP API, while game plugins connect to the bot through dedicated command and notification TCP channels.

## Quick Start

1. **Download**: Grab the self-contained Windows or Linux package from the [latest release](https://github.com/jikekei/Server_Qchat/releases/latest) and extract it to a deployment directory.
2. **Configure**: If upgrading, back up `data/` and `appsettings.json` first. Copy `appsettings.Example.json` to `appsettings.Local.json` and configure your QQ connection and game-server paths.
3. **Secure**: Set strong random tokens (≥ 24 characters) for bot–plugin and daemon authentication, and configure explicit QQ admin and group allowlists where required.
4. **Install Plugin**: Extract the matching EXILED or LabAPI plugin ZIP into your game server's plugin folder, keeping the `dependencies/` directory alongside `Server_Qcha.dll` ([downloads](https://github.com/jikekei/Server_Qchat/releases/latest)).
5. **Launch**:
   - **Windows**: Run `一键启动(守护+机器人).bat` or start `Server_Qcha.Bot.exe`.
   - **Linux**: Run `./Server_Qcha.Bot` (which can start the daemon automatically).
6. **Sign In**: Open `http://127.0.0.1:8080/` and log in using the initial credentials printed to the console.

> [!NOTE]
> The panel binds to loopback (`127.0.0.1`) by default. To access it from other devices, set `WebPanel:Host` to `0.0.0.0` and restrict access with a firewall. Startup logs automatically print detected local IPv4 panel URLs.

## Documentation

| Topic | English | 简体中文 |
| --- | --- | --- |
| **Documentation Index** | [docs/README.md](docs/README.md) | [docs/README.zh-CN.md](docs/README.zh-CN.md) |
| **Getting Started** | [Getting Started Guide](docs/en/guides/getting-started.md) | — |
| **Web Panel** | [Web Panel Guide](docs/en/guides/web-panel.md) | [面板使用指南](docs/zh-CN/guides/web-panel.md) |
| **Bot & Configuration** | [Bot Component Guide](docs/en/components/bot.md) | [机器人指南](docs/zh-CN/guides/bot-guide.md) · [配置参考](docs/zh-CN/guides/configuration.md) |
| **Game Plugins** | [EXILED Plugin](docs/en/components/exiled-plugin.md) · [LabAPI Plugin](docs/en/components/labapi-plugin.md) | [插件与数据库指南](docs/zh-CN/guides/plugin-guide.md#玩家统计与数据库封禁) |
| **Security & Maintainers** | — | [数据库与构建安全说明](docs/zh-CN/guides/database-security.md) · [自动化发布指南](docs/zh-CN/reference/automation.md) |

## Security & Compatibility

> [!WARNING]
> **Do not expose the plugin TCP listeners to untrusted networks.** Protocol messages are authenticated with HMAC using a shared token, **not encrypted**. Use a VPN or firewall allowlist for cross-machine deployments.

- **Shared Tokens**: Configure matching random tokens of at least **24 characters** across the bot, daemon, and game plugin. Empty tokens or legacy public defaults trigger a warning on startup for backward compatibility only.
- **QQ Access Control**: Leaving QQ administrator allowlists empty retains legacy role-based behavior. Configure explicit administrator and group allowlists whenever strict access control is required.

## License

Licensed under the [Apache License 2.0](LICENSE).

SCP: Secret Laboratory, Unity, EXILED, and LabAPI remain subject to their respective owners' terms. Game-server reference assemblies are not distributed in this repository—see the [database and build-reference security notes (ZH)](docs/zh-CN/guides/database-security.md) for details.
