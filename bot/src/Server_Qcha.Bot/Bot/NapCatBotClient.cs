using EleCho.GoCqHttpSdk;
using EleCho.GoCqHttpSdk.Message;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;

namespace Server.Qcat.Bot;

/// <summary>
/// NapCat（OneBot 11）接入实现。
///
/// 由原 <c>GoCqHttpBotService</c> 重构而来，行为保持一致：
/// 主动连接正向 WebSocket、指数退避重连、可被面板唤醒立即重连；
/// 新增的能力是把 OneBot 事件翻译成平台无关的 <see cref="BotIncomingMessage"/>，
/// 并实现 <see cref="IBotClient"/> 供命令路由与通知推送统一调用。
/// </summary>
public sealed class NapCatBotClient : BotClientHost, IBotClient
{
    /// <summary>OneBot 文本消息实际可承载的长度（QQ 侧限制，留出余量）。</summary>
    private const int NapCatTextLimit = 4000;

    private readonly IOptionsMonitor<GoCqHttpOptions> _optsMonitor;
    private readonly IOptionsMonitor<BotOptions> _botOptsMonitor;
    private readonly CommandRouter _router;
    private readonly ILogger<NapCatBotClient> _log;

    private readonly object _stateLock = new();
    private CancellationTokenSource? _waitReconnectCts;
    private volatile CqWsSession? _session;

    public NapCatBotClient(
        IOptionsMonitor<GoCqHttpOptions> optsMonitor,
        IOptionsMonitor<BotOptions> botOptsMonitor,
        CommandRouter router,
        ILogger<NapCatBotClient> log)
    {
        _optsMonitor = optsMonitor;
        _botOptsMonitor = botOptsMonitor;
        _router = router;
        _log = log;
    }

    // ---------------- IBotClient ----------------

    public BotPlatform Platform => BotPlatform.NapCat;

    public string DisplayName => "NapCat / OneBot 11";

    public bool IsConnected { get; private set; }

    public bool SupportsCqCode => true;

    public int MaxTextLength => NapCatTextLimit;

    public bool SupportsActivePush => true;

    public DateTime? LastConnectedAt { get; private set; }

    public DateTime? LastDisconnectedAt { get; private set; }

    public string? LastError { get; private set; }

    public int CurrentAttempt { get; private set; }

    public int? NextReconnectInSeconds { get; private set; }

    public string? ConnectedUserId { get; private set; }

    public string? ConnectedNickname { get; private set; }

    public string? Endpoint => _optsMonitor.CurrentValue.WsBaseUri;

    public async Task<IReadOnlyList<BotGroupSummary>> GetGroupsAsync(CancellationToken ct = default)
    {
        var session = _session;
        if (session == null || !IsConnected)
            return Array.Empty<BotGroupSummary>();

        try
        {
            var res = await session.GetGroupListAsync();
            if (res?.Groups == null)
                return Array.Empty<BotGroupSummary>();

            return res.Groups
                .Select(g => new BotGroupSummary(g.GroupId.ToString(), g.GroupName, (int)g.MemberCount, (int)g.MaxMemberCount))
                .OrderBy(g => g.GroupName)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning("获取已加入群组列表失败: {Error}", ex.Message);
            return Array.Empty<BotGroupSummary>();
        }
    }

    public async Task<IReadOnlyList<BotFriendSummary>> GetFriendsAsync(CancellationToken ct = default)
    {
        var session = _session;
        if (session == null || !IsConnected)
            return Array.Empty<BotFriendSummary>();

        try
        {
            var res = await session.GetFriendListAsync();
            if (res?.Friends == null)
                return Array.Empty<BotFriendSummary>();

            return res.Friends
                .Select(f => new BotFriendSummary(f.UserId.ToString(), f.Nickname, f.Remark))
                .OrderBy(f => f.Nickname)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning("获取好友列表失败: {Error}", ex.Message);
            return Array.Empty<BotFriendSummary>();
        }
    }

