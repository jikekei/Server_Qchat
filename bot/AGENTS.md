# Agent Notes (Server_Qcha.Bot)

## Quick Commands

```powershell
dotnet build Server_Qcha.Bot.sln -c Release
dotnet test  Server_Qcha.Bot.sln -c Release
dotnet run   --project src/Server_Qcha.Bot -c Release
```

## Config

- Default config: `src/Server_Qcha.Bot/appsettings.json`
- Local overrides (gitignored): `src/Server_Qcha.Bot/appsettings.Local.json`
- Env var override example: `MySql__ConnectionString`

## What This Project Is

This folder contains the QQ bot side. It connects to go-cqhttp via forward WebSocket and forwards commands to your SCPSL SocketServer via TCP.
