<div align="center">

<img src="../../../assets/readme/doc-guides-en.svg" width="100%" alt="Server_Qcha documentation: Operating guides.">

</div>

[Documentation index](../../../docs/README.md) · [Project homepage](../../../README.md) · [简体中文](../../zh-CN/guides/bot-guide.md)

# QQ Bot and Game-Server Integration Guide

[English documentation index](../README.md) · [简体中文](../../zh-CN/guides/bot-guide.md)

Server_Qcha v2.0 supports two bot engines: the official QQ Bot OpenAPI v2 platform and NapCatQQ using OneBot 11. Both use one command-dispatch system and can be switched in the web panel while the application is running.

## Engine comparison

| Feature | Official QQ Bot OpenAPI v2 | NapCatQQ / OneBot 11 |
| --- | --- | --- |
| Connection | Tencent OpenAPI v2 and WebSocket gateway | OneBot 11 forward WebSocket |
| Group trigger | Mention the bot before a command | Text or configured prefix; mention is not required |
| Official command panel | Supported through `/v2/panels` | Not supported |
| Administrator handling | Official panel supports `only_admin`; app also checks configured user allowlists or member roles | App checks configured user allowlists or group roles |
| Message limits | Follows platform rate limits; long messages are split automatically | Generally less restrictive for longer text |
| Typical use | Official platform compliance and long-term operation | Existing OneBot deployments and no-mention workflows |

## Option A: official QQ Bot

### Create the application

1. Sign in at the [QQ Open Platform](https://q.qq.com/) with a developer account.
2. Create a bot application and enable the required group and/or direct-message scenarios.
3. Obtain its `AppID` and `AppSecret` (`ClientSecret`).
4. For sandbox testing, add the test QQ groups and users to the platform sandbox allowlist.

### Configure the official engine

In `appsettings.Local.json` (or `appsettings.json`), set values similar to:

```json
{
  "Bot": { "Mode": "Official" },
  "OfficialQq": {
    "ApiBase": "https://api.bot.qq.com",
    "AppId": "102xxxxxx",
    "ClientSecret": "your_client_secret_here",
    "Sandbox": false,
    "Intents": 33554432,
    "MaxTextLength": 800,
    "AllowActivePush": false,
    "AdminOpenIds": [],
    "AllowedGroupOpenIds": [],
    "AcTargetGroupOpenId": ""
  }
}
```

- Set `Bot:Mode` to `Official`.
- `AppId` and `ClientSecret` are the credentials from the developer platform.
- Set `Sandbox` to `true` while testing in the platform sandbox and to `false` for production.
- `Intents` defaults to `33554432`, the group and direct-message event intent.
- `MaxTextLength` defaults to 800 characters. Longer replies are split automatically.

### Synchronize the official command panel

The application can register supported commands through `/v2/panels`. It truncates command names to the platform's 14-character limit and descriptions to 30 characters. Sensitive operations such as `/bc`, `/round`, `/ban`, and `/setadmin` are marked `only_admin: true`. In the web panel settings, administrators can synchronize the command definitions, inspect current registrations, or clear and reset them.

In group chats, users must mention the bot and then send a command (for example, `@Bot cx`). In direct messages, a command can be sent without a mention. The app manages required `msg_id` and increasing `msg_seq` values for passive replies, including retries.

## Option B: NapCatQQ / OneBot 11

### Configure NapCatQQ

1. Deploy and start NapCatQQ with a dedicated operations QQ account.
2. Enable its OneBot 11 forward WebSocket server.
3. A same-machine setup can listen on `127.0.0.1:6700`. If NapCat access authentication is enabled, configure its token on both ends.

### Configure the bot

```json
{
  "Bot": {
    "Mode": "NapCat",
    "AllowedGroupIds": [123456789],
    "AcTargetGroupId": 123456789,
    "AdminUserIds": []
  },
  "GoCqHttp": {
    "WsBaseUri": "ws://127.0.0.1:6700",
    "ReconnectDelaySeconds": 5,
    "MaxReconnectDelaySeconds": 30
  }
}
```

- `WsBaseUri` is the full forward WebSocket URL.
- `AllowedGroupIds` restricts which groups are handled. An empty list means all groups the bot has joined.
- When `AdminUserIds` is configured, only those QQ user IDs can run administrator commands; group roles and notification lists do not grant administrator access. If it is empty, legacy group-owner/admin checks and the permitted private-message fallback remain, and startup prints a reminder.
- `AcTargetGroupId` selects the group for in-game help alerts.

## Switch engines in the web panel

On the **System Settings** or **Bot Management** page, check the active engine, connection health, and latency. Select `NapCat`, `Official`, `Both`, or `None`, then choose **Save and Apply**. The application disconnects the old engine, starts the selected engine(s), refreshes subscriptions, and checks health in the background; a bot process restart is not required.

## QQ commands

Player commands:

| Command | Purpose | Example |
| --- | --- | --- |
| `help` or `/help` | Show the available commands | `help` |
| `cx` or `/cx` | Query status and online counts for managed servers | `cx` |
| `info` or `/info` | Show server information and rules | `info` |
| `#<server>` | List players on the selected server, including nickname, role group, SteamID, and ping | `#1` |
| `/bd <Steam64>` | Bind the sender's QQ account to a Steam64 ID | `/bd 76561198000000000` |
| `/me` | Show the sender's stored play history and statistics | `/me` |

Administrator commands:

| Command | Purpose | Example |
| --- | --- | --- |
| `/bc <server> <message>` | Broadcast to a game server | `/bc 1 Server maintenance in 10 minutes` |
| `/round <server>` | Restart the current round | `/round 1` |
| `/ban <server> <id> <duration> <reason>` | Kick or ban a player | `/ban 1 3 120m Rule violation` |
| `/setadmin <server> <id> <group>` | Assign a temporary administration group | `/setadmin 1 3 admin` |

When explicit administrator IDs are set (`Bot:AdminUserIds` or `OfficialQq:AdminOpenIds`), only listed users can run these commands. Otherwise, legacy group-owner/administrator role checks apply; see the [configuration reference](configuration.md).

## In-game help alerts (`.ac`)

Players can press `~` to open the game console and enter `.ac <reason>`. The plugin sends a structured event containing the player's nickname, SteamID, and server name to the bot, which relays it to the configured QQ target group. Online in-game administrators also see a 10-second alert broadcast.

## Player data and persistence

When `MySql:ConnectionString` is configured, the application manages the QQ/Steam binding table (`qq_steam_binding`) and player history data. Bindings and play statistics can be shared across servers. Administrators can search, edit, or unbind records in the web panel's database management module.

Copyright 2025 hmyhserver.top