    public async Task<BotSendResult> SendGroupMessageAsync(string groupId, string text, BotReplyContext? reply = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return BotSendResult.Fail("消息内容为空");

        if (!long.TryParse(groupId, out long gid))
            return BotSendResult.Fail($"群号格式不正确：{groupId}");

        var session = _session;
        if (session == null || !IsConnected)
            return BotSendResult.Fail($"{DisplayName} 未连接");

        try
        {
            var res = await session.SendGroupMessageAsync(gid, new CqMessage(text));
            return BotSendResult.Ok(res?.MessageId.ToString());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "发送群消息失败 (群 {GroupId}): {Error}", gid, ex.Message);
            return BotSendResult.Fail(ex.Message);
        }
    }

    public async Task<BotSendResult> SendPrivateMessageAsync(string userId, string text, BotReplyContext? reply = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return BotSendResult.Fail("消息内容为空");

        if (!long.TryParse(userId, out long uid))
            return BotSendResult.Fail($"用户标识格式不正确：{userId}");

        var session = _session;
        if (session == null || !IsConnected)
            return BotSendResult.Fail($"{DisplayName} 未连接");

        try
        {
            var res = await session.SendPrivateMessageAsync(uid, new CqMessage(text));
            return BotSendResult.Ok(res?.MessageId.ToString());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "发送私聊消息失败 (用户 {UserId}): {Error}", uid, ex.Message);
            return BotSendResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 主动触发重新连接。若当前已有连接会先断开；若正在等待重连倒计时会立即唤醒。
    /// </summary>
    public async Task ReconnectAsync()
    {
        _log.LogInformation("收到主动重新连接请求，正在重置当前连接...");
        lock (_stateLock)
        {
            CurrentAttempt = 0;
            NextReconnectInSeconds = null;
        }

        var session = _session;
        if (session != null)
        {
            try
            {
                await session.StopAsync();
            }
            catch (Exception ex)
            {
                _log.LogDebug("断开旧连接时忽略异常: {Error}", ex.Message);
            }
        }

        _waitReconnectCts?.Cancel();
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

            CqWsSession? session = null;
            bool wasRunning = false;
            var currentOpts = _optsMonitor.CurrentValue;

            try
            {
                _log.LogInformation("正在连接 {DisplayName}：{WsBaseUri}（第 {Attempt} 次尝试）",
                    DisplayName, currentOpts.WsBaseUri, attempt);

                var ws = new CqWsSession(new CqWsSessionOptions
                {
                    BaseUri = new Uri(currentOpts.WsBaseUri),
                });
                session = ws;

                ws.UseGroupMessage(async ctx =>
                {
                    var botOpts = _botOptsMonitor.CurrentValue;
                    if (botOpts.AllowedGroupIds.Length > 0 && !botOpts.AllowedGroupIds.Contains(ctx.GroupId))
                        return;

                    string text = ctx.Message?.Text ?? "";
                    if (string.IsNullOrWhiteSpace(text))
                        return;

                    var incoming = new BotIncomingMessage
                    {
                        Platform = BotPlatform.NapCat,
                        IsGroup = true,
                        TargetId = ctx.GroupId.ToString(),
                        SenderId = ctx.UserId.ToString(),
                        SenderName = ctx.Sender?.Nickname ?? ctx.Sender?.Card ?? "",
                        IsAdmin = ctx.Sender != null && (ctx.Sender.Role == CqRole.Admin || ctx.Sender.Role == CqRole.Owner),
                        Text = text,
                        MessageId = ctx.MessageId.ToString(),
                    };

                    await _router.HandleAsync(this, incoming, stoppingToken);
                });

                ws.UsePrivateMessage(async ctx =>
                {
                    // 私聊仅对显式配置的运营用户开放，避免陌生人私聊触发管理指令
                    var botOpts = _botOptsMonitor.CurrentValue;
                    if (botOpts.NotifyPrivateUserIds.Length == 0 || !botOpts.NotifyPrivateUserIds.Contains(ctx.UserId))
                        return;

                    string text = ctx.Message?.Text ?? "";
                    if (string.IsNullOrWhiteSpace(text))
                        return;

                    var incoming = new BotIncomingMessage
                    {
                        Platform = BotPlatform.NapCat,
                        IsGroup = false,
                        TargetId = ctx.UserId.ToString(),
                        SenderId = ctx.UserId.ToString(),
                        SenderName = ctx.Sender?.Nickname ?? "",
                        // 私聊场景没有群角色，视为管理员（已在上面通过白名单收敛）
                        IsAdmin = true,
                        Text = text,
                        MessageId = ctx.MessageId.ToString(),
                    };

                    await _router.HandleAsync(this, incoming, stoppingToken);
                });

                _session = ws;

                // 启动后台任务尝试拉取登录信息
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(600, stoppingToken);
                        var loginInfo = await ws.GetLoginInformationAsync();
                        if (loginInfo != null)
                        {
                            ConnectedUserId = loginInfo.UserId.ToString();
                            ConnectedNickname = loginInfo.Nickname;
                            _log.LogInformation("已连接 QQ 机器人账号: {Nickname} ({UserId})", loginInfo.Nickname, loginInfo.UserId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug("获取登录信息暂不可用: {Error}", ex.Message);
                    }
                }, stoppingToken);

                // RunAsync 不接受 CancellationToken，靠注册停止回调主动断开来“唤醒”它
                using var stopRegistration = stoppingToken.Register(() =>
                {
                    try { _ = ws.StopAsync(); } catch { /* ignore */ }
                });

                IsConnected = true;
                LastConnectedAt = DateTime.UtcNow;
                LastError = null;

                await ws.RunAsync();

                wasRunning = true;
                _log.LogWarning("{DisplayName} 连接已断开，准备重连", DisplayName);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _log.LogWarning("连接 {DisplayName} 失败：{Error}", DisplayName, ex.Message);
            }
            finally
            {
                IsConnected = false;
                LastDisconnectedAt = DateTime.UtcNow;
                _session = null;
                ConnectedUserId = null;
                ConnectedNickname = null;
                session?.Dispose();
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            if (wasRunning)
                attempt = 0;

            int delaySec = Math.Min(
                currentOpts.ReconnectDelaySeconds * Math.Max(attempt, 1),
                currentOpts.MaxReconnectDelaySeconds);
            if (delaySec < 1)
                delaySec = 1;

            lock (_stateLock)
            {
                NextReconnectInSeconds = delaySec;
            }

            _log.LogInformation("{Delay} 秒后重试连接 {DisplayName} ...", delaySec, DisplayName);

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

        _log.LogInformation("{DisplayName} 连接服务已停止", DisplayName);
    }
}
