using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Server.Qcat.Configuration;
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
    public const string HistoryFileName = "player-history.json";
    public const int MaxHistoryPoints = 30 * 24 * 60 * 2; // 每 30 秒采样一次，最多保留 30 天
    public const int ChartPointsPerRange = 2880; // 图表最多返回 2880 个点，避免浏览器渲染过重
    private readonly ServerRegistry _registry;
    private readonly IServerCommandGateway _gateway;
    private readonly ILogger<PlayerHistoryTracker> _log;
    private readonly string _historyPath;
    private readonly object _persistSync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly List<PlayerHistoryPoint> _history = new();
    private readonly ConcurrentDictionary<string, ServerOnlineSnapshot> _serverSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    private int _peakToday = 0;
    private DateTime _peakDate = DateTime.Today;

    public PlayerHistoryTracker(
        ServerRegistry registry,
        IServerCommandGateway gateway,
        ILogger<PlayerHistoryTracker> log,
        IHostEnvironment environment)
    {
        _registry = registry;
        _gateway = gateway;
        _log = log;
        _historyPath = DataDirectoryManager.GetDataFilePath(environment.ContentRootPath, HistoryFileName);

        LoadHistory();
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

                // 以当天完整历史重新校正峰值，避免峰值只依赖最近一次采样或
                // 其他代码路径写入的缓存值。
                int historyPeak = _history
                    .Where(p => p.Timestamp.Date == DateTime.Today)
                    .Select(p => p.TotalOnline)
                    .DefaultIfEmpty(0)
                    .Max();
                if (historyPeak > _peakToday)
                    _peakToday = historyPeak;

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

    /// <summary>
    /// 按图表时间范围返回历史数据。短时间范围保留原始采样精度，30 天范围按时间桶取最大值，
    /// 这样既能保留峰值，又不会把 8 万多个采样点一次性发送给浏览器。
    /// </summary>
    public IReadOnlyList<PlayerHistoryPoint> GetChartHistory(string? range)
    {
        string normalized = (range ?? "24h").Trim().ToLowerInvariant();
        return normalized switch
        {
            "1h" => GetHistory(120),
            "6h" => GetHistory(720),
            "30d" => GetHistory(ChartPointsPerRange, TimeSpan.FromMinutes(15)),
            _ => GetHistory(2880)
        };
    }

    private IReadOnlyList<PlayerHistoryPoint> GetHistory(int maxPoints, TimeSpan bucket)
    {
        lock (_sync)
        {
            if (_history.Count <= maxPoints)
                return _history.ToList();

            var source = _history
                .Skip(Math.Max(0, _history.Count - MaxHistoryPoints))
                .ToList();
            if (source.Count == 0)
                return source;

            var first = source[0].Timestamp;
            return source
                .GroupBy(point => (long)Math.Floor((point.Timestamp - first).TotalSeconds / bucket.TotalSeconds))
                .Select(group => group
                    .OrderByDescending(point => point.TotalOnline)
                    .ThenByDescending(point => point.Timestamp)
                    .First())
                .OrderBy(point => point.Timestamp)
                .ToList();
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
            DateTime cutoff = now.AddDays(-30);
            _history.RemoveAll(p => p.Timestamp < cutoff);
            while (_history.Count > MaxHistoryPoints)
                _history.RemoveAt(0);
        }

        PersistHistory();
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

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(_historyPath))
            {
                string json = File.ReadAllText(_historyPath);
                var saved = JsonSerializer.Deserialize<List<PlayerHistoryPoint>>(json, JsonOptions);
                if (saved is not null)
                {
                    lock (_sync)
                    {
                        DateTime cutoff = DateTime.Now.AddDays(-30);
                        _history.AddRange(saved
                            .Where(p => p.Timestamp != default && p.Timestamp >= cutoff)
                            .OrderBy(p => p.Timestamp)
                            .TakeLast(MaxHistoryPoints));

                        _peakToday = _history
                            .Where(p => p.Timestamp.Date == DateTime.Today)
                            .Select(p => p.TotalOnline)
                            .DefaultIfEmpty(0)
                            .Max();
                    }

                    _log.LogInformation("已加载在线人数历史记录：{Count} 个采样点，文件：{Path}", _history.Count, _historyPath);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "读取在线人数历史记录失败，将从新的采样开始：{Path}", _historyPath);
        }

        lock (_sync)
        {
            if (_history.Count == 0)
                InitSeedHistory();
        }
    }

    private void PersistHistory()
    {
        lock (_persistSync)
        {
            try
            {
                List<PlayerHistoryPoint> snapshot;
                lock (_sync)
                    snapshot = _history.ToList();

                string json = JsonSerializer.Serialize(snapshot, JsonOptions);
                string tempPath = _historyPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _historyPath, overwrite: true);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "保存在线人数历史记录失败：{Path}", _historyPath);
            }
        }
    }
}
