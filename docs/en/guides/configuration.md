# Configuration Reference

[English documentation index](../README.md) · [简体中文](../../zh-CN/guides/configuration.md)

## Loading and precedence

Settings load in this order; later sources override earlier values:

1. `appsettings.json` — packaged defaults.
2. `appsettings.Local.json` — recommended private, Git-ignored machine configuration.
3. Environment variables. Use `__` between levels, e.g. `WebPanel__Port=8080`.

## Main application settings

### `WebPanel`

| Field | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `true` | Enable the panel. |
| `Host` | string | `127.0.0.1` | Loopback by default. `0.0.0.0` binds all interfaces; restrict access with a firewall. |
| `Port` | int | `8080` | Panel port. |
| `SessionMinutes` | int | `480` | Session lifetime. |
| `DatabasePath` | string | `data/panel.db` | SQLite panel database path. |
| `DefaultAdminUsername` | string | `admin` | Initial built-in admin name. |
| `ResetBuiltInPasswordOnStartup` | bool | `false` | Reset and print a random password on every start. New accounts still get an initial random password. Enable temporarily only for recovery. |

### `LocalAdmin`

| Field | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `true` | Enable process management. |
| `Mode` | string | `Daemon` | `Daemon` is the recommended independent supervisor; `Embedded` runs management inside the bot. |
| `DaemonUri` | string | `http://127.0.0.1:10090` | Internal daemon endpoint. |
| `DaemonToken` | string | empty | Shared bot/daemon token. Empty or legacy-default values warn but allow startup; an empty token disables daemon token validation. |
| `AutoStartDaemon` | bool | `true` | Start the daemon when missing at bot startup. |
| `DefaultExecutablePath` | string | configured path | Default SCPSL executable. |
| `ConsoleBufferLines` | int | `2000` | Maximum in-memory console lines. |
| `LogDirectory` | string | `localadmin-logs` | Console log archive directory. |
| `WriteLogFiles` | bool | `true` | Write console output to files. |
| `LogExpirationDays` | int | `0` | Log retention; zero disables automatic cleanup. |
| `Servers` | array | `[]` | Managed server instances. |

Each `Servers[]` entry:

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | `main` | Unique ID; accepts ASCII letters, digits, underscore, and hyphen only. |
| `Name` | string | `Main server` | Panel display name. |
| `ExecutablePath` | string | path | Executable path; filename must be `SCPSL.exe` or `SCPSL.x86_64`. The daemon starts it under its own OS identity, so this is a high-privilege operation. |
| `WorkingDirectory` | string | empty | Working directory; defaults to the executable directory. |
| `GamePort` | int | `7777` | Game port. |
| `ExtraArguments` | string | empty | Additional command-line arguments. |
| `AutoStart` | bool | `false` | Launch this instance with the daemon. |
| `EnableHeartbeat` | bool | `true` | Heartbeat and silent-crash detection. |
| `HeartbeatSpanMaxThreshold` | int | `30` | Seconds before a heartbeat is considered missing. |
| `HeartbeatRestartInSeconds` | int | `11` | Delay before restart after abnormal heartbeat. |
| `RestartOnCrash` | bool | `true` | Restart after unexpected exit. |
| `RestartLimit` | int | `4` | Maximum restarts per rate-limit window. |
| `RestartTimeWindowSeconds` | int | `480` | Rate-limit window in seconds. |
| `GracefulStopTimeoutSeconds` | int | `30` | Graceful stop timeout before force termination. |
| `DisableAnsiColors` | bool | `true` | Strip ANSI color codes. |
| `RedirectStandardStreams` | bool | `true` | Capture standard output and error. |

### `Bot`

| Field | Type | Default | Description |
|---|---|---|---|
| `Mode` | string | `NapCat` | `NapCat`, `Official`, `Both`, or `None`. |
| `AllowedGroupIds` | long[] | `[]` | OneBot group allowlist. Empty means all groups the bot has joined; this setting does not grant admin rights. |
| `AdminUserIds` | long[] | `[]` | Explicit admin QQ IDs. When set, only listed users are admins, regardless of group role; notification lists do not grant access. Empty keeps legacy group-owner/admin and permitted private-user checks and logs a reminder. |
| `NotifyGroupIds` | long[] | `[]` | Groups for routine notifications. |
| `NotifyPrivateUserIds` | long[] | `[]` | Users for routine notifications. With `AdminUserIds` configured, this does not grant admin access. |
| `AcTargetGroupId` | long | `0` | Group for in-game `.ac` alerts. |

