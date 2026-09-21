using Microsoft.Extensions.Logging;
using Server.Qcat.Data;
using Server.Qcat.Socket;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;

namespace Server.Qcat.Bot;

/// <summary>
/// 指令路由：平台无关的指令分发与执行。
///
/// 相比旧版直接依赖 <c>CqWsSession</c> 与 <c>CqGroupMessagePostContext</c> 的写法，
/// 这里只认 <see cref="BotIncomingMessage"/> + <see cref="IBotClient"/>，
/// 因此同一套指令逻辑可以原样跑在 NapCat（OneBot 11）与 QQ 官方 Bot API 上。
/// 平台差异（文本长度、CQ 码、被动回复、身份标识）由适配器与
/// <see cref="BotCommandParser.SplitForPlatform"/> 消化，业务层不再感知。
/// </summary>
public sealed class CommandRouter
{
    private readonly SocketCommandClient _socket;
    private readonly PlayerRepository _players;
    private readonly BotBindingStore _bindings;
    private readonly ServerRegistry _registry;
    private readonly ILogger<CommandRouter> _log;

    public CommandRouter(
        SocketCommandClient socket,
        PlayerRepository players,
        BotBindingStore bindings,
        ServerRegistry registry,
        ILogger<CommandRouter> log)
    {
        _socket = socket;
        _players = players;
        _bindings = bindings;
        _registry = registry;
        _log = log;
    }

    public async Task HandleAsync(IBotClient client, BotIncomingMessage msg, CancellationToken ct)
    {
        var request = BotCommandParser.Parse(msg.Text);
        if (request.IsEmpty)
            return;

        var definition = BotCommandCatalog.Resolve(request.Name);

        if (definition is null)
        {
            // 只有显式带前缀（/ 或 #）的才提示未知指令，避免群里正常聊天被回怼
            if (request.ExplicitCommand)
                await ReplyAsync(client, msg, $"未知指令：{request.Name}\n发送 /help 查看全部可用指令", ct);
            return;
        }

        _log.LogInformation("[{Platform}] {Scope} {Target} 用户 {Sender}: {Text}",
            client.DisplayName,
            msg.IsGroup ? "群" : "私聊",
            msg.TargetId,
            msg.SenderId,
            request.RawText);

        if (definition.AdminOnly && !msg.IsAdmin)
        {
            _log.LogWarning("用户 {Sender} 在 {Target} 尝试执行管理指令 [{Command}] 被拒绝（权限不足）",
                msg.SenderId, msg.TargetId, definition.Name);
            await ReplyAsync(client, msg, BotCommandCatalog.NoPermissionHint, ct);
            return;
        }

        switch (definition.Name)
        {
            case "help":
                await ReplyAsync(client, msg,
                    BotCommandCatalog.BuildHelpText(client.Platform, msg.IsAdmin), ct);
                return;

            case "version":
                await ReplyAsync(client, msg, GetVersionDetails(client), ct);
                return;

            case "cx":
                await ReplyAsync(client, msg, await HandleCxAsync(ct), ct);
                return;

            case "info":
                await ReplyAsync(client, msg, await HandleInfoAsync(ct), ct);
                return;

            case "list":
                await HandleListCommandAsync(client, msg, request, ct);
                return;

            case "bind":
                await HandleBindCommandAsync(client, msg, request, ct);
                return;

            case "me":
                await HandleMeCommandAsync(client, msg, ct);
                return;

            case "broadcast":
                await HandleServerCommandAsync(client, msg, request,
                    minArgs: 2,
                    buildPayload: _ => $"bc&{request.ArgTextFrom(1)}",
                    commandLabel: "bc", ct);
                return;

            case "round":
                await HandleServerCommandAsync(client, msg, request,
                    minArgs: 1,
                    buildPayload: _ => "rest",
                    commandLabel: "round", ct);
                return;

            case "ban":
                await HandleServerCommandAsync(client, msg, request,
                    minArgs: 4,
                    buildPayload: args => $"kick&{args[1]}&{request.ArgTextFrom(3)}&{args[2]}",
                    commandLabel: "ban", ct);
                return;

            case "setadmin":
                await HandleServerCommandAsync(client, msg, request,
                    minArgs: 3,
                    buildPayload: args => $"bc&{args[1]}&{args[2]}",
                    commandLabel: "setadmin", ct);
                return;

            default:
                await ReplyAsync(client, msg, $"指令 [{definition.Name}] 暂未实现", ct);
                return;
        }
    }

    // ---------------- 指令实现 ----------------

    private async Task HandleListCommandAsync(IBotClient client, BotIncomingMessage msg, BotCommandRequest request, CancellationToken ct)
    {
        int? index = await ResolveServerIndexAsync(client, msg, request.Arg(0), ct);
        if (index is null)
            return;

        await ReplyAsync(client, msg, await HandleListAsync(index.Value, ct), ct);
    }

