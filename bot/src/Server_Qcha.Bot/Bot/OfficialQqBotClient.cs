using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;

namespace Server.Qcat.Bot;

/// <summary>
/// QQ 开放平台官方 Bot API v2 接入实现。
///
/// 接入流程（官方规范）：
/// <list type="number">
/// <item><c>POST {ApiBase}/app/getAppAccessToken</c> 用 AppID + ClientSecret 换取 access_token；</item>
/// <item><c>GET {ApiBase}/gateway</c> 取 WebSocket 接入点；</item>
/// <item>建立长连接后收到 <c>op 10 Hello</c>（含 heartbeat_interval）；</item>
/// <item>发送 <c>op 2 Identify</c>（token=QQBot xxx、intents、shard、properties）完成鉴权；
///       断线重连时改用 <c>op 6 Resume</c>（携带 session_id 与最后一次 seq）续接会话；</item>
/// <item>按间隔发送 <c>op 1 Heartbeat</c>（d 为最近一次收到的 s），收到 <c>op 11</c> 表示心跳成功。</item>
/// </list>
///
/// 与 NapCat 的硬性差异（已在本实现中处理）：
/// <list type="bullet">
/// <item>群、用户都是 OpenID，拿不到真实 QQ 号；</item>
/// <item>只能收到群里 @机器人 的消息，且平台已把 @前缀从 content 中剥离；</item>
/// <item>没有群列表 / 好友列表接口，因此这里用「首次被 @ 时记录的活跃群」充当可选项来源；</item>
/// <item>回复必须带触发事件的 msg_id（及递增的 msg_seq）才算被动回复，单条事件最多回复 5 次；</item>
/// <item>主动消息受额度限制，默认关闭。</item>
/// </list>
/// </summary>
public sealed class OfficialQqBotClient : BotClientHost, IBotClient
{
    /// <summary>官方限制：同一条入站消息最多被动回复 5 次（msg_seq 1..5）。</summary>
    private const int MaxReplySeq = 5;

    private const int OpDispatch = 0;
    private const int OpHeartbeat = 1;
    private const int OpIdentify = 2;
    private const int OpResume = 6;
    private const int OpReconnect = 7;
    private const int OpInvalidSession = 9;
    private const int OpHello = 10;
    private const int OpHeartbeatAck = 11;

    private readonly IOptionsMonitor<OfficialQqOptions> _opts;
    private readonly IOptionsMonitor<BotOptions> _botOpts;
    private readonly OfficialQqTokenProvider _token;
    private readonly CommandRouter _router;
    private readonly HttpClient _http;
    private readonly ILogger<OfficialQqBotClient> _log;

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>同一 msg_id 已使用的 msg_seq，避免官方去重导致发送失败。</summary>
    private readonly ConcurrentDictionary<string, (int Seq, DateTimeOffset At)> _replySeq = new();

    /// <summary>观察到的群（官方无群列表接口，靠收到群消息时登记）。</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _observedGroups = new();

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _sessionCts;
    private CancellationTokenSource? _waitReconnectCts;
    private int _lastSeq;

    public OfficialQqBotClient(
        IOptionsMonitor<OfficialQqOptions> opts,
        IOptionsMonitor<BotOptions> botOpts,
        OfficialQqTokenProvider token,
        CommandRouter router,
        HttpClient http,
        ILogger<OfficialQqBotClient> log)
    {
        _opts = opts;
        _botOpts = botOpts;
        _token = token;
        _router = router;
        _http = http;
        _log = log;
    }

    // ---------------- IBotClient ----------------

    public BotPlatform Platform => BotPlatform.OfficialQq;

    public string DisplayName => "QQ 官方 Bot API";

    public bool IsConnected { get; private set; }

    /// <summary>官方平台不支持 OneBot CQ 码。</summary>
    public bool SupportsCqCode => false;

    public int MaxTextLength => Math.Clamp(_opts.CurrentValue.MaxTextLength, 100, 4000);

    public bool SupportsActivePush => _opts.CurrentValue.AllowActivePush;

    public DateTime? LastConnectedAt { get; private set; }

    public DateTime? LastDisconnectedAt { get; private set; }

    public string? LastError { get; private set; }

    public int CurrentAttempt { get; private set; }

    public int? NextReconnectInSeconds { get; private set; }

    public string? ConnectedUserId { get; private set; }

    public string? ConnectedNickname { get; private set; }

