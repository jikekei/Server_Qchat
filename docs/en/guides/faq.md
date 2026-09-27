# FAQ and Troubleshooting

[English documentation index](../README.md) · [简体中文](../../zh-CN/guides/faq.md)

## Web panel

### Q1: Why cannot another computer or phone open the panel?

1. Set `WebPanel:Host` in `appsettings.Local.json` to `0.0.0.0`; `127.0.0.1` accepts only local connections.
2. Allow the panel port (8080 by default, TCP) in the cloud security group.
3. Allow that port in Windows Defender Firewall or Linux iptables/ufw. Restrict access to trusted clients.

### Q2: I forgot the initial administrator password.

Stop the program, temporarily set `WebPanel:ResetBuiltInPasswordOnStartup` to `true`, then restart and use the new password printed in the console. After signing in, set the option back to `false` and restart; otherwise every start resets the password.

### Q3: Do simultaneous administrator logins invalidate each other?

No. Independent session tokens allow one account to be used on multiple devices. Changing the password invalidates the account sessions on other devices.

## LocalAdmin and game processes

### Q4: Do I still need the official LocalAdmin window?

No. Server_Qcha implements the LocalAdmin/game process protocol through `-console`, `-id`, and `-heartbeat`. It starts processes, redirects standard streams, checks heartbeats, and recovers from crashes, with a web interface, mobile layout, multiple accounts, and live charts.

### Q5: Why does a crashed game server stop restarting?

Restart rate limiting may have been triggered. The default permits four restarts in a rolling 480-second (eight-minute) window. Repeated crashes caused by invalid configuration, missing dependencies, or plugin errors stop retries to avoid exhausting CPU and disk I/O. Inspect `localadmin-logs/`, fix the underlying error, and start the server from the panel.

### Q6: Why is `Dedicated Server` excluded from player counts?

Some server environments report a virtual `Dedicated Server` entry. The application filters it from counts, charts, and player tables so they represent actual players.

## Plugins and network

### Q7: Why does a panel command say Unauthorized or time out?

Check that the plugin `auth_token` exactly matches `SocketServer:AuthToken` (including case), that the plugin `tcp_port` is in `SocketServer:Ports`, and that the port is available. For split hosts, allow the plugin TCP port through the game-server firewall and verify routing.

### Q8: Why does `.ac` not notify the QQ group?

Check that `Bot:AcTargetGroupId` is a nonzero correct group ID (or configure the official platform OpenId), and that plugin `bot_ip`/`bot_port` points to the bot notification listener (10088 by default). For cloud deployments, allow inbound TCP 10088 from the game servers.

## QQ bot

### Q9: Why does the bot ignore group commands?

Verify NapCatQQ/OneBot is logged in and its forward WebSocket server is running (port 6700 by default). Check `Bot:AllowedGroupIds`: a non-empty list restricts handling to those groups. Set logging to Debug in the console or panel and confirm that group-message events arrive.
