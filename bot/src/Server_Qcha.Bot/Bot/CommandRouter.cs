using EleCho.GoCqHttpSdk;
using EleCho.GoCqHttpSdk.Message;
using EleCho.GoCqHttpSdk.Post;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.Data;
using Server.Qcat.Socket;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace Server.Qcat.Bot;

public sealed class CommandRouter
{
    private readonly SocketServerOptions _socketOpts;
    private readonly SocketCommandClient _socket;
    private readonly PlayerRepository _players;
    private readonly ServerRegistry _registry;
    private readonly ILogger<CommandRouter> _log;

    public CommandRouter(
        IOptions<SocketServerOptions> socketOpts,
        SocketCommandClient socket,
        PlayerRepository players,
        ServerRegistry registry,
        ILogger<CommandRouter> log)
    {
        _socketOpts = socketOpts.Value;
        _socket = socket;
        _players = players;
        _registry = registry;
        _log = log;
    }

    public async Task HandleGroupMessageAsync(CqWsSession session, CqGroupMessagePostContext context, CancellationToken ct)
    {
        string text = context.Message?.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return;

        text = text.Trim();
        _log.LogInformation("群 {GroupId} 用户 {UserId}: {Text}", context.GroupId, context.Sender.UserId, text);

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

        if (text.Equals("#qcha", StringComparison.OrdinalIgnoreCase))
        {
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(GetVersionDetails()));
            return;
        }

