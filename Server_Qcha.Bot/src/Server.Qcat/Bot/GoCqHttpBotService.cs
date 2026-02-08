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
        _log.LogInformation("Connecting to go-cqhttp websocket: {WsBaseUri}", _opts.WsBaseUri);

        var session = new CqWsSession(new CqWsSessionOptions
        {
            BaseUri = new Uri(_opts.WsBaseUri),
        });

        _sessionAccessor.Session = session;

        session.UseGroupMessage(async ctx =>
        {
            // Filter by group if configured.
            if (_botOpts.AllowedGroupIds.Length > 0 && !_botOpts.AllowedGroupIds.Contains(ctx.GroupId))
                return;

            await _router.HandleGroupMessageAsync(session, ctx, stoppingToken);
        });

        // RunAsync doesn't currently take a CancellationToken in this SDK version.
        await session.RunAsync();
    }
}
