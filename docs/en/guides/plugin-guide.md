# SCP:SL Server Plugin Guide — EXILED and LabAPI

[English documentation index](../README.md) · [简体中文](../../zh-CN/guides/plugin-guide.md)

The repository provides plugins for both EXILED and LabAPI using the same configuration conventions and bot communication protocol. Install only the framework used by a given game-server instance.

## Select a plugin

| Framework | Repository build target | Release asset | Install location |
|---|---|---|---|
| EXILED | `ExMod.Exiled` 9.5.0 | `Server_Qcha-EXILED.dll` | `%APPDATA%\EXILED\Plugins\` |
| LabAPI | LabAPI 1.1.7+ | `Server_Qcha-LabAPI.dll` | `%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<port>\` |

EXILED and LabAPI are incompatible when loaded together on the same server port. EXILED versions other than the repository's 9.5.0 dependency, including 8.x, have not been verified. `PluginAPI.dll` appears as an EXILED package reference; this project is not a plugin for the archived standalone NwPluginAPI framework.

## Install the EXILED plugin

1. Download `Server_Qcha-EXILED.dll` from Releases and rename it to `Server_Qcha.dll`.
2. Place it in `%APPDATA%\EXILED\Plugins\` on Windows or `~/.config/EXILED/Plugins/` on Linux.
3. Start the server once to generate the configuration.

| Setting | Default | Description |
|---|---|---|
| `is_enabled` | `true` | Enable the plugin. |
| `tcp_port` | `10087` | TCP command listener port; use a different port for each server. |
| `ip` | `127.0.0.1` | Command listener address. |
| `server_name` | `1` | Name shown in the panel and bot replies. |
| `bot_ip` | `127.0.0.1` | Host running the bot. |
| `bot_port` | `10088` | Bot notification listener port. |
| `auth_token` | empty | Shared token matching the bot. Empty or legacy-default values warn but the plugin still starts. |

## Install the LabAPI plugin

1. Download `Server_Qcha-LabAPI.dll` and rename it to `Server_Qcha.dll`.
2. Put it under the per-port plugin directory `%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<server-port>\`. Use `plugins\global\` to share it between ports.
3. Start the server once. LabAPI creates a configuration at `%APPDATA%\SCP Secret Laboratory\LabAPI\configs\<server-port>\Server_Qcha\config.yml`.

Configure matching ports and a shared token. The main LabAPI keys are `tcp_port`, `ip`, `server_name`, `content_text`, `display_mode`, `bot_ip`, `bot_port`, `auth_token`, `sort_order`, `connect_host`, and `debug`. Their meanings match the EXILED version. `display_mode` values: `0` adds query time, `1` adds `content_text`, and `2` returns basic information. `connect_host` is needed when bot and game server are on different hosts.

## Communication channels

The plugin uses two connections:

1. **Command channel**: the plugin listens on `tcp_port` (10087 by default). The bot connects when it needs a response to a command such as `cx`, `list`, `bc`, or `kick`.
2. **Notification channel**: the plugin connects outward to `bot_ip:bot_port` (10088 by default) to send `register`, `heartbeat`, and `ac` events. Since the plugin initiates this connection, no inbound game-host port mapping is needed for notifications.

Both sides must use the same `AuthToken`. Messages use HMAC authentication, timestamps, and replay checks, but the TCP channel is not encrypted. Use a VPN or firewall allowlists across machines. Empty or legacy-default tokens log a warning and continue for compatibility.

## In-game `.ac` help alert

Press `~` to open the game console and enter `.ac <message>`. The plugin sends the player name, SteamID, and server name to the bot, which forwards the report to the configured QQ group. Online in-game administrators also see a prominent ten-second alert.

## Dedicated Server placeholder filtering

Some server/plugin combinations add a `Dedicated Server` placeholder entry. The plugin removes it from `cx` counts and `list` replies so panel views, bot responses, and sampled history contain actual players.

See the [EXILED plugin details](../components/exiled-plugin.md), [LabAPI plugin details](../components/labapi-plugin.md), and [communication protocol reference](../../zh-CN/reference/communication-protocol.md).