### `OfficialQq`

| Field | Type | Default | Description |
|---|---|---|---|
| `ApiBase` | string | `https://api.bot.qq.com` | Tencent API base URL. |
| `AppId` / `ClientSecret` | string | empty | Open Platform application credentials. |
| `Sandbox` | bool | `false` | Connect to the sandbox. |
| `Intents` | int | `33554432` | WebSocket event subscription bitmask. |
| `ShardIndex` / `ShardTotal` | int | `0` / `1` | Shard index and total. |
| `MaxTextLength` | int | `800` | Message text limit; longer replies are split. |
| `AllowActivePush` | bool | `false` | Allow proactive notifications if enabled by the platform. |
| `AdminOpenIds` | string[] | `[]` | Explicit admin OpenIds. Empty keeps group member `admin`/`owner` checks; direct messages cannot run admin commands in this fallback. |
| `AllowedGroupOpenIds` | string[] | `[]` | Group allowlist. |
| `NotifyGroupOpenIds` / `NotifyPrivateOpenIds` | string[] | `[]` | Routine notification targets. |
| `AcTargetGroupOpenId` | string | empty | Group for `.ac` alerts. |
| `ReconnectDelaySeconds` / `MaxReconnectDelaySeconds` | int | `5` / `60` | Initial and maximum reconnect delays. |
| `RequestTimeoutSeconds` | int | `15` | OpenAPI request timeout. |

### `GoCqHttp`

| Field | Type | Default | Description |
|---|---|---|---|
| `WsBaseUri` | string | `ws://127.0.0.1:6700` | OneBot 11 forward WebSocket endpoint. |
| `ReconnectDelaySeconds` / `MaxReconnectDelaySeconds` | int | `5` / `30` | Initial and maximum reconnect delays. |

### `SocketServer`

| Field | Type | Default | Description |
|---|---|---|---|
| `Host` | string | `127.0.0.1` | Game-plugin command endpoint host. |
| `Ports` | int[] | `[10087]` | Plugin command ports. |
| `NotificationHost` | string | `127.0.0.1` | Bind for alerts and heartbeats. `0.0.0.0` listens on all interfaces; restrict remote sources. |
| `NotificationPort` | int | `10088` | Notification listener port. |
| `AuthToken` | string | empty | Shared HMAC token; set a strong value matching the plugin. Empty or legacy-default values warn but do not block startup. Traffic is not encrypted. |
| `ConnectTimeoutMs` / `ReadTimeoutMs` | int | `10000` / `2000` | Connection and response-read timeouts. |
| `Retries` / `RetryDelayMs` | int | `3` / `1000` | Retry count and delay. |

### `MySql`

| Field | Type | Default | Description |
|---|---|---|---|
| `ConnectionString` | string | empty | Optional MySQL player-binding and statistics database connection. Store credentials in local configuration or environment variables. |

## Game plugin settings (`config.yml`)

EXILED or LabAPI generates this file on first load.

| Key | Type | Default | Description |
|---|---|---|---|
| `is_enabled` | bool | `true` | Enable the plugin. |
| `tcp_port` | int | `10087` | This server's command listener port. |
| `ip` | string | `127.0.0.1` | Listener bind address. |
| `server_name` | string | `1` | Display name in the panel and bot replies. |
| `bot_ip` | string | `127.0.0.1` | Bot notification listener address; use the management host across machines. |
| `bot_port` | int | `10088` | Bot notification port. |
| `auth_token` | string | empty | Must match `SocketServer:AuthToken`. Empty or legacy-default values warn but the TCP service still starts. |
| `debug` | bool | `false` | Print diagnostics to the game console. |

Copyright 2025 hmyhserver.top