    public string? Endpoint { get; private set; }

    /// <summary>当前会话 ID（Resume 用）。</summary>
    public string? SessionId { get; private set; }

    /// <summary>最近活动过的群（未显式配置推送目标时的兜底来源）。</summary>
    public string? MostRecentGroupOpenId
    {
        get
        {
            if (_observedGroups.IsEmpty)
                return null;

            return _observedGroups.OrderByDescending(kv => kv.Value).First().Key;
        }
    }

    /// <summary>
    /// 官方不提供群列表接口，这里返回「曾向机器人发过消息的群」，
    /// 便于在面板中挑选 group_openid 配置为通知目标。
    /// </summary>
    public Task<IReadOnlyList<BotGroupSummary>> GetGroupsAsync(CancellationToken ct = default)
    {
        var list = _observedGroups
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new BotGroupSummary(kv.Key, "(官方平台不提供群名称)", 0, 0))
            .ToList();

        return Task.FromResult<IReadOnlyList<BotGroupSummary>>(list);
    }

    /// <summary>官方不提供好友列表接口。</summary>
    public Task<IReadOnlyList<BotFriendSummary>> GetFriendsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BotFriendSummary>>(Array.Empty<BotFriendSummary>());

    public async Task<BotSendResult> SendGroupMessageAsync(string groupId, string text, BotReplyContext? reply = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return BotSendResult.Fail("群 OpenID 为空");

        string url = $"{_opts.CurrentValue.ResolveApiBase()}/v2/groups/{Uri.EscapeDataString(groupId)}/messages";
        return await SendChunkedAsync(url, text, reply, ct);
    }

    public async Task<BotSendResult> SendPrivateMessageAsync(string userId, string text, BotReplyContext? reply = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BotSendResult.Fail("用户 OpenID 为空");

        string url = $"{_opts.CurrentValue.ResolveApiBase()}/v2/users/{Uri.EscapeDataString(userId)}/messages";
        return await SendChunkedAsync(url, text, reply, ct);
    }

    public Task ReconnectAsync()
    {
        _log.LogInformation("收到主动重新连接请求，正在重置官方 QQ 连接...");

        lock (_stateLock)
        {
            CurrentAttempt = 0;
            NextReconnectInSeconds = null;
        }

        // 清空会话信息，强制走全新建连
        SessionId = null;
        _lastSeq = 0;

        try { _ws?.Abort(); } catch { /* ignore */ }
        _sessionCts?.Cancel();
        _waitReconnectCts?.Cancel();

        return Task.CompletedTask;
    }

    // ---------------- 连接主循环 ----------------

    protected override async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        int attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            lock (_stateLock)
            {
                CurrentAttempt = attempt;
                NextReconnectInSeconds = null;
            }

            var opts = _opts.CurrentValue;
            bool wasRunning = false;

            try
            {
                if (!opts.IsConfigured)
                {
                    LastError = "未配置 AppId / ClientSecret";
                    _log.LogWarning("官方 QQ 模式缺少 AppId / ClientSecret，等待配置补全后自动连接");
                }
                else
                {
                    await RunSessionAsync(stoppingToken);
                    wasRunning = true;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _log.LogWarning("官方 QQ 连接中断：{Error}", ex.Message);
            }
            finally
            {
                IsConnected = false;
                LastDisconnectedAt = DateTime.UtcNow;
                CleanupSocket();
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            if (wasRunning)
                attempt = 0;

            int delaySec = Math.Min(
                Math.Max(opts.ReconnectDelaySeconds, 1) * Math.Max(attempt, 1),
                Math.Max(opts.MaxReconnectDelaySeconds, 1));

            lock (_stateLock)
            {
                NextReconnectInSeconds = delaySec;
            }

            _log.LogInformation("{Delay} 秒后重试连接 QQ 官方 Bot API ...", delaySec);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _waitReconnectCts = cts;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                _log.LogInformation("重连等待已被主动唤醒，立即开始连接");
                attempt = 0;
            }
            finally
            {
                _waitReconnectCts = null;
                lock (_stateLock)
                {
                    NextReconnectInSeconds = null;
                }
            }
        }

        _log.LogInformation("QQ 官方 Bot API 连接服务已停止");
    }

    private async Task RunSessionAsync(CancellationToken stoppingToken)
    {
        var opts = _opts.CurrentValue;

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _sessionCts = sessionCts;
        var ct = sessionCts.Token;

        string accessToken = await _token.GetAsync(ct);

        // 1) 取网关地址
        string gatewayUrl = await GetGatewayUrlAsync(accessToken, ct);
        Endpoint = gatewayUrl;
        _log.LogInformation("QQ 官方网关地址：{Url}", gatewayUrl);

        // 2) 建立长连接
        var ws = new ClientWebSocket();
        _ws = ws;
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        await ws.ConnectAsync(new Uri(gatewayUrl), ct);

        // 3) Hello：拿心跳间隔
        var (helloOp, helloDoc) = await ReceiveAsync(ws, ct);
        if (helloOp != OpHello || helloDoc is null)
            throw new InvalidOperationException($"网关首帧不是 Hello，op={helloOp}");

        int heartbeatMs = 45000;
        if (helloDoc.Value.TryGetProperty("d", out var helloD) &&
            helloD.TryGetProperty("heartbeat_interval", out var hbEl) &&
            hbEl.TryGetInt32(out int hb))
        {
            heartbeatMs = hb;
        }

        _log.LogInformation("已连接 QQ 官方网关，心跳间隔 {Interval} ms", heartbeatMs);

        // 4) 鉴权：有会话则 Resume，否则 Identify
        bool resuming = !string.IsNullOrEmpty(SessionId);
        await SendIdentifyOrResumeAsync(ws, accessToken, resuming, ct);

        IsConnected = true;
        LastConnectedAt = DateTime.UtcNow;
        LastError = null;

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = HeartbeatLoopAsync(ws, heartbeatMs, heartbeatCts.Token);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (op, doc) = await ReceiveAsync(ws, ct);

                switch (op)
                {
                    case OpDispatch when doc is not null:
                        await HandleDispatchAsync(doc.Value, ct);
                        break;

                    case OpHeartbeatAck:
                        _log.LogDebug("心跳 ACK（seq={Seq}）", _lastSeq);
                        break;

                    case OpReconnect:
                        _log.LogWarning("平台要求客户端重新连接（op 7）");
                        return;

                    case OpInvalidSession:
                        _log.LogWarning("会话失效（op 9），将丢弃 session 并重新鉴权");
                        SessionId = null;
                        _lastSeq = 0;
                        return;

                    case OpHello:
                        _log.LogDebug("收到额外的 Hello 帧，忽略");
                        break;

                    default:
                        _log.LogDebug("收到未处理的 opcode {Op}", op);
                        break;
                }
            }
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { /* ignore */ }
        }
    }

    private async Task HeartbeatLoopAsync(ClientWebSocket ws, int intervalMs, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(Math.Max(intervalMs, 5000), ct);
                await SendRawAsync(ws, JsonSerializer.Serialize(new { op = OpHeartbeat, d = _lastSeq }), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        catch (Exception ex)
        {
            _log.LogDebug("心跳循环结束：{Error}", ex.Message);
        }
    }

    // ---------------- 事件分发 ----------------

    private async Task HandleDispatchAsync(JsonElement root, CancellationToken ct)
    {
        string? type = root.TryGetProperty("t", out var tEl) ? tEl.GetString() : null;
        if (string.IsNullOrEmpty(type) || !root.TryGetProperty("d", out var d))
            return;

        switch (type)
        {
            case "READY":
            {
                SessionId = d.TryGetProperty("session_id", out var sid) ? sid.GetString() : null;
                if (d.TryGetProperty("user", out var user))
                {
                    ConnectedUserId = user.TryGetProperty("id", out var uid) ? uid.GetString() : null;
                    ConnectedNickname = user.TryGetProperty("username", out var un) ? un.GetString() : null;
                }
                _log.LogInformation("{DisplayName} 鉴权成功，机器人 {Name}（AppID {AppId}），session={Session}",
                    DisplayName, ConnectedNickname ?? "?", ConnectedUserId ?? "?", SessionId ?? "-");
                break;
            }

            case "RESUMED":
                _log.LogInformation("会话已恢复（RESUMED），session={Session}", SessionId ?? "-");
                break;

            case "GROUP_AT_MESSAGE_CREATE":
                await HandleGroupMessageAsync(d, ct);
                break;

            case "C2C_MESSAGE_CREATE":
                await HandleC2CMessageAsync(d, ct);
                break;

            default:
                _log.LogDebug("忽略未订阅的事件类型：{Type}", type);
                break;
        }
    }

    private async Task HandleGroupMessageAsync(JsonElement d, CancellationToken ct)
    {
        string groupOpenId = d.TryGetProperty("group_openid", out var g) ? g.GetString() ?? "" : "";
        if (groupOpenId.Length == 0)
            return;

        _observedGroups[groupOpenId] = DateTimeOffset.Now;

        var opts = _opts.CurrentValue;
        if (opts.AllowedGroupOpenIds.Length > 0 && !opts.AllowedGroupOpenIds.Contains(groupOpenId))
        {
            _log.LogDebug("群 {GroupOpenId} 不在白名单内，忽略该消息", groupOpenId);
            return;
        }

        // 官方已把 @机器人 前缀从 content 中剥离
        string text = d.TryGetProperty("content", out var c) ? (c.GetString() ?? "").Trim() : "";
        if (text.Length == 0)
            return;

        string senderId = "";
        string senderName = "";
        bool isAdmin = false;

        if (d.TryGetProperty("author", out var author))
        {
            if (author.TryGetProperty("member_openid", out var mo))
                senderId = mo.GetString() ?? "";
            if (senderId.Length == 0 && author.TryGetProperty("id", out var aid))
                senderId = aid.GetString() ?? "";

            if (author.TryGetProperty("username", out var un))
                senderName = un.GetString() ?? "";

            // 官方群角色：member / admin / owner
            string role = author.TryGetProperty("member_role", out var r) ? (r.GetString() ?? "") : "";
            isAdmin = role.Equals("admin", StringComparison.OrdinalIgnoreCase)
                   || role.Equals("owner", StringComparison.OrdinalIgnoreCase);
        }

        // 兜底白名单：官方在某些场景下不返回 member_role
        if (!isAdmin && opts.AdminOpenIds.Length > 0 && senderId.Length > 0)
            isAdmin = opts.AdminOpenIds.Contains(senderId);

        var incoming = new BotIncomingMessage
        {
            Platform = BotPlatform.OfficialQq,
            IsGroup = true,
            TargetId = groupOpenId,
            SenderId = senderId,
            SenderName = senderName.Length > 0 ? senderName : $"群成员 {Shorten(senderId)}",
            IsAdmin = isAdmin,
            Text = text,
            MessageId = d.TryGetProperty("id", out var idEl) ? idEl.GetString() : null,
        };

        await _router.HandleAsync(this, incoming, ct);
    }

    private async Task HandleC2CMessageAsync(JsonElement d, CancellationToken ct)
    {
        string text = d.TryGetProperty("content", out var c) ? (c.GetString() ?? "").Trim() : "";
        if (text.Length == 0)
            return;

        string senderId = "";
        string senderName = "";

        if (d.TryGetProperty("author", out var author))
        {
            if (author.TryGetProperty("user_openid", out var uo))
                senderId = uo.GetString() ?? "";
            if (senderId.Length == 0 && author.TryGetProperty("id", out var aid))
                senderId = aid.GetString() ?? "";
            if (author.TryGetProperty("username", out var un))
                senderName = un.GetString() ?? "";
        }

        if (senderId.Length == 0)
            return;

        var opts = _opts.CurrentValue;
        // 单聊没有群角色，是否放行管理指令由 AdminOpenIds 决定；空列表时视为未授权
        bool isAdmin = opts.AdminOpenIds.Length > 0 && opts.AdminOpenIds.Contains(senderId);

        var incoming = new BotIncomingMessage
        {
            Platform = BotPlatform.OfficialQq,
            IsGroup = false,
            TargetId = senderId,
            SenderId = senderId,
            SenderName = senderName.Length > 0 ? senderName : $"用户 {Shorten(senderId)}",
            IsAdmin = isAdmin,
            Text = text,
            MessageId = d.TryGetProperty("id", out var idEl) ? idEl.GetString() : null,
        };

        await _router.HandleAsync(this, incoming, ct);
    }

    // ---------------- HTTP ----------------

    private async Task<string> GetGatewayUrlAsync(string accessToken, CancellationToken ct)
    {
        var opts = _opts.CurrentValue;
        string url = $"{opts.ResolveApiBase()}/gateway";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"QQBot {accessToken}");
        req.Headers.TryAddWithoutValidation("User-Agent", "Server_Qcha.Qcha/1.0");

        using var resp = await _http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取网关地址失败：HTTP {(int)resp.StatusCode}，响应：{Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("url", out var urlEl) || urlEl.GetString() is not { Length: > 0 } gateway)
            throw new InvalidOperationException($"网关响应缺少 url 字段：{Truncate(body)}");

        return gateway;
    }

    private async Task SendIdentifyOrResumeAsync(ClientWebSocket ws, string accessToken, bool resuming, CancellationToken ct)
    {
        var opts = _opts.CurrentValue;

        object payload;

        if (resuming)
        {
            payload = new
            {
                op = OpResume,
                d = new
                {
                    token = $"QQBot {accessToken}",
                    session_id = SessionId,
                    seq = _lastSeq,
                },
            };
            _log.LogInformation("发送 Resume（op 6），session={Session}，seq={Seq}", SessionId, _lastSeq);
        }
        else
        {
            payload = new
            {
                op = OpIdentify,
                d = new
                {
                    token = $"QQBot {accessToken}",
                    intents = opts.Intents,
                    shard = new[] { opts.ShardIndex, Math.Max(opts.ShardTotal, 1) },
                    properties = new
                    {
                        os = Environment.OSVersion.Platform.ToString(),
                        browser = "Server_Qcha.Qcha",
                        device = "Server_Qcha.Qcha",
                    },
                },
            };
            _log.LogInformation("发送 Identify（op 2），intents={Intents}，shard=[{Index},{Total}]",
                opts.Intents, opts.ShardIndex, Math.Max(opts.ShardTotal, 1));
        }

        await SendRawAsync(ws, JsonSerializer.Serialize(payload), ct);
    }

    private async Task<BotSendResult> SendChunkedAsync(string url, string text, BotReplyContext? reply, CancellationToken ct)
    {
        var opts = _opts.CurrentValue;

        var chunks = BotCommandParser.SplitForPlatform(text, MaxTextLength, supportsCqCode: false);
        if (chunks.Count == 0)
            return BotSendResult.Fail("消息内容为空");

        bool passive = reply is { MessageId: { Length: > 0 } };

        if (!passive && !opts.AllowActivePush)
            return BotSendResult.Fail("当前为主动消息且未开启主动推送（官方平台主动消息受配额限制，可在设置中开启）");

        if (passive && chunks.Count > MaxReplySeq)
        {
            _log.LogWarning("回复内容将被截断为 {Max} 段（官方限制同一消息最多被动回复 {Max} 次）", MaxReplySeq, MaxReplySeq);
            chunks = chunks.Take(MaxReplySeq).ToList();
        }

        string? firstId = null;

        foreach (var chunk in chunks)
        {
            var (ok, id, err) = await PostMessageAsync(url, chunk, reply, ct);
            if (!ok)
                return BotSendResult.Fail(err ?? "发送失败");

            firstId ??= id;
        }

        return BotSendResult.Ok(firstId);
    }

    private async Task<(bool ok, string? id, string? error)> PostMessageAsync(string url, string content, BotReplyContext? reply, CancellationToken ct)
    {
        string accessToken;
        try
        {
            accessToken = await _token.GetAsync(ct);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }

        var body = new Dictionary<string, object?>
        {
            ["content"] = content,
            ["msg_type"] = 0,
        };

        if (reply is { MessageId: { Length: > 0 } msgId })
        {
            int seq = NextReplySeq(msgId);
            if (seq > MaxReplySeq)
                return (false, null, $"同一条消息的被动回复已达上限（{MaxReplySeq} 次），请稍后再试");

            body["msg_id"] = msgId;
            body["msg_seq"] = seq;

            if (reply.EventId is { Length: > 0 } eventId)
                body["event_id"] = eventId;
        }

        string json = JsonSerializer.Serialize(body);

        await _sendGate.WaitAsync(ct);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("Authorization", $"QQBot {accessToken}");
            req.Headers.TryAddWithoutValidation("User-Agent", "Server_Qcha.Qcha/1.0");

            using var resp = await _http.SendAsync(req, ct);
            string respBody = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                // 鉴权失效时作废 token，下一次自动重新申请
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    _token.Invalidate();

                _log.LogWarning("发送官方消息失败：HTTP {Status}，响应：{Body}", (int)resp.StatusCode, Truncate(respBody));
                return (false, null, DescribeError(respBody, (int)resp.StatusCode));
            }

            string? messageId = null;
            try
            {
                using var doc = JsonDocument.Parse(respBody);
                if (doc.RootElement.TryGetProperty("id", out var idEl))
                    messageId = idEl.GetString();
            }
            catch (JsonException)
            {
                // 响应不是 JSON 也不影响发送成功的事实
            }

            return (true, messageId, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "发送官方消息异常：{Error}", ex.Message);
            return (false, null, ex.Message);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>取该 msg_id 的下一个 msg_seq（官方要求同一 msg_id 下递增且不重复）。</summary>
    private int NextReplySeq(string msgId)
    {
        CleanupReplySeq();

        return _replySeq.AddOrUpdate(
            msgId,
            _ => (1, DateTimeOffset.Now),
            (_, old) => (old.Seq + 1, DateTimeOffset.Now)).Seq;
    }

    private void CleanupReplySeq()
    {
        if (_replySeq.Count < 256)
            return;

        var cutoff = DateTimeOffset.Now.AddMinutes(-5);
        foreach (var kv in _replySeq)
        {
            if (kv.Value.At < cutoff)
                _replySeq.TryRemove(kv.Key, out _);
        }
    }

    // ---------------- WebSocket 读写 ----------------

    private async Task SendRawAsync(ClientWebSocket ws, string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private async Task<(int op, JsonElement? root)> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        string? json = await ReceiveFullMessageAsync(ws, ct);
        if (json is null)
            throw new WebSocketException("WebSocket 连接已关闭");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.Clone();

        int op = root.TryGetProperty("op", out var opEl) && opEl.TryGetInt32(out int opVal) ? opVal : -1;

        if (op == OpDispatch && root.TryGetProperty("s", out var sEl) && sEl.TryGetInt32(out int seq))
            _lastSeq = seq;

        return (op, root);
    }

    private static async Task<string?> ReceiveFullMessageAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            ms.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
                break;

            if (ms.Length > 4 * 1024 * 1024)
                throw new InvalidOperationException("单帧数据超过 4MB，疑似协议异常");
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private void CleanupSocket()
    {
        var ws = _ws;
        _ws = null;

        if (ws is null)
            return;

        try
        {
            if (ws.State == WebSocketState.Open)
                _ = ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        }
        catch { /* ignore */ }

        try { ws.Dispose(); } catch { /* ignore */ }
    }

    // ---------------- 工具 ----------------

    private static string DescribeError(string body, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            string? code = root.TryGetProperty("code", out var c) ? c.ToString() : null;

            if (!string.IsNullOrEmpty(message))
                return $"HTTP {statusCode}：{message}（code={code}）";
        }
        catch (JsonException)
        {
            // 落到通用描述
        }

        return $"HTTP {statusCode}：{Truncate(body)}";
    }

    private static string Truncate(string value)
        => string.IsNullOrEmpty(value) ? "(空)" : (value.Length > 300 ? value[..300] + "..." : value);

    private static string Shorten(string openId)
        => string.IsNullOrEmpty(openId) ? "?" : (openId.Length <= 8 ? openId : openId[..8] + "…");

    // ---------------- 官方指令面板 API (/v2/panels) ----------------

    /// <summary>
    /// 查询官方指令面板列表。
    /// scope 为 null 时并发拉取 group（群聊）和 c2c（单聊）两个场景的面板列表。
    /// </summary>
    public async Task<IReadOnlyList<OfficialPanelRecord>> GetPanelsAsync(string? scope = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(scope))
        {
            var groupTask = GetPanelsForScopeAsync("group", ct);
            var c2cTask = GetPanelsForScopeAsync("c2c", ct);
            await Task.WhenAll(groupTask, c2cTask);

            var combined = new List<OfficialPanelRecord>();
            combined.AddRange(groupTask.Result);
            combined.AddRange(c2cTask.Result);
            return combined;
        }

        return await GetPanelsForScopeAsync(scope, ct);
    }

    private async Task<IReadOnlyList<OfficialPanelRecord>> GetPanelsForScopeAsync(string scope, CancellationToken ct)
    {
        string token = await _token.GetAsync(ct);
        var opts = _opts.CurrentValue;
        string url = $"{opts.ResolveApiBase()}/v2/panels?scope={Uri.EscapeDataString(scope)}&limit=50";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"QQBot {token}");
        req.Headers.TryAddWithoutValidation("User-Agent", "Server_Qcha.Qcha/1.0");

        using var resp = await _http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                _token.Invalidate();
            throw new InvalidOperationException($"查询 {scope} 指令面板失败：{DescribeError(body, (int)resp.StatusCode)}");
        }

        var result = JsonSerializer.Deserialize<OfficialPanelsQueryResult>(body);
        return result?.Records ?? (IReadOnlyList<OfficialPanelRecord>)Array.Empty<OfficialPanelRecord>();
    }

    /// <summary>
    /// 创建官方指令面板（POST /v2/panels）。
    /// </summary>
    public async Task<string> CreatePanelAsync(
        string scope,
        string targetType,
        string remark,
        IReadOnlyList<BotOfficialPanelItem> items,
        CancellationToken ct = default)
    {
        string token = await _token.GetAsync(ct);
        var opts = _opts.CurrentValue;
        string url = $"{opts.ResolveApiBase()}/v2/panels";

        var payload = new
        {
            scope,
            target_type = targetType,
            panel = new
            {
                remark,
                items = items.Select(i => new
                {
                    name = i.Name,
                    desc = i.Desc,
                    type = i.Type,
                    only_admin = i.OnlyAdmin,
                    link = i.Link
                }).ToArray()
            }
        };

        string json = JsonSerializer.Serialize(payload);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"QQBot {token}");
        req.Headers.TryAddWithoutValidation("User-Agent", "Server_Qcha.Qcha/1.0");

        using var resp = await _http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                _token.Invalidate();
            throw new InvalidOperationException($"创建 {scope} 指令面板失败：{DescribeError(body, (int)resp.StatusCode)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("panel_id", out var idEl) && idEl.GetString() is { Length: > 0 } panelId)
            return panelId;

        throw new InvalidOperationException($"创建面板成功但响应缺少 panel_id：{Truncate(body)}");
    }

    /// <summary>
    /// 更新官方指令面板配置（PUT /v2/panels/{panel_id}）。
    /// </summary>
    public async Task<int> UpdatePanelAsync(
        string panelId,
        string remark,
        IReadOnlyList<BotOfficialPanelItem> items,
        CancellationToken ct = default)
    {
        string token = await _token.GetAsync(ct);
        var opts = _opts.CurrentValue;
        string url = $"{opts.ResolveApiBase()}/v2/panels/{Uri.EscapeDataString(panelId)}";

        var payload = new
        {
            panel = new
            {
                remark,
                items = items.Select(i => new
                {
                    name = i.Name,
                    desc = i.Desc,
                    type = i.Type,
                    only_admin = i.OnlyAdmin,
                    link = i.Link
                }).ToArray()
            }
        };

        string json = JsonSerializer.Serialize(payload);

        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"QQBot {token}");
        req.Headers.TryAddWithoutValidation("User-Agent", "Server_Qcha.Qcha/1.0");

        using var resp = await _http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                _token.Invalidate();
            throw new InvalidOperationException($"更新指令面板 {panelId} 失败：{DescribeError(body, (int)resp.StatusCode)}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("version", out var verEl) && verEl.TryGetInt32(out int ver) ? ver : 1;
    }

    /// <summary>
    /// 删除官方指令面板（DELETE /v2/panels/{panel_id}）。
    /// </summary>
    public async Task<bool> DeletePanelAsync(string panelId, CancellationToken ct = default)
    {
        string token = await _token.GetAsync(ct);
        var opts = _opts.CurrentValue;
        string url = $"{opts.ResolveApiBase()}/v2/panels/{Uri.EscapeDataString(panelId)}";

        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"QQBot {token}");
        req.Headers.TryAddWithoutValidation("User-Agent", "Server_Qcha.Qcha/1.0");

        using var resp = await _http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                _token.Invalidate();
            throw new InvalidOperationException($"删除指令面板 {panelId} 失败：{DescribeError(body, (int)resp.StatusCode)}");
        }

        return true;
    }

    /// <summary>
    /// 一键同步官方指令面板：
    /// 根据当前项目的指令库（BotCommandCatalog），分别更新或创建：
    /// 1. group（群聊）：全局（target_type=all），包含普通指令与管理指令（管理指令标记 only_admin=true）；
    /// 2. c2c（单聊）：全局（target_type=all），仅包含普通玩家指令。
    /// </summary>
    public async Task<OfficialPanelSyncResult> SyncCommandPanelsAsync(CancellationToken ct = default)
    {
        try
        {
            var groupItems = BotCommandCatalog.BuildOfficialPanelItems(isGroup: true);
            var c2cItems = BotCommandCatalog.BuildOfficialPanelItems(isGroup: false);

            // 1. 获取现有面板列表
            var existingGroupPanels = await GetPanelsForScopeAsync("group", ct);
            var existingC2cPanels = await GetPanelsForScopeAsync("c2c", ct);

            // 2. 处理群聊全局面板
            var existingGlobalGroup = existingGroupPanels.FirstOrDefault(p =>
                string.Equals(p.TargetType, "all", StringComparison.OrdinalIgnoreCase));

            string groupPanelId;
            string groupAction;
            if (existingGlobalGroup is not null && !string.IsNullOrEmpty(existingGlobalGroup.PanelId))
            {
                groupPanelId = existingGlobalGroup.PanelId;
                await UpdatePanelAsync(groupPanelId, "Qcha群聊指令面板", groupItems, ct);
                groupAction = "更新现有全局面板";
            }
            else
            {
                groupPanelId = await CreatePanelAsync("group", "all", "Qcha群聊指令面板", groupItems, ct);
                groupAction = "新建全局面板";
            }

            // 3. 处理单聊全局面板
            var existingGlobalC2c = existingC2cPanels.FirstOrDefault(p =>
                string.Equals(p.TargetType, "all", StringComparison.OrdinalIgnoreCase));

            string c2cPanelId;
            string c2cAction;
            if (existingGlobalC2c is not null && !string.IsNullOrEmpty(existingGlobalC2c.PanelId))
            {
                c2cPanelId = existingGlobalC2c.PanelId;
                await UpdatePanelAsync(c2cPanelId, "Qcha单聊指令面板", c2cItems, ct);
                c2cAction = "更新现有全局面板";
            }
            else
            {
                c2cPanelId = await CreatePanelAsync("c2c", "all", "Qcha单聊指令面板", c2cItems, ct);
                c2cAction = "新建全局面板";
            }

            _log.LogInformation("成功同步 QQ 官方指令面板：群聊面板 {GroupPanelId}（{GroupAction}，{GroupCount} 项），单聊面板 {C2cPanelId}（{C2cAction}，{C2cCount} 项）",
                groupPanelId, groupAction, groupItems.Count, c2cPanelId, c2cAction, c2cItems.Count);

            return new OfficialPanelSyncResult(
                Success: true,
                GroupPanelId: groupPanelId,
                GroupAction: groupAction,
                GroupItemCount: groupItems.Count,
                C2cPanelId: c2cPanelId,
                C2cAction: c2cAction,
                C2cItemCount: c2cItems.Count,
                Message: "指令面板已成功同步到 QQ 开放平台");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "同步 QQ 官方指令面板失败：{Error}", ex.Message);
            return new OfficialPanelSyncResult(
                Success: false,
                GroupPanelId: null,
                GroupAction: null,
                GroupItemCount: 0,
                C2cPanelId: null,
                C2cAction: null,
                C2cItemCount: 0,
                Message: ex.Message);
        }
    }
}

