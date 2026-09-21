using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;

namespace Server.Qcat.Bot;

/// <summary>
/// 机器人接入模式托管服务。
///
/// 按 <see cref="BotOptions.Mode"/> 在 NapCat（OneBot 11）与 QQ 官方 Bot API 之间选择唯一的活动连接，
/// 并监听配置热重载：一旦模式变更就停掉旧连接、启动新连接，全程无需重启进程。
/// 同一时刻只有一套连接在运行，避免两条通道同时回复同一条指令。
/// </summary>
public sealed class BotHostService : BackgroundService
{
    private readonly IOptionsMonitor<BotOptions> _botOpts;
    private readonly NapCatBotClient _napCat;
    private readonly OfficialQqBotClient _official;
    private readonly BotClientAccessor _accessor;
    private readonly ILogger<BotHostService> _log;

    private readonly SemaphoreSlim _switchSignal = new(0, 1);
    private volatile IBotClient? _active;

    public BotHostService(
        IOptionsMonitor<BotOptions> botOpts,
        NapCatBotClient napCat,
        OfficialQqBotClient official,
        BotClientAccessor accessor,
        ILogger<BotHostService> log)
    {
        _botOpts = botOpts;
        _napCat = napCat;
        _official = official;
        _accessor = accessor;
        _log = log;
    }

    /// <summary>当前活动的接入实现（未启动时为 null）。</summary>
    public IBotClient? ActiveClient => _active;

    /// <summary>当前配置的模式（活动连接尚未建立时按配置推断）。</summary>
    public BotPlatform ActivePlatform => _active?.Platform ?? ResolveMode(_botOpts.CurrentValue);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var registration = _botOpts.OnChange((_, _) => SignalSwitch());

        await SwitchToAsync(ResolveMode(_botOpts.CurrentValue), stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _switchSignal.WaitAsync(stoppingToken);
                await SwitchToAsync(ResolveMode(_botOpts.CurrentValue), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停机
        }
        finally
        {
            await StopActiveAsync(stoppingToken);
        }
    }

    /// <summary>重连当前活动连接（面板「重新连接」按钮）。</summary>
    public async Task ReconnectActiveAsync()
    {
        var client = _active;
        if (client is null)
        {
            SignalSwitch();
            return;
        }

        await client.ReconnectAsync();
    }

    private void SignalSwitch()
    {
        try
        {
            _switchSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已有待处理的切换信号，无需重复
        }
    }

    private async Task SwitchToAsync(BotPlatform mode, CancellationToken ct)
    {
        var current = _active;
        if (current is not null && current.Platform == mode)
            return;

        await StopActiveAsync(ct);

        BotClientHost host;
        IBotClient client;

        if (mode == BotPlatform.OfficialQq)
        {
            host = _official;
            client = _official;
        }
        else
        {
            host = _napCat;
            client = _napCat;
        }

        _active = client;
        _accessor.Set(client);

        await host.StartAsync(ct);

        _log.LogInformation("机器人接入模式已切换为 {DisplayName}", client.DisplayName);
    }

    private async Task StopActiveAsync(CancellationToken ct)
    {
        var client = _active;
        _active = null;
        _accessor.Set(null);

        if (client is not BotClientHost host)
            return;

        try
        {
            await host.StopAsync(ct);
            _log.LogInformation("已停止 {DisplayName} 连接", client.DisplayName);
        }
        catch (Exception ex)
        {
            _log.LogDebug("停止 {DisplayName} 时出现异常：{Error}", client.DisplayName, ex.Message);
        }
    }

    private static BotPlatform ResolveMode(BotOptions opts)
        => Enum.IsDefined(typeof(BotPlatform), opts.Mode) ? opts.Mode : BotPlatform.NapCat;
}
