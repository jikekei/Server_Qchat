# Server_Qcha — LabAPI Plugin

[English documentation index](../README.md) · [简体中文](../../zh-CN/components/labapi-plugin.md) · [Project homepage](../../../README.md)

The LabAPI plugin connects an SCP:SL game server to the Server_Qcha bot. It is the LabAPI migration of the EXILED plugin: features and the TCP protocol are intended to match, so no bot-side changes are required.

- [Migration notes and behavior differences](../../zh-CN/components/exiled-to-labapi-migration.md)
- [TCP protocol reference](../../zh-CN/reference/communication-protocol.md)

## Requirements

| Component | Requirement |
| --- | --- |
| Target framework | .NET Framework 4.8 (`net48`) |
| Plugin API | LabAPI 1.1.7 |
| Base class | `LabApi.Loader.Features.Plugins.Plugin<Config>` |
| References | LabAPI NuGet package and local game assemblies from `SCPSL_Data/Managed` |

Install the SCP:SL Dedicated Server through Steam and set `SCPSL_REFERENCES` to the server's `SCPSL_Data/Managed` directory. The official [`Northwood.LabAPI`](https://www.nuget.org/packages/Northwood.LabAPI) NuGet package supplies the LabAPI API; game assemblies are read locally and are not committed to this repository.

```powershell
$env:SCPSL_REFERENCES = 'C:\path\to\SCPSL_Data\Managed'
dotnet restore server-labapi/Server_Qcha.csproj
dotnet build server-labapi/Server_Qcha.csproj -c Release
```

The output is `server-labapi/bin/Release/Server_Qcha.dll` (approximately 35 KB; the plugin does not copy game dependencies). The LabAPI project uses the SDK-style project format, so both `dotnet build` and MSBuild work. The EXILED project uses a legacy project format and requires MSBuild or Visual Studio.

## Install

1. Place `Server_Qcha.dll` in the per-port plugin directory:

   ```text
   %APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<server-port>\Server_Qcha.dll
   ```

   The port is the value in the server's `config_gameplay.txt`. Use `plugins\global\` to load it for all ports.
2. Start the server once. LabAPI creates the configuration files under:

   ```text
   %APPDATA%\SCP Secret Laboratory\LabAPI\configs\<server-port>\Server_Qcha\
     config.yml       # Plugin settings; keys match the EXILED version.
     properties.yml   # LabAPI-managed file; is_enabled controls whether the plugin loads.
   ```

3. Edit `config.yml`, then restart the server or run `reload configs` in game.

EXILED and LabAPI conflict when loaded together. Do not install both variants on the same server instance; test a migration on a separate server port first.

## Configuration

LabAPI generates `config.yml` on first launch. Its fields correspond to the EXILED plugin:

| Key | Default | Description |
| --- | --- | --- |
| `tcp_port` | `10087` | Starting command-listener port; probes upward by up to 100 ports if occupied |
| `ip` | `127.0.0.1` | Command listener address |
| `server_name` | `Main` | Name shown in bot replies |
| `content_text` | empty | Text included when `display_mode = 1` |
| `display_mode` | `2` | `0` adds query time, `1` adds `content_text`, `2` returns basic information |
| `bot_ip` | `127.0.0.1` | Address of the bot notification listener |
| `bot_port` | `10088` | Bot notification port |
| `auth_token` | empty | Shared authentication token; set it to match the bot. Empty or legacy-default tokens warn but do not block startup. |
| `sort_order` | `0` | Positive values set an explicit display order; zero uses automatic ordering |
| `connect_host` | empty | Address the bot should use to reconnect to this server; required across machines, blank on one host |
| `debug` | `false` | Logs command and heartbeat details |

## Bot integration

The LabAPI and EXILED editions use the same ports, tokens, and message format:

- **Command channel**: the plugin listens on `tcp_port` (10087 by default); the bot connects and exchanges one request and response per connection.
- **Notification channel**: the plugin connects to `bot_ip:bot_port` (normally `127.0.0.1:10088`) and sends `register`, `heartbeat`, `unregister`, and `ac` notifications.

Because the plugin initiates the notification connection, the game server does not need an inbound port mapping for that channel. This is useful behind NAT. For remote command access, still restrict the listener with a firewall.

Set `auth_token` to the same strong random value as `SocketServer:AuthToken` in the bot. The protocol uses HMAC authentication, timestamps, and replay checks but does not encrypt traffic. Use a VPN or firewall restrictions across machines.

Copyright 2025 hmyhserver.top
