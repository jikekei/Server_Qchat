# Server_Qcha — EXILED Plugin

[简体中文](README.md) · [English](README.en.md)

This SCP: Secret Laboratory Dedicated Server plugin exposes the authenticated TCP command and notification channels used by the Server_Qcha bot. It can report player and server status, deliver broadcasts, and handle supported moderation and round-management commands.

## Requirements

- SCP:SL Dedicated Server and EXILED.
- The project is built against EXILED 9.5.0 using the `ExMod.Exiled` NuGet package.
- .NET Framework 4.8.1 targeting pack, NuGet CLI, and MSBuild or Visual Studio.
- Local SCP:SL server references. Set `SCPSL_REFERENCES` to the server's `SCPSL_Data/Managed` directory. Game assemblies are not included in this repository.

## Build

From the repository root:

```powershell
nuget restore server/Server_Qcha.csproj -PackagesDirectory packages
msbuild server/Server_Qcha.csproj /p:Configuration=Release
```

The plugin is produced at `server/bin/Release/Server_Qcha.dll`. Install only this plugin DLL in the EXILED plugins directory; the game and EXILED supply their own dependencies.

## Network and configuration

The default command listener binds to `127.0.0.1:10087`. Keep loopback for a same-machine bot. For a split deployment, bind to a reachable address and use a firewall allowlist. The plugin sends notifications to the bot on port `10088` by default.

Set the plugin `auth_token` to the same strong random value as `SocketServer:AuthToken` in the bot. The protocol uses HMAC authentication, timestamps, and replay checks; it does not encrypt traffic. Use a VPN or firewall restrictions between machines. Empty or legacy-default tokens produce a startup warning but retain compatibility behavior.

For the LabAPI alternative, see [server-labapi](../server-labapi/README.en.md). Both plugin frameworks must not be loaded for the same server instance.
