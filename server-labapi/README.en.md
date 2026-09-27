# Server_Qcha — LabAPI Plugin

[简体中文](README.md) · [English](README.en.md)

The LabAPI edition connects an SCP:SL server to the Server_Qcha bot over the same authenticated TCP protocol as the EXILED edition. It supports server and player queries, broadcasts, moderation commands, round management, and bot notifications.

## Requirements

- SCP:SL Dedicated Server with LabAPI 1.1.7 or a compatible version.
- .NET Framework 4.8 targeting pack and the .NET SDK.
- Local game reference assemblies from the server's `SCPSL_Data/Managed` directory. They are not committed or distributed in this repository.

Set `SCPSL_REFERENCES` to the game's `SCPSL_Data/Managed` directory, then restore and build from the repository root:

```powershell
$env:SCPSL_REFERENCES = 'C:\path\to\SCPSL_Data\Managed'
dotnet restore server-labapi/Server_Qcha.csproj
dotnet build server-labapi/Server_Qcha.csproj -c Release
```

The plugin is produced at `server-labapi/bin/Release/Server_Qcha.dll`. Install it in the LabAPI plugin directory for the server port, or in the global plugin directory. Install only the plugin DLL; the game and LabAPI provide their own dependencies.

## Configuration and network

On first start, LabAPI creates the plugin configuration under its per-port configuration directory. The default command listener uses `127.0.0.1:10087`; the notification client connects to the bot at `127.0.0.1:10088`.

Set `auth_token` to the same strong random value as `SocketServer:AuthToken` in the bot. The protocol authenticates messages with HMAC, timestamps, and replay checks, but does not encrypt traffic. Use a VPN or firewall allowlist across machines. Empty or legacy-default tokens log a warning and continue for compatibility.

Do not load both EXILED and LabAPI versions on the same server instance. See the [migration notes](MIGRATION-EXILED-to-LABAPI.md) and [TCP protocol reference](../通信协议文档.md).
