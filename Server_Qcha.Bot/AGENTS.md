# Agent Notes (Server.Qcat)

## Quick Commands

```powershell
dotnet build SocketServer-GoHttpQQBOT.sln -c Release
dotnet test  SocketServer-GoHttpQQBOT.sln -c Release
dotnet run   --project src/Server.Qcat -c Release
```

## Config

- Default config: `src/Server.Qcat/appsettings.json`
- Local overrides (gitignored): `src/Server.Qcat/appsettings.Local.json`
- Env var override example: `MySql__ConnectionString`

## What This Project Is

This folder contains the QQ bot side. It connects to go-cqhttp via forward WebSocket and forwards commands to your SCPSL SocketServer via TCP.