        if (CommandParsing.TryParseHashIndex(text, out int serverIndex))
        {
            var msg = await HandleListAsync(serverIndex, ct);
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(msg));
            return;
        }

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
            await session.SendGroupMessageAsync(context.GroupId, new CqMessage(ok ? "绑定成功" : "服务器数据库内未找到该玩家的 ID"));
            return;
        }

        if (text.Equals("/me", StringComparison.OrdinalIgnoreCase) || text.Equals("/stat", StringComparison.OrdinalIgnoreCase))
        {
            long qq = context.Sender.UserId;
            var stats = await _players.GetByQqAsync(qq, ct);
            await session.SendGroupMessageAsync(
                context.GroupId,
                new CqMessage(stats is null ? "您没有绑定账号，请输入 /bd <Steam64>" : FormatStats(stats)));
            return;
        }

        if (!IsAdmin(context))
            return;

        if (text.StartsWith("/round ", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseServerIndex(text, out int idx, out string err))
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage(err));
                return;
            }

            var server = GetServerByIndex(idx);
            if (server == null)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器不在线"));
                return;
            }

            var resp = await _socket.SendAsync(server.ConnectHost, server.Port, "rest", ct);
            if (resp == null)
            {
                _registry.MarkOffline(server.ConnectHost, server.Port);
                _log.LogWarning("命令 [round] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）", server.Name, server.ConnectHost, server.Port);
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器不在线"));
            }
            else
            {
                _log.LogInformation("命令 [round] → [{ServerName}] {ConnectHost}:{Port} 执行成功", server.Name, server.ConnectHost, server.Port);
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage(resp));
            }
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

            if (!int.TryParse(parts[1], out int idx))
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器索引无效"));
                return;
            }

            var server = GetServerByIndex(idx);
            if (server == null)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器不在线"));
                return;
            }

            var resp = await _socket.SendAsync(server.ConnectHost, server.Port, $"bc&{parts[2]}", ct);
            if (resp == null)
            {
                _registry.MarkOffline(server.ConnectHost, server.Port);
                _log.LogWarning("命令 [bc] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）", server.Name, server.ConnectHost, server.Port);
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器不在线"));
            }
            else
            {
                _log.LogInformation("命令 [bc] → [{ServerName}] {ConnectHost}:{Port} 执行成功", server.Name, server.ConnectHost, server.Port);
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage(resp));
            }
            return;
        }

        if (text.StartsWith("/ban ", StringComparison.OrdinalIgnoreCase))
        {
            if (!CommandParsing.TryParseBan(text, out int idx, out string id, out string time, out string reason))
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("用法: /ban <服务器索引> <ID> <时间> <原因>"));
                return;
            }

            var server = GetServerByIndex(idx);
            if (server == null)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器不在线"));
                return;
            }

            var resp = await _socket.SendAsync(server.ConnectHost, server.Port, $"kick&{id}&{reason}&{time}", ct);
            if (resp == null)
            {
                _registry.MarkOffline(server.ConnectHost, server.Port);
                _log.LogWarning("命令 [ban] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）", server.Name, server.ConnectHost, server.Port);
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器不在线"));
            }
            else
            {
                _log.LogInformation("命令 [ban] → [{ServerName}] {ConnectHost}:{Port} 执行成功", server.Name, server.ConnectHost, server.Port);
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage(resp));
            }
            return;
        }

        if (text.StartsWith("/setadmin ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("用法: /setadmin <服务器索引> <ID> <权限组>"));
                return;
            }

            if (!int.TryParse(parts[1], out int idx))
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器索引无效"));
                return;
            }

            var server = GetServerByIndex(idx);
            if (server == null)
            {
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器不在线"));
                return;
            }

            var resp = await _socket.SendAsync(server.ConnectHost, server.Port, $"bc&{parts[2]}&{parts[3]}", ct);
            if (resp == null)
            {
                _registry.MarkOffline(server.ConnectHost, server.Port);
                _log.LogWarning("命令 [setadmin] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）", server.Name, server.ConnectHost, server.Port);
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage("服务器不在线"));
            }
            else
            {
                _log.LogInformation("命令 [setadmin] → [{ServerName}] {ConnectHost}:{Port} 执行成功", server.Name, server.ConnectHost, server.Port);
                await session.SendGroupMessageAsync(context.GroupId, new CqMessage(resp));
            }
        }
    }

    private async Task<string> HandleCxAsync(CancellationToken ct)
    {
        var servers = _registry.GetSorted();
        if (servers.Count == 0)
        {
            _log.LogInformation("指令 [cx] 执行失败：当前没有服务器在线（注册表为空）");
            return "当前没有服务器在线";
        }

        int totalOnline = 0;
        var sb = new StringBuilder();

        var tasks = servers.Select(async server =>
        {
            var resp = await _socket.SendAsync(server.ConnectHost, server.Port, "cx", ct);
            if (string.IsNullOrWhiteSpace(resp))
            {
                _registry.MarkOffline(server.ConnectHost, server.Port);
                _log.LogWarning("命令 [cx] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）", server.Name, server.ConnectHost, server.Port);
                return "";
            }

            _log.LogInformation("命令 [cx] → [{ServerName}] {ConnectHost}:{Port} 执行成功", server.Name, server.ConnectHost, server.Port);

            var m = Regex.Match(resp, @"在线人数:(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                Interlocked.Add(ref totalOnline, n);

            return resp;
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        foreach (var r in results)
            sb.Append(r);

        sb.Append($"总在线人数: {totalOnline}\r\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    private async Task<string> HandleInfoAsync(CancellationToken ct)
    {
        var servers = _registry.GetSorted();
        if (servers.Count == 0)
        {
            _log.LogInformation("指令 [info] 执行失败：当前没有服务器在线（注册表为空）");
            return "当前没有服务器在线";
        }

        var tasks = servers.Select(async (server, index) =>
        {
            var resp = await _socket.SendAsync(server.ConnectHost, server.Port, "info", ct);
            if (resp == null)
            {
                _registry.MarkOffline(server.ConnectHost, server.Port);
                _log.LogWarning("命令 [info] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）", server.Name, server.ConnectHost, server.Port);
                return $"#{index + 1} 服不在线\r\n";
            }

            _log.LogInformation("命令 [info] → [{ServerName}] {ConnectHost}:{Port} 执行成功", server.Name, server.ConnectHost, server.Port);
            return resp;
        });

        return string.Concat(await Task.WhenAll(tasks));
    }

    private async Task<string> HandleListAsync(int serverIndex, CancellationToken ct)
    {
        int onlineCount = _registry.Count;
        if (onlineCount == 0)
        {
            _log.LogInformation("指令 [list] 执行失败：当前没有服务器在线（注册表为空）");
            return "当前没有服务器在线";
        }

        if (serverIndex < 1 || serverIndex > onlineCount)
        {
            _log.LogWarning("指令 [list] 服务器索引 #{Index} 无效，当前在线 {Count} 台", serverIndex, onlineCount);
            return "服务器索引无效";
        }

        var server = GetServerByIndex(serverIndex);
        if (server == null)
            return "服务器不在线";

        var resp = await _socket.SendAsync(server.ConnectHost, server.Port, "list", ct);
        if (resp == null)
        {
            _registry.MarkOffline(server.ConnectHost, server.Port);
            _log.LogWarning("命令 [list] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）", server.Name, server.ConnectHost, server.Port);
            return "服务器不在线";
        }

        _log.LogInformation("命令 [list] → [{ServerName}] {ConnectHost}:{Port} 执行成功", server.Name, server.ConnectHost, server.Port);
        return $"服务器 #{serverIndex} 玩家列表\r\n{resp}";
    }

    private bool TryParseServerIndex(string text, out int idx, out string error)
    {
        idx = 0;
        error = "服务器索引无效";

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out idx))
        {
            error = "用法错误，需要指定服务器索引";
            return false;
        }

        int onlineCount = _registry.Count;
        if (idx < 1 || idx > onlineCount)
        {
            _log.LogWarning("指令服务器索引 #{Index} 无效，当前在线 {Count} 台", idx, onlineCount);
            error = "服务器索引无效";
            return false;
        }

        return true;
    }

    private ServerInfo? GetServerByIndex(int idx)
    {
        var list = _registry.GetSorted();
        if (idx < 1 || idx > list.Count)
            return null;
        return list[idx - 1];
    }

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
            "  #qcha            查看当前机器人版本详细",
            "  #<n>             查询第 n 个服务器玩家列表",
            "  /bd <Steam64>    绑定 QQ 到 Steam64",
            "  /me              查询自己绑定的玩家数据",
            "管理指令(群管理/群主):",
            "  /bc <n> <内容>                 广播",
            "  /round <n>                    重启回合 (rest)",
            "  /ban <n> <ID> <时间> <原因>   踢出/封禁 (kick)",
            "  /setadmin <n> <ID> <分组>     设置权限 (bc&id&group)",
        });
    }

    private static string GetVersionDetails()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var name = asm.GetName();
        var informationalVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? name.Version?.ToString()
            ?? "unknown";
        var fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
            ?? name.Version?.ToString()
            ?? "unknown";
        var framework = asm.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? ".NET";

        return string.Join("\r\n", new[]
        {
            "Qcha QQ Bot 版本详情",
            $"名称: {name.Name ?? "unknown"}",
            $"产品版本: {informationalVersion}",
            $"程序集版本: {name.Version?.ToString() ?? "unknown"}",
            $"文件版本: {fileVersion}",
            $"目标框架: {framework}",
            $"运行时: .NET {Environment.Version}",
            $"查询时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
        });
    }
}
