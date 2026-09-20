using EleCho.GoCqHttpSdk;
using EleCho.GoCqHttpSdk.Message;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;

namespace Server.Qcat.Bot;

public sealed record BotGroupSummary(long GroupId, string GroupName, int MemberCount, int MaxMemberCount);
public sealed record BotFriendSummary(long UserId, string Nickname, string? Remark);

public sealed class GoCqHttpBotService : BackgroundService
{
    private readonly IOptionsMonitor<GoCqHttpOptions> _optsMonitor;
    private readonly IOptionsMonitor<BotOptions> _botOptsMonitor;
    private readonly CommandRouter _router;
    private readonly BotSessionAccessor _sessionAccessor;
    private readonly ILogger<GoCqHttpBotService> _log;

    private CancellationTokenSource? _reconnectCts;
    private readonly object _stateLock = new();

    // ---- 状态与信息公开属性 ----
    public bool IsConnected { get; private set; }
    public DateTime? LastConnectedAt { get; private set; }
    public DateTime? LastDisconnectedAt { get; private set; }
    public string? LastError { get; private set; }
    public int CurrentAttempt { get; private set; }
    public int? NextReconnectInSeconds { get; private set; }
    public long? ConnectedUserId { get; private set; }
    public string? ConnectedNickname { get; private set; }

    public GoCqHttpBotService(
        IOptionsMonitor<GoCqHttpOptions> optsMonitor,
        IOptionsMonitor<BotOptions> botOptsMonitor,
        CommandRouter router,
        BotSessionAccessor sessionAccessor,
        ILogger<GoCqHttpBotService> log)
    {
        _optsMonitor = optsMonitor;
        _botOptsMonitor = botOptsMonitor;
        _router = router;
        _sessionAccessor = sessionAccessor;
        _log = log;
    }

    /// <summary>
    /// 主动触发重新连接 NapCatQQ / OneBot 11。
    /// 若当前已有连接，会先断开；若正在等待重连倒计时，会立即唤醒重连。
    /// </summary>
    public async Task ReconnectAsync()
    {
        _log.LogInformation("收到主动重新连接请求，正在重置当前连接...");
        lock (_stateLock)
        {
            CurrentAttempt = 0;
            NextReconnectInSeconds = null;
        }

        var session = _sessionAccessor.Session;
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

        _reconnectCts?.Cancel();
    }

    /// <summary>
    /// 获取当前机器人加入的群组列表（需处于连接状态）。
    /// </summary>
    public async Task<IReadOnlyList<BotGroupSummary>> GetGroupsAsync(CancellationToken ct = default)
    {
        var session = _sessionAccessor.Session;
        if (session == null || !IsConnected)
            return Array.Empty<BotGroupSummary>();

        try
        {
            var res = await session.GetGroupListAsync();
            if (res?.Groups == null)
                return Array.Empty<BotGroupSummary>();

            return res.Groups
                .Select(g => new BotGroupSummary(g.GroupId, g.GroupName, (int)g.MemberCount, (int)g.MaxMemberCount))
                .OrderBy(g => g.GroupName)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning("获取已加入群组列表失败: {Error}", ex.Message);
            return Array.Empty<BotGroupSummary>();
        }
    }

    /// <summary>
    /// 获取当前机器人的好友列表（需处于连接状态）。
    /// </summary>
    public async Task<IReadOnlyList<BotFriendSummary>> GetFriendsAsync(CancellationToken ct = default)
    {
        var session = _sessionAccessor.Session;
        if (session == null || !IsConnected)
            return Array.Empty<BotFriendSummary>();

        try
        {
            var res = await session.GetFriendListAsync();
            if (res?.Friends == null)
                return Array.Empty<BotFriendSummary>();

            return res.Friends
                .Select(f => new BotFriendSummary(f.UserId, f.Nickname, f.Remark))
                .OrderBy(f => f.Nickname)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning("获取好友列表失败: {Error}", ex.Message);
            return Array.Empty<BotFriendSummary>();
        }
    }

    /// <summary>
    /// 发送测试消息（群聊或私聊）。
    /// </summary>
    public async Task<(bool success, string? messageId, string? error)> SendTestMessageAsync(
        bool isGroup, long targetId, string message, CancellationToken ct = default)
    {
        var session = _sessionAccessor.Session;
        if (session == null || !IsConnected)
            return (false, null, "QQ 机器人未连接到 OneBot 11");

        try
        {
            if (isGroup)
            {
                var res = await session.SendGroupMessageAsync(targetId, new CqMessage(message));
                return (true, res?.MessageId.ToString(), null);
            }
            else
            {
                var res = await session.SendPrivateMessageAsync(targetId, new CqMessage(message));
                return (true, res?.MessageId.ToString(), null);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "发送测试消息失败 (TargetId: {TargetId}, IsGroup: {IsGroup}): {Error}", targetId, isGroup, ex.Message);
            return (false, null, ex.Message);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
                _log.LogInformation("Connecting to go-cqhttp websocket: {WsBaseUri} (attempt {Attempt})", currentOpts.WsBaseUri, attempt);

                var ws = new CqWsSession(new CqWsSessionOptions
                {
                    BaseUri = new Uri(currentOpts.WsBaseUri),
                });
                session = ws;

                ws.UseGroupMessage(async ctx =>
                {
                    var botOpts = _botOptsMonitor.CurrentValue;
                    // Filter by group if configured.
                    if (botOpts.AllowedGroupIds.Length > 0 && !botOpts.AllowedGroupIds.Contains(ctx.GroupId))
                        return;

                    await _router.HandleGroupMessageAsync(ws, ctx, stoppingToken);
                });

                _sessionAccessor.Session = ws;

                // 启动后台任务尝试拉取登录信息
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(600, stoppingToken);
                        var loginInfo = await ws.GetLoginInformationAsync();
                        if (loginInfo != null)
                        {
                            ConnectedUserId = loginInfo.UserId;
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
                _log.LogWarning("go-cqhttp websocket 连接已断开，准备重连");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _log.LogWarning("连接 go-cqhttp 失败：{Error}", ex.Message);
            }
            finally
            {
                IsConnected = false;
                LastDisconnectedAt = DateTime.UtcNow;
                _sessionAccessor.Session = null;
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

            _log.LogInformation("{Delay} 秒后重试连接 go-cqhttp ...", delaySec);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _reconnectCts = cts;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 被主动重连唤醒或整体停止
                if (stoppingToken.IsCancellationRequested)
                    break;

                _log.LogInformation("重连等待已被主动唤醒，立即开始连接");
                attempt = 0;
            }
            finally
            {
                _reconnectCts = null;
                lock (_stateLock)
                {
                    NextReconnectInSeconds = null;
                }
            }
        }

        _log.LogInformation("go-cqhttp 连接服务已停止");
    }
}