// ---------------- 官方指令面板 DTO 模型 ----------------

public sealed class OfficialPanelItemDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("desc")]
    public string Desc { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "command";

    [JsonPropertyName("only_admin")]
    public bool OnlyAdmin { get; set; }

    [JsonPropertyName("link")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Link { get; set; }
}

public sealed class OfficialPanelConfigDto
{
    [JsonPropertyName("items")]
    public List<OfficialPanelItemDto> Items { get; set; } = new();

    [JsonPropertyName("remark")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Remark { get; set; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Version { get; set; }
}

public sealed class OfficialPanelRecord
{
    [JsonPropertyName("panel_id")]
    public string PanelId { get; set; } = "";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonPropertyName("target_type")]
    public string TargetType { get; set; } = "";

    [JsonPropertyName("panel")]
    public OfficialPanelConfigDto Panel { get; set; } = new();

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }
}

public sealed class OfficialPanelsQueryResult
{
    [JsonPropertyName("records")]
    public List<OfficialPanelRecord>? Records { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }

    [JsonPropertyName("is_end")]
    public bool? IsEnd { get; set; }
}

public sealed record OfficialPanelSyncResult(
    bool Success,
    string? GroupPanelId,
    string? GroupAction,
    int GroupItemCount,
    string? C2cPanelId,
    string? C2cAction,
    int C2cItemCount,
    string? Message);