    private async Task HandleBindCommandAsync(IBotClient client, BotIncomingMessage msg, BotCommandRequest request, CancellationToken ct)
    {
        string playerId = request.Arg(0).Trim();
        if (playerId.Length == 0)
        {
            await ReplyAsync(client, msg, "用法: /bd <Steam64>", ct);
            return;
        }

        try
        {
            // NapCat：QQ 号为真实数字，直接写回玩家库 QQ_ID 列
            if (client.Platform == BotPlatform.NapCat)
            {
                if (!long.TryParse(msg.SenderId, out long qq))
                {
                    await ReplyAsync(client, msg, "无法识别你的 QQ 号，绑定失败", ct);
                    return;
                }

                bool ok = await _players.BindQqAsync(playerId, qq, ct);
                await ReplyAsync(client, msg,
                    ok ? "绑定成功" : "服务器数据库内未找到该玩家的 ID", ct);
                return;
            }

            // 官方平台：身份是 OpenID（字符串），写入独立的绑定表
            if (!await _players.PlayerExistsAsync(playerId, ct))
            {
                await ReplyAsync(client, msg, "服务器数据库内未找到该玩家的 ID", ct);
                return;
            }

            bool bound = await _bindings.BindAsync(client.Platform, msg.SenderId, playerId, ct);
            await ReplyAsync(client, msg,
                bound
                    ? "绑定成功\n提示：官方平台使用 OpenID 记录身份，更换账号后需重新绑定"
                    : "绑定失败，请稍后重试", ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "执行 [/bd] 绑定失败（平台 {Platform}，用户 {Sender}）", client.Platform, msg.SenderId);
            await ReplyAsync(client, msg, "绑定失败，请检查数据库配置或稍后重试", ct);
        }
    }

    private async Task HandleMeCommandAsync(IBotClient client, BotIncomingMessage msg, CancellationToken ct)
    {
        try
        {
            PlayerStats? stats;

            if (client.Platform == BotPlatform.NapCat)
            {
                if (!long.TryParse(msg.SenderId, out long qq))
                {
                    await ReplyAsync(client, msg, "无法识别你的 QQ 号", ct);
                    return;
                }
                stats = await _players.GetByQqAsync(qq, ct);
            }
            else
            {
                string? playerId = await _bindings.ResolveAsync(client.Platform, msg.SenderId, ct);
                stats = playerId is null ? null : await _players.GetByPlayerIdAsync(playerId, ct);
            }

            await ReplyAsync(client, msg,
                stats is null ? "您没有绑定账号，请输入 /bd <Steam64>" : FormatStats(stats), ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "执行 [/me] 查询失败（平台 {Platform}，用户 {Sender}）", client.Platform, msg.SenderId);
            await ReplyAsync(client, msg, "查询失败，请检查数据库配置或稍后重试", ct);
        }
    }

    /// <summary>
    /// 需要「服务器索引 + 内容」的管理指令通用流程：校验参数 → 定位服务器 → 下发插件 TCP 指令。
    /// </summary>
    private async Task HandleServerCommandAsync(
        IBotClient client,
        BotIncomingMessage msg,
        BotCommandRequest request,
        int minArgs,
        Func<string[], string> buildPayload,
        string commandLabel,
        CancellationToken ct)
    {
        if (request.Args.Length < minArgs)
        {
            string usage = BotCommandCatalog.Resolve(commandLabel)?.Usage ?? $"/{commandLabel}";
            await ReplyAsync(client, msg, $"用法: {usage}", ct);
            return;
        }

        int? index = await ResolveServerIndexAsync(client, msg, request.Arg(0), ct);
        if (index is null)
            return;

        var server = GetServerByIndex(index.Value);
        if (server == null)
        {
            await ReplyAsync(client, msg, "服务器不在线", ct);
            return;
        }

        // args[0] 是服务器索引，交给插件的内容从 args[1] 起拼接
        var args = request.Args;
        string payload;
        try
        {
            payload = buildPayload(args);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "构造 [{Command}] 指令载荷失败", commandLabel);
            await ReplyAsync(client, msg, "参数解析失败，请检查用法", ct);
            return;
        }

        var resp = await _socket.SendAsync(server.ConnectHost, server.Port, payload, ct);
        if (resp == null)
        {
            _registry.MarkOffline(server.ConnectHost, server.Port);
            _log.LogWarning("命令 [{Command}] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）",
                commandLabel, server.Name, server.ConnectHost, server.Port);
            await ReplyAsync(client, msg, "服务器不在线", ct);
            return;
        }

