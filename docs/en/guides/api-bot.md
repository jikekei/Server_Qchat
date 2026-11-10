<div align="center">

<img src="../../../assets/readme/doc-guides-en.svg" width="100%" alt="Server_Qcha documentation: Operating guides.">

</div>

[Documentation index](../../../docs/README.md) · [Project homepage](../../../README.md) · [简体中文](../../zh-CN/guides/api-bot.md)

# Server_Qchat_API — Lightweight Server Query Bot

[English documentation index](../README.md) · [简体中文](../../zh-CN/guides/api-bot.md)

`Server_Qchat_API` is a lightweight, independently deployable edition for operators who only need to query SCP:SL online-player counts from QQ groups. Unlike the full Qridge application, it does not require a game-server plugin or custom game-server networking. It connects to a OneBot 11 bot framework and queries servers by their public-list IDs.

## Features

- No server plugin: queries the public server-list API without changing game servers.
- OneBot 11 forward WebSocket support, compatible with NapCatQQ, Lagrange, and LLOneBot.
- Enter server IDs interactively at startup or save a default list; multiple IDs may be separated with `*`, commas, or spaces.
- WebSocket heartbeat and automatic reconnection every three seconds after network or framework interruption.
- Concurrent asynchronous requests for multiple servers.
- Lightweight console process, typically below 30 MB of memory.

## Requirements

- Windows 10/11/Server 2016+ or Linux x64/arm64.
- .NET 6 runtime or newer (.NET 8 is compatible).
- OneBot 11 framework such as [NapCatQQ](https://github.com/NapNeko/NapCatQQ), with a forward WebSocket endpoint (port 6700 is used below).
- Network access to `https://api.scplist.kr/`.

## Quick start

### 1. Start NapCatQQ

Deploy and sign in to NapCatQQ, then edit `config/onebot11_<your-QQ-ID>.json`. Enable the forward WebSocket server on the local interface and port 6700:

```json
{
  "ws": {
    "enable": true,
    "host": "127.0.0.1",
    "port": 6700
  }
}
```

Restart NapCatQQ and verify that its console reports the WebSocket listener is active.

### 2. Configure Server_Qchat_API

Edit `Server_Qchat_API/appsettings.json`:

```json
{
  "App": {
    "WsBaseUri": "ws://localhost:6700",
    "CommandKeyword": "cx",
    "DefaultServers": [31146, 31150, 31160]
  }
}
```

| Setting | Type | Default | Description |
|---|---|---|---|
| `App:WsBaseUri` | string | `ws://localhost:6700` | NapCatQQ forward WebSocket address. |
| `App:CommandKeyword` | string | `cx` | Case-insensitive substring used to trigger a group query. |
| `App:DefaultServers` | integer array | `[]` | Default server IDs used when no interactive list is entered. |

Environment variables can override settings using the `SERVER_QCHAT_API_` prefix, for example `SERVER_QCHAT_API_App__WsBaseUri=ws://127.0.0.1:6700`.

### 3. Find a server ID

This program queries the SCPSL list API using the server's numeric public-list ID. Find the server on the public server list or a statistics site and note its unique ID, usually five digits, such as `31146`.

### 4. Start the bot

Run `Server_Qchat_API.exe`. At the prompt, enter one or more IDs separated by `*` or commas, such as `31146*31150*31160`. If `DefaultServers` is configured, press Enter to use that list. For unattended startup, configure the defaults and launch the executable from a batch file or service supervisor.

## Group command and response

Send a group message containing the configured keyword, `cx` by default:

```text
cx
```

The bot asynchronously queries each server and replies with its current player count, for example:

```text
Current - Server 1
Players online: 18

Current - Server 2
Players online: 24
```

## Lightweight API edition versus full Qridge

| Capability | API query edition | Full Qridge (`Server_Qcha.Bot` + Daemon) |
|---|---|---|
| Purpose | Fast player-count query | Full operations, supervision, community integration, and panel |
| Game plugin | Not required | Requires EXILED or LabAPI plugin |
| LocalAdmin daemon | Not included | Independent daemon and crash recovery |
| Web panel | None | Vue 3 and Element Plus control panel |
| Group commands | Player-count query | Broadcast, moderation, round control, and more |
| In-game alerts | None | `.ac <message>` help alert to QQ group |
| QQ integration | OneBot 11 only | NapCat and official QQ Bot OpenAPI v2 |
| Setup | Minimal | Configure ports, tokens, and server plugin |

## Troubleshooting

### `WebSocket connect/run failed; retrying in 3s`

Verify NapCatQQ is running and signed in, `ws.enable` is `true`, `WsBaseUri` uses the same port (6700 by default), and the local firewall does not block the connection.

### The bot does not reply to `cx`

Check that the QQ account is not muted or blocked, the incoming message contains the configured keyword, and the console reports no network errors. A host that cannot reach the public API may time out.

### A player count is zero or stale

The public-list API may refresh or cache data for around one minute. A newly started server or recently joined players may take time to appear.

## License

This project is distributed under the repository's [Apache License 2.0](../../../LICENSE).

Copyright 2025 hmyhserver.top
