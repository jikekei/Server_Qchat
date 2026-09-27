# Server_Qcha — EXILED Plugin

[English documentation index](../README.md) · [简体中文](../../zh-CN/components/exiled-plugin.md) · [Project homepage](../../../README.md)

This plugin runs inside an SCP:SL Dedicated Server. It opens a TCP command listener, receives text commands from external clients such as Server_Qcha.Bot, executes supported operations in the game, and returns a response.

## Capabilities

- Query server information and online-player counts.
- Broadcast messages.
- Start or restart rounds.
- Ban players by in-server player ID.
- Return the player list.

## Install and build

1. Install EXILED on your SCP:SL Dedicated Server.
2. Install the .NET Framework 4.8.1 targeting pack and NuGet CLI. Point `SCPSL_REFERENCES` to the game's `SCPSL_Data/Managed` directory. Game assemblies are not distributed in this repository. EXILED 9.5.0 dependencies are restored from [`ExMod.Exiled`](https://www.nuget.org/packages/ExMod.Exiled/9.5.0).
3. From the repository root, restore and build:

```powershell
nuget restore server/Server_Qcha.csproj -PackagesDirectory packages
dotnet build server/Server_Qcha.csproj -c Release
```

The output is `server/bin/Release/Server_Qcha.dll`. Install that DLL in `%APPDATA%\EXILED\Plugins\` on Windows or `~/.config/EXILED/Plugins/` on Linux, then restart the server to load the plugin and create its configuration.

## Configuration

The plugin settings are defined in `server/Main.cs`:

| Setting | Default | Description |
| --- | --- | --- |
| `TcpPort` | `10087` | TCP command listener port; use a distinct port for each server |
| `IP` | `127.0.0.1` | Listener address; loopback is safest on a single host |
| `ServerName` | — | Display name, such as `Main` or `Test` |
| `ContentText` | — | Additional text when `DisplayMode=1` |
| `DisplayMode` | — | `0` adds query time, `1` adds `ContentText`, `2` returns basic information |
| `Debug` | `false` | Enables more detailed error output |

Keep `IP=127.0.0.1` when the bot and game server run on the same machine. For a split deployment, bind to a reachable private address and allow only trusted sources through the firewall.

## TCP request protocol

Each connection sends one UTF-8 command. The server returns readable UTF-8 text and closes the connection.

Commands without arguments:

- `cx`: online-player and online-staff counts, with optional time or configured text.
- `info`: detailed server information, including round and SCP-related status.
- `list`: players as `Nickname-Id` entries.
- `start`: start the round.
- `rest`: restart the round; rejected after the round has been running for more than 60 seconds.
- `allrest`: restart the server process.
- `ac`: returns a reserved help/alert response, normally `null`; primarily retained for extensions.

Commands with `&`-separated arguments:

- `bc&<text>`: broadcast `[Administrator message]<text>` for 15 seconds.
- `ychhe&<text>`: broadcast `[Automated message]<text>` for 17 seconds and lock the lobby.
- `kick&<id>&<reason>&<time>`: ban a player. The ID comes from `list`; the reason may contain spaces, but `&` is a delimiter; duration is an integer interpreted by the server/EXILED ban implementation.

The bot usually relays the plugin's response to the originating QQ group or direct message.

## Manual calls without the QQ bot

Send `cx` from PowerShell:

```powershell
$client = [System.Net.Sockets.TcpClient]::new("127.0.0.1", 10087)
$stream = $client.GetStream()
$bytes  = [System.Text.Encoding]::UTF8.GetBytes("cx")
$stream.Write($bytes, 0, $bytes.Length)
$buf = New-Object byte[] 4096
$n = $stream.Read($buf, 0, $buf.Length)
[System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
$stream.Dispose()
$client.Dispose()
```

For a broadcast, set `$cmd = "bc&Server maintenance in five minutes"` and send `$cmd` in place of `cx`.

## Relationship to the bot and security

`bot/` receives QQ messages through OneBot 11/NapCat or the official QQ bot integration. This plugin executes the corresponding operations inside the game server. You can also call the TCP protocol directly from a trusted private application.

Set the plugin `auth_token` to the same strong random value as the bot's `SocketServer:AuthToken`. Empty or legacy-default tokens log a warning and continue for compatibility. HMAC authenticates messages; it does not encrypt the channel. Use a VPN or firewall allowlist between machines. The repository currently verifies the EXILED 9.5.0 dependency; other versions, including 8.x, are not guaranteed.

For LabAPI, see the [LabAPI plugin guide](labapi-plugin.md). Do not load both frameworks on the same server instance.

## License

Copyright 2025 hmyhserver.top
