using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Qcat.Socket;
using Server.Qcat.Web;

namespace Server.Qcat.Services;

public sealed record PlayerHistoryPoint(
    DateTime Timestamp,
    string TimeLabel,
    int TotalOnline,
    Dictionary<string, int> PerServer);

public sealed record ServerOnlineSnapshot(
    string Key,
    string Name,
    int Online,
    int Max,
    DateTime LastUpdated);

/// <summary>
/// 全服玩家历史采样与统计服务：
/// 周期性（每 30 秒）采集各在线服务器的当前人数与上限，维护时间序列与今日峰值，
/// 为前端“服务器总览仪表盘”和“玩家数量折线图”提供数据支撑。
/// </summary>
public sealed class PlayerHistoryTracker : BackgroundService
{
    private const int MaxHistoryPoints = 2880; // 最多保留 2880 个采样点（按 30 秒一次即 24 小时）
    private readonly ServerRegistry _registry;
    private readonly IServerCommandGateway _gateway;
    private readonly ILogger<PlayerHistoryTracker> _log;

    private readonly List<PlayerHistoryPoint> _history = new();
    private readonly ConcurrentDictionary<string, ServerOnlineSnapshot> _serverSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    private int _peakToday = 0;
    private DateTime _peakDate = DateTime.Today;

    public PlayerHistoryTracker(
        ServerRegistry registry,
        IServerCommandGateway gateway,
        ILogger<PlayerHistoryTracker> log)
    {
        _registry = registry;
        _gateway = gateway;
        _log = log;

        // 初始化基础平滑曲线（若刚启动没有任何历史点，生成一些平滑数据基线）
        InitSeedHistory();
    }

    public int PeakToday
    {
        get
        {
            lock (_sync)
            {
                if (DateTime.Today != _peakDate)
                {
                    _peakToday = 0;
                    _peakDate = DateTime.Today;
                }
                return _peakToday;
            }
        }
    }

    /// <summary>获取指定长度的历史采样点（供折线图渲染）。</summary>
    public IReadOnlyList<PlayerHistoryPoint> GetHistory(int maxPoints = 60)
    {
        lock (_sync)
        {
            if (_history.Count <= maxPoints)
                return _history.ToList();

            return _history.Skip(_history.Count - maxPoints).ToList();
        }
    }

    /// <summary>获取某服务器最近一次采集的人数快照。</summary>
    public (int Online, int Max) GetServerOnline(string host, int port)
    {
        string key = $"{host.Trim()}:{port}";
        if (_serverSnapshots.TryGetValue(key, out var snap))
        {
            return (snap.Online, snap.Max);
        }
        return (0, 0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后稍作等待，让服务器注册和连接就绪
        await Task.Delay(3000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SampleAllServersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "采集服务器在线玩家统计时出错");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task SampleAllServersAsync(CancellationToken ct = default)
    {
        var servers = _registry.GetSorted().Where(s => s.IsOnline).ToList();
        var perServer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int totalOnline = 0;

        foreach (var server in servers)
        {
            int online = 0;
            int max = 0;

            try
            {
                // 优先通过 list 命令精确获取在线玩家列表（排除 Dedicated Server）
                var listResult = await _gateway.SendAsync(server, "list", ct);
                bool listOk = false;
                if (listResult.Success && !string.IsNullOrWhiteSpace(listResult.Response))
                {
                    var players = PanelEndpoints.ParsePlayerList(listResult.Response);
                    online = players.Count;
                    listOk = true;
                }

                // 通过 cx 指令获取 上限（以及在 list 失败时的在线人数备选）
                var cxResult = await _gateway.SendAsync(server, "cx", ct);
                if (cxResult.Success && !string.IsNullOrWhiteSpace(cxResult.Response))
                {
                    var (parsedOnline, parsedMax) = PanelEndpoints.ParseOnline(cxResult.Response);
                    if (parsedMax.HasValue)
                    {
                        max = parsedMax.Value;
                    }
                    if (!listOk && parsedOnline.HasValue)
                    {
                        online = parsedOnline.Value;
                    }
                }
            }
            catch
            {
                // 命令超时或网络暂不可达，沿用上一次快照或置0
            }

            string key = $"{server.ConnectHost.Trim()}:{server.Port}";
            _serverSnapshots[key] = new ServerOnlineSnapshot(key, server.Name, online, max, DateTime.Now);
            perServer[server.Name] = online;
            totalOnline += online;
        }

        var now = DateTime.Now;
        var point = new PlayerHistoryPoint(
            Timestamp: now,
            TimeLabel: now.ToString("HH:mm:ss"),
            TotalOnline: totalOnline,
            PerServer: perServer);

        lock (_sync)
        {
            if (DateTime.Today != _peakDate)
            {
                _peakToday = 0;
                _peakDate = DateTime.Today;
            }

            if (totalOnline > _peakToday)
                _peakToday = totalOnline;

            _history.Add(point);
            if (_history.Count > MaxHistoryPoints)
            {
                _history.RemoveAt(0);
            }
        }
    }

    private void InitSeedHistory()
    {
        lock (_sync)
        {
            var now = DateTime.Now;
            // 初始化最近 10 个时间点，保证刚启动时折线图不会空空如也
            for (int i = 10; i >= 1; i--)
            {
                var t = now.AddSeconds(-i * 30);
                _history.Add(new PlayerHistoryPoint(
                    Timestamp: t,
                    TimeLabel: t.ToString("HH:mm:ss"),
                    TotalOnline: 0,
                    PerServer: new Dictionary<string, int>()));
            }
        }
    }
}
