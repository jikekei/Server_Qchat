using EleCho.GoCqHttpSdk;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;

namespace Server.Qcat.Bot;

public sealed class GoCqHttpBotService : BackgroundService
{
    private readonly GoCqHttpOptions _opts;
    private readonly BotOptions _botOpts;
    private readonly CommandRouter _router;
    private readonly BotSessionAccessor _sessionAccessor;
    private readonly ILogger<GoCqHttpBotService> _log;

    public GoCqHttpBotService(
        IOptions<GoCqHttpOptions> opts,
        IOptions<BotOptions> botOpts,
        CommandRouter router,
        BotSessionAccessor sessionAccessor,
        ILogger<GoCqHttpBotService> log)
    {
        _opts = opts.Value;
        _botOpts = botOpts.Value;
        _router = router;
        _sessionAccessor = sessionAccessor;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int attempt = 0;

        // 连接失败/断开时自动重连，绝不让异常冒泡到 Host（否则整个程序会退出）
        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            CqWsSession? session = null;
            bool wasRunning = false;

            try
            {
                _log.LogInformation("Connecting to go-cqhttp websocket: {WsBaseUri} (attempt {Attempt})", _opts.WsBaseUri, attempt);

                var ws = new CqWsSession(new CqWsSessionOptions
                {
                    BaseUri = new Uri(_opts.WsBaseUri),
                });
                session = ws;

                ws.UseGroupMessage(async ctx =>
                {
                    // Filter by group if configured.
                    if (_botOpts.AllowedGroupIds.Length > 0 && !_botOpts.AllowedGroupIds.Contains(ctx.GroupId))
                        return;

                    await _router.HandleGroupMessageAsync(ws, ctx, stoppingToken);
                });

                _sessionAccessor.Session = ws;

                // RunAsync 不接受 CancellationToken，靠注册停止回调主动断开来“唤醒”它
                using var stopRegistration = stoppingToken.Register(() =>
                {
                    try { _ = ws.StopAsync(); } catch { /* ignore */ }
                });

                // RunAsync doesn't currently take a CancellationToken in this SDK version.
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
                // 连接被拒绝属于可恢复状态（如 QQ 框架尚未启动），只记录简要原因
                _log.LogWarning("连接 go-cqhttp 失败：{Error}", ex.Message);
            }
            finally
            {
                _sessionAccessor.Session = null;
                session?.Dispose();
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            // 曾经成功运行过说明是掉线，重新计数快速重连；连续连不上则线性退避
            if (wasRunning)
                attempt = 0;

            int delaySec = Math.Min(
                _opts.ReconnectDelaySeconds * Math.Max(attempt, 1),
                _opts.MaxReconnectDelaySeconds);
            if (delaySec < 1)
                delaySec = 1;

            _log.LogInformation("{Delay} 秒后重试连接 go-cqhttp ...", delaySec);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _log.LogInformation("go-cqhttp 连接服务已停止");
    }
}