        _log.LogInformation("命令 [{Command}] → [{ServerName}] {ConnectHost}:{Port} 执行成功",
            commandLabel, server.Name, server.ConnectHost, server.Port);
        await ReplyAsync(client, msg, resp, ct);
    }

    /// <summary>解析并校验服务器索引；返回 null 表示无效（此时已向用户回发提示）。</summary>
    private async Task<int?> ResolveServerIndexAsync(IBotClient client, BotIncomingMessage msg, string raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw.Trim(), out int index))
        {
            await ReplyAsync(client, msg, "服务器索引无效", ct);
            return null;
        }

        int onlineCount = _registry.Count;
        if (index < 1 || index > onlineCount)
        {
            _log.LogWarning("指令服务器索引 #{Index} 无效，当前在线 {Count} 台", index, onlineCount);
            await ReplyAsync(client, msg, "服务器索引无效", ct);
            return null;
        }

        return index;
    }

    // ---------------- 聚合查询 ----------------

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
                _log.LogWarning("命令 [cx] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）",
                    server.Name, server.ConnectHost, server.Port);
                return "";
            }

            _log.LogInformation("命令 [cx] → [{ServerName}] {ConnectHost}:{Port} 执行成功",
                server.Name, server.ConnectHost, server.Port);

            var m = System.Text.RegularExpressions.Regex.Match(resp, @"在线人数:(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                Interlocked.Add(ref totalOnline, n);

            return resp;
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        foreach (var r in results)
            sb.Append(r);

        sb.Append($"总在线人数: {totalOnline}\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
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
                _log.LogWarning("命令 [info] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）",
                    server.Name, server.ConnectHost, server.Port);
                return $"#{index + 1} 服不在线\n";
            }

            _log.LogInformation("命令 [info] → [{ServerName}] {ConnectHost}:{Port} 执行成功",
                server.Name, server.ConnectHost, server.Port);
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
            _log.LogWarning("命令 [list] → [{ServerName}] {ConnectHost}:{Port} 执行失败（连接超时/断开）",
                server.Name, server.ConnectHost, server.Port);
            return "服务器不在线";
        }

        _log.LogInformation("命令 [list] → [{ServerName}] {ConnectHost}:{Port} 执行成功",
            server.Name, server.ConnectHost, server.Port);
        return $"服务器 #{serverIndex} 玩家列表\n{resp}";
    }

    private ServerInfo? GetServerByIndex(int idx)
    {
        var list = _registry.GetSorted();
        if (idx < 1 || idx > list.Count)
            return null;
        return list[idx - 1];
    }

    // ---------------- 回复 ----------------

    /// <summary>
    /// 统一回复入口：按平台能力裁剪文本、分段发送，
    /// 并把消息 ID 作为被动回复凭据带给适配器（官方平台必需）。
    /// </summary>
    private async Task ReplyAsync(IBotClient client, BotIncomingMessage msg, string text, CancellationToken ct)
    {
        var chunks = BotCommandParser.SplitForPlatform(text, client.MaxTextLength, client.SupportsCqCode);
        if (chunks.Count == 0)
            return;

        var reply = msg.ToReplyContext();

        for (int i = 0; i < chunks.Count; i++)
        {
            var result = msg.IsGroup
                ? await client.SendGroupMessageAsync(msg.TargetId, chunks[i], reply, ct)
                : await client.SendPrivateMessageAsync(msg.TargetId, chunks[i], reply, ct);

            if (!result.Success)
            {
                _log.LogWarning("回复失败（{Scope} {Target}，第 {Index}/{Total} 段）：{Error}",
                    msg.IsGroup ? "群" : "私聊", msg.TargetId, i + 1, chunks.Count, result.Error);
                return;
            }
        }
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

    public static string GetVersionDetails(IBotClient? client = null)
    {
        var asm = typeof(CommandRouter).Assembly;
        var name = asm.GetName();
        var informationalVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? name.Version?.ToString()
            ?? "2.0.0";

        // 清理 git commit hash 等附加后缀（如 2.0.0+a1b2c3d -> 2.0.0）
        int plusIdx = informationalVersion.IndexOf('+');
        string cleanVersion = plusIdx > 0 ? informationalVersion[..plusIdx] : informationalVersion;

        var framework = asm.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? ".NET 8.0";
        if (framework.Contains("Version=v", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(framework, @"Version=v([0-9.]+)");
            if (match.Success)
                framework = $".NET {match.Groups[1].Value}";
        }

        string platformDesc = client is not null
            ? $"{client.DisplayName}（{(client.IsConnected ? "已连接" : "未连接")}）"
            : "Qcha Bot";

        var sb = new StringBuilder();
        sb.AppendLine("Qcha QQ Bot 版本详情");
        sb.AppendLine("-------------------------");
        sb.AppendLine($"机器人版本: v{cleanVersion}");
        sb.AppendLine($"接入平台: {platformDesc}");
        sb.AppendLine($"运行平台: {Environment.OSVersion.Platform} ({Environment.OSVersion.VersionString})");
        sb.AppendLine($"目标框架: {framework}");
        sb.AppendLine($"运行时环境: .NET {Environment.Version}");
        sb.AppendLine("核心特性: 多服自动联动 / 官方指令面板 / Web管理面板 / LocalAdmin托管 / 玩家数据统计");
        sb.AppendLine($"查询时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        return sb.ToString().TrimEnd();
    }
}
