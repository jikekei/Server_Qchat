using EleCho.GoCqHttpSdk;
using EleCho.GoCqHttpSdk.Message;
using EleCho.GoCqHttpSdk.Post;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.Data;
using Server.Qcat.Socket;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.Qcat.Bot;

public sealed class CommandRouter
{
    private readonly SocketServerOptions _socketOpts;
    private readonly SocketCommandClient _socket;
    private readonly PlayerRepository _players;
    private readonly ILogger<CommandRouter> _log;

    public CommandRouter(
        IOptions<SocketServerOptions> socketOpts,
        SocketCommandClient socket,
        PlayerRepository players,
        ILogger<CommandRouter> log)
    {
        _socketOpts = socketOpts.Value;
        _socket = socket;
        _players = players;
        _log = log;
    }

    public async Task HandleGroupMessageAsync(CqWsSession session, CqGroupMessagePostContext context, CancellationToken ct)
    {
        string text = context.Message?.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return;

        text = text.Trim();
        _log.LogInformation("Group {GroupId} {UserId}: {Text}", context.GroupId, context.Sender.UserId, text);

        // Simple commands (non-admin)
        if (text.Equals("help", StringComparison.OrdinalIgnoreCase) || text.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(GetHelpText()));
            return;
        }

        if (text.Equals("cx", StringComparison.OrdinalIgnoreCase))
        {
            var msg = await HandleCxAsync(ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg));
            return;
        }

        if (text.Equals("info", StringComparison.OrdinalIgnoreCase))
        {
            var msg = await HandleInfoAsync(ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg));
            return;
        }

        // "#n" -> list players of server n
        if (CommandParsing.TryParseHashIndex(text, out int serverIndex))
        {
            var msg = await HandleListAsync(serverIndex, ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg));
            return;
        }

        // Bind QQ to Steam64 id: /bd 7656...
        if (text.StartsWith("/bd ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("/bind ", StringComparison.OrdinalIgnoreCase))
        {
            var playerId = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "";
            if (string.IsNullOrWhiteSpace(playerId))
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("用法: /bd <Steam64>"));
                return;
            }

            long qq = context.Sender.UserId;
            bool ok = await _players.BindQqAsync(playerId.Trim(), qq, ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(ok ? "绑定成功" : "服务器数据库内未找到该玩家的ID"));
            return;
        }

        // Query self stats: /me
        if (text.Equals("/me", StringComparison.OrdinalIgnoreCase) || text.Equals("/stat", StringComparison.OrdinalIgnoreCase))
        {
            long qq = context.Sender.UserId;
            var stats = await _players.GetByQqAsync(qq, ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(stats is null ? "您没有绑定账号，请输入 /bd <Steam64>" : FormatStats(stats)));
            return;
        }

        // Admin-only commands
        if (!IsAdmin(context))
            return;

        if (text.StartsWith("/round ", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseServerIndex(text, out int idx, out string err))
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage(err));
                return;
            }

            var port = GetPortByIndex(idx);
            var resp = await _socket.SendAsync(port, "rest", ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(resp ?? "服务器不在线"));
            return;
        }

        if (text.StartsWith("/bc ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("用法: /bc <服务器索引> <内容>"));
                return;
            }

            if (!int.TryParse(parts[1], out int idx) || idx < 1 || idx > _socketOpts.Ports.Length)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器索引无效"));
                return;
            }

            var port = GetPortByIndex(idx);
            var resp = await _socket.SendAsync(port, $"bc&{parts[2]}", ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(resp ?? "服务器不在线"));
            return;
        }

        if (text.StartsWith("/ban ", StringComparison.OrdinalIgnoreCase))
        {
            // /ban <serverIndex> <id> <time> <reason...>
            if (!CommandParsing.TryParseBan(text, out int idx, out string id, out string time, out string reason))
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("用法: /ban <服务器索引> <ID> <时间> <原因>"));
                return;
            }

            if (idx < 1 || idx > _socketOpts.Ports.Length)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器索引无效"));
                return;
            }

            // Keep legacy wire format: kick&{id}&{reason}&{time}
            var port = GetPortByIndex(idx);
            var resp = await _socket.SendAsync(port, $"kick&{id}&{reason}&{time}", ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(resp ?? "服务器不在线"));
            return;
        }

        if (text.StartsWith("/setadmin ", StringComparison.OrdinalIgnoreCase))
        {
            // /setadmin <serverIndex> <id> <group>
            var parts = text.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("用法: /setadmin <服务器索引> <ID> <权限分组>"));
                return;
            }

            if (!int.TryParse(parts[1], out int idx) || idx < 1 || idx > _socketOpts.Ports.Length)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器索引无效"));
                return;
            }

            var port = GetPortByIndex(idx);
            var resp = await _socket.SendAsync(port, $"bc&{parts[2]}&{parts[3]}", ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(resp ?? "服务器不在线"));
            return;
        }
    }

    private async Task<string> HandleCxAsync(CancellationToken ct)
    {
        if (_socketOpts.Ports.Length == 0)
            return "没有配置任何服务器端口 (SocketServer:Ports)";

        int totalOnline = 0;
        var sb = new StringBuilder();

        var tasks = _socketOpts.Ports.Select(async (port, index) =>
        {
            var resp = await _socket.SendAsync(port, "cx", ct);
            if (string.IsNullOrWhiteSpace(resp))
                return "";

            // Hide "empty" servers if response contains 0 online in common formats.
            if (resp.Contains("在线人数:0/45", StringComparison.OrdinalIgnoreCase) ||
                resp.Contains("在线人数:0/40", StringComparison.OrdinalIgnoreCase))
                return "";

            var m = Regex.Match(resp, @"在线人数:(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                Interlocked.Add(ref totalOnline, n);

            return resp;
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        foreach (var r in results)
            sb.Append(r);

        sb.Append($"总在线人数:{totalOnline}\r\n时间:{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    private async Task<string> HandleInfoAsync(CancellationToken ct)
    {
        if (_socketOpts.Ports.Length == 0)
            return "没有配置任何服务器端口 (SocketServer:Ports)";

        var tasks = _socketOpts.Ports.Select(async (port, index) =>
        {
            var resp = await _socket.SendAsync(port, "info", ct);
            return resp ?? $"#{index + 1}服不在线\r\n";
        });

        return string.Concat(await Task.WhenAll(tasks));
    }

    private async Task<string> HandleListAsync(int serverIndex, CancellationToken ct)
    {
        if (_socketOpts.Ports.Length == 0)
            return "没有配置任何服务器端口 (SocketServer:Ports)";

        if (serverIndex < 1 || serverIndex > _socketOpts.Ports.Length)
            return "服务器索引无效";

        int port = GetPortByIndex(serverIndex);
        var resp = await _socket.SendAsync(port, "list", ct);
        return resp is null ? "服务器不在线" : $"服务器{serverIndex}服玩家列表\r\n{resp}";
    }

    private bool TryParseServerIndex(string text, out int idx, out string error)
    {
        idx = 0;
        error = "服务器索引无效";
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out idx))
        {
            error = "用法: /round <服务器索引>";
            return false;
        }

        if (idx < 1 || idx > _socketOpts.Ports.Length)
        {
            error = "服务器索引无效";
            return false;
        }

        return true;
    }

    private int GetPortByIndex(int idx) => _socketOpts.Ports[idx - 1];

    private static bool IsAdmin(CqGroupMessagePostContext context)
    {
        return context.Sender.Role == CqRole.Admin || context.Sender.Role == CqRole.Owner;
    }

    private static string FormatStats(PlayerStats p)
    {
        double hours = p.PlayTimeSeconds / 3600.0;
        double kd = p.Deaths == 0 ? 0 : (p.ScpsKilled * 5.0 + p.PlayersKilled) / p.Deaths;

        var sb = new StringBuilder();
        sb.AppendLine("玩家信息统计");
        sb.AppendLine("----------------------------");
        sb.AppendLine($"玩家名称: {p.PlayerName}");
        sb.AppendLine($"SCP 击杀数: {p.ScpsKilled}");
        sb.AppendLine($"玩家击杀数: {p.PlayersKilled}");
        sb.AppendLine($"游玩时间: {hours:F2} 小时");
        sb.AppendLine($"死亡次数: {p.Deaths}");
        sb.AppendLine($"补充说明: {p.AdminNote ?? ""}");
        sb.AppendLine($"KD 比率: {Math.Round(kd, 6)}");
        return sb.ToString().TrimEnd();
    }

    private static string GetHelpText()
    {
        return string.Join("\r\n", new[]
        {
            "Server.Qcat 指令帮助",
            "普通指令:",
            "  cx               查询所有服务器在线人数",
            "  info             查询所有服务器信息",
            "  #<n>             查询第 n 个服务器玩家列表",
            "  /bd <Steam64>    绑定 QQ 到 Steam64",
            "  /me              查询自己绑定的玩家数据",
            "管理指令(群管理/群主):",
            "  /bc <n> <内容>                 广播",
            "  /round <n>                     重启回合 (rest)",
            "  /ban <n> <ID> <时间> <原因>    踢出/封禁 (kick)",
            "  /setadmin <n> <ID> <分组>      设置权限 (bc&id&group)",
        });
    }
}
