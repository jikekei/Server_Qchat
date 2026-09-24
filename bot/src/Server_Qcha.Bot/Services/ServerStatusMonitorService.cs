using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Qcat.Socket;

namespace Server.Qcat.Services;

/// <summary>
/// 瓶颈指标条目
/// </summary>
public sealed record BottleneckItem(string Key, string Name, int Value);

/// <summary>
/// 硬件与系统底层明细数据
/// </summary>
public sealed record ServerStatusDetails(
    double SystemCpuPercent,
    double ProcessCpuPercent,
    long WorkingSetBytes,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    double DiskUsagePercent,
    int ActiveConnections,
    int ProcessCount,
    double EstimatedLatencyMs);

/// <summary>
/// 游戏服务器综合负载与状态模型（对应《综合负载监控系统规划书》规范）
/// </summary>
public sealed record ServerStatusModel(
    int Load,
    string Status,
    string StatusText,
    int Cpu,
    int Memory,
    int Disk,
    int Network,
    int MainThread,
    int Game,
    int Players,
    int MaxPlayers,
    string PrimaryBottleneck,
    string SecondaryBottleneck,
    List<BottleneckItem> Bottlenecks,
    string Diagnosis,
    int SmoothLoad,
    int PeakLoad,
    ServerStatusDetails Details,
    DateTime UpdatedAt);

/// <summary>
/// 游戏服务器综合负载监控系统：
/// 采集系统资源（CPU、内存、磁盘、网络）与游戏服务器自身状态（主线程、业务压力、玩家数），
/// 通过多维加权、峰值修正、瓶颈保护与 EMA 平滑，计算出真实反映游戏服压力的综合负载。
/// </summary>
public sealed class ServerStatusMonitorService : BackgroundService
{
    private readonly ServerRegistry _registry;
    private readonly PlayerHistoryTracker _historyTracker;
    private readonly ILogger<ServerStatusMonitorService> _log;

    private readonly object _sync = new();
    private ServerStatusModel _currentStatus;

    // CPU 采样计算
    private DateTime _lastCpuSampleTime = DateTime.UtcNow;
    private TimeSpan _lastCpuTotalTime = TimeSpan.Zero;
    private double _lastCalculatedCpu = 0;

    // 平滑 EMA 负载与峰值窗口 (最近 60 秒)
    private double _smoothLoad = 0;
    private const double EmaAlpha = 0.25;
    private readonly Queue<(DateTime Time, int Load)> _peakWindow = new();

    public ServerStatusMonitorService(
        ServerRegistry registry,
        PlayerHistoryTracker historyTracker,
        ILogger<ServerStatusMonitorService> log)
    {
        _registry = registry;
        _historyTracker = historyTracker;
        _log = log;

        // 初始化默认健康模型
        _currentStatus = CreateFallbackStatus(0, 0);

        try
        {
            var p = Process.GetCurrentProcess();
            _lastCpuTotalTime = p.TotalProcessorTime;
        }
        catch
        {
            // 忽略进程时间读取异常
        }
    }

    /// <summary>
    /// 获取当前最新综合负载与状态快照
    /// </summary>
    public ServerStatusModel GetCurrentStatus()
    {
        lock (_sync)
        {
            return _currentStatus;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("游戏服务器综合负载监控服务已启动（采样周期：2 秒）");

        // 首次立即采集一次
        CollectAndCalculate();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                CollectAndCalculate();
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停机退出
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "综合负载监控服务运行异常");
        }
    }

    private void CollectAndCalculate()
    {
        try
        {
            // 1. 采集在线玩家与服务器信息
            var servers = _registry.GetSorted();
            int totalPlayers = 0;
            int totalMax = 0;
            bool hasOnlineServer = false;
            double minHeartbeatDiffSeconds = 999;

            foreach (var s in servers)
            {
                var (online, max) = _historyTracker.GetServerOnline(s.ConnectHost, s.Port);
                if (s.IsOnline)
                {
                    hasOnlineServer = true;
                    totalPlayers += online;
                    totalMax += max;
                    double diffSec = Math.Max(0, (DateTime.Now - s.LastHeartbeat).TotalSeconds);
                    if (diffSec < minHeartbeatDiffSeconds)
                        minHeartbeatDiffSeconds = diffSec;
                }
            }

            if (totalMax <= 0 && servers.Count > 0)
                totalMax = servers.Count * 30; // 兜底标准服容量

            // 2. 采集 CPU 压力
            double processCpu = CalculateProcessCpu();
            double totalCpu = CalculateSystemCpuEstimate(processCpu);
            int cpuPressure = (int)Math.Clamp(Math.Round(Math.Max(totalCpu, processCpu * 1.1)), 0, 100);

            // 3. 采集内存压力
            var (memoryPressure, workingSetBytes, totalMemBytes, availMemBytes) = CalculateMemoryPressure();

            // 4. 采集磁盘压力
            var (diskPressure, diskUsagePercent) = CalculateDiskPressure();

            // 5. 采集网络压力
            var (networkPressure, activeConnCount, estLatencyMs) = CalculateNetworkPressure(hasOnlineServer, minHeartbeatDiffSeconds);

            // 6. 采集游戏业务压力
            int gamePressure = 0;
            if (hasOnlineServer && totalMax > 0)
            {
                double playerRatio = (double)totalPlayers / totalMax;
                gamePressure = (int)Math.Clamp(Math.Round(playerRatio * 85.0 + Math.Min(totalPlayers * 0.5, 15.0)), 0, 100);
            }
            else
            {
                gamePressure = servers.Count > 0 ? 5 : 0;
            }

            // 7. 采集游戏主线程压力
            int mainThreadPressure = CalculateMainThreadPressure(hasOnlineServer, minHeartbeatDiffSeconds, cpuPressure, totalPlayers);

            // 8. 核心算法：加权负载 (WeightedLoad)
            // 初始推荐权重：CPU 25%, 内存 15%, 磁盘 10%, 网络 10%, 游戏主线程 25%, 游戏业务 15%
            double weightedLoad = (cpuPressure * 0.25)
                                + (memoryPressure * 0.15)
                                + (diskPressure * 0.10)
                                + (networkPressure * 0.10)
                                + (mainThreadPressure * 0.25)
                                + (gamePressure * 0.15);

            // 9. 瓶颈修正机制 (PeakPressure)
            int peakPressure = Math.Max(cpuPressure,
                Math.Max(memoryPressure,
                    Math.Max(diskPressure,
                        Math.Max(networkPressure,
                            Math.Max(mainThreadPressure, gamePressure)))));

            double finalLoad = (weightedLoad * 0.75) + (peakPressure * 0.25);

            // 10. 强制瓶颈保护
            if (peakPressure >= 95 && finalLoad < 85)
                finalLoad = 85;
            else if (peakPressure >= 90 && finalLoad < 75)
                finalLoad = 75;

            // 11. EMA 数据平滑
            if (_smoothLoad <= 0)
                _smoothLoad = finalLoad;
            else
                _smoothLoad = (finalLoad * EmaAlpha) + (_smoothLoad * (1.0 - EmaAlpha));

            int finalLoadInt = (int)Math.Clamp(Math.Round(finalLoad), 0, 100);
            int smoothLoadInt = (int)Math.Clamp(Math.Round(_smoothLoad), 0, 100);

            // 12. 60 秒峰值窗口
            DateTime now = DateTime.UtcNow;
            _peakWindow.Enqueue((now, finalLoadInt));
            while (_peakWindow.Count > 0 && (now - _peakWindow.Peek().Time).TotalSeconds > 60)
            {
                _peakWindow.Dequeue();
            }
            int peakLoad60s = _peakWindow.Count > 0 ? _peakWindow.Max(x => x.Load) : finalLoadInt;

            // 13. 负载等级定义 (Idle, Normal, Medium, High, Critical, Overload)
            string status;
            string statusText;
            if (mainThreadPressure >= 100 || finalLoadInt >= 95)
            {
                status = "Overload";
                statusText = "严重过载";
            }
            else if (finalLoadInt >= 85)
            {
                status = "Critical";
                statusText = "极高负载";
            }
            else if (finalLoadInt >= 70)
            {
                status = "High";
                statusText = "高负载";
            }
            else if (finalLoadInt >= 50)
            {
                status = "Medium";
                statusText = "中等负载";
            }
            else if (finalLoadInt >= 30)
            {
                status = "Normal";
                statusText = "正常";
            }
            else
            {
                status = "Idle";
                statusText = "空闲";
            }

            // 14. 瓶颈识别排序
            var metricItems = new List<BottleneckItem>
            {
                new("mainThread", "游戏主线程", mainThreadPressure),
                new("cpu", "CPU综合压力", cpuPressure),
                new("game", "游戏业务压力", gamePressure),
                new("memory", "内存压力", memoryPressure),
                new("network", "网络压力", networkPressure),
                new("disk", "磁盘压力", diskPressure)
            };
            metricItems.Sort((a, b) => b.Value.CompareTo(a.Value));

            string primaryBottleneck = $"{metricItems[0].Name} ({metricItems[0].Value}%)";
            string secondaryBottleneck = $"{metricItems[1].Name} ({metricItems[1].Value}%)";

            // 15. 异常诊断逻辑 (根据第十九节)
            string diagnosis = GenerateDiagnosis(cpuPressure, memoryPressure, diskPressure, networkPressure, mainThreadPressure, gamePressure, status);

            var details = new ServerStatusDetails(
                SystemCpuPercent: Math.Round(totalCpu, 1),
                ProcessCpuPercent: Math.Round(processCpu, 1),
                WorkingSetBytes: workingSetBytes,
                TotalMemoryBytes: totalMemBytes,
                AvailableMemoryBytes: availMemBytes,
                DiskUsagePercent: Math.Round(diskUsagePercent, 1),
                ActiveConnections: activeConnCount,
                ProcessCount: Process.GetProcessesByName("SCPSL").Length,
                EstimatedLatencyMs: Math.Round(estLatencyMs, 1));

            var snapshot = new ServerStatusModel(
                Load: finalLoadInt,
                Status: status,
                StatusText: statusText,
                Cpu: cpuPressure,
                Memory: memoryPressure,
                Disk: diskPressure,
                Network: networkPressure,
                MainThread: mainThreadPressure,
                Game: gamePressure,
                Players: totalPlayers,
                MaxPlayers: totalMax,
                PrimaryBottleneck: primaryBottleneck,
                SecondaryBottleneck: secondaryBottleneck,
                Bottlenecks: metricItems,
                Diagnosis: diagnosis,
                SmoothLoad: smoothLoadInt,
                PeakLoad: peakLoad60s,
                Details: details,
                UpdatedAt: DateTime.Now);

            lock (_sync)
            {
                _currentStatus = snapshot;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "采集服务器综合指标异常：{Error}", ex.Message);
        }
    }

    private double CalculateProcessCpu()
    {
        try
        {
            var now = DateTime.UtcNow;
            var currentProc = Process.GetCurrentProcess();
            var totalTime = currentProc.TotalProcessorTime;

            // 累计 SCPSL 游戏服进程
            foreach (var sp in Process.GetProcessesByName("SCPSL"))
            {
                try
                {
                    totalTime += sp.TotalProcessorTime;
                }
                catch { }
            }

            double elapsedSec = (now - _lastCpuSampleTime).TotalSeconds;
            if (elapsedSec > 0.5)
            {
                double cpuUsage = (totalTime - _lastCpuTotalTime).TotalSeconds / (elapsedSec * Math.Max(1, Environment.ProcessorCount)) * 100.0;
                _lastCpuTotalTime = totalTime;
                _lastCpuSampleTime = now;
                _lastCalculatedCpu = Math.Clamp(cpuUsage, 0.0, 100.0);
            }
            return _lastCalculatedCpu;
        }
        catch
        {
            return _lastCalculatedCpu;
        }
    }

    private double CalculateSystemCpuEstimate(double processCpu)
    {
        // 若无法获取系统总 CPU 计数器，则以主进程组合负载加算底噪估算
        double systemBase = 5.0; // 操作系统后台基线 5%
        return Math.Clamp(processCpu + systemBase, 0.0, 100.0);
    }

    private (int Pressure, long WorkingSetBytes, long TotalMemBytes, long AvailMemBytes) CalculateMemoryPressure()
    {
        long workingSet = 0;
        try
        {
            workingSet = Process.GetCurrentProcess().WorkingSet64;
            foreach (var p in Process.GetProcessesByName("SCPSL"))
            {
                try { workingSet += p.WorkingSet64; } catch { }
            }
        }
        catch { }

        long totalMem = 0;
        long availMem = 0;
        double memUsageRatio = 0.5; // 默认 50%

        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            totalMem = gcInfo.TotalAvailableMemoryBytes;
            long memoryLoadBytes = gcInfo.MemoryLoadBytes;

            if (totalMem > 0)
            {
                availMem = Math.Max(0, totalMem - memoryLoadBytes);
                memUsageRatio = (double)memoryLoadBytes / totalMem;
            }
        }
        catch
        {
            totalMem = 16L * 1024 * 1024 * 1024; // 16GB 兜底估算
            availMem = 8L * 1024 * 1024 * 1024;
        }

        double commitRatio = Math.Min(1.0, (double)workingSet / Math.Max(1, totalMem));
        double memPressure = (memUsageRatio * 60.0) + (commitRatio * 40.0);
        int finalMemPressure = (int)Math.Clamp(Math.Round(memPressure), 0, 100);

        return (finalMemPressure, workingSet, totalMem, availMem);
    }

    private (int Pressure, double UsagePercent) CalculateDiskPressure()
    {
        try
        {
            string root = Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                double usedRatio = 1.0 - ((double)drive.AvailableFreeSpace / drive.TotalSize);
                double usagePercent = usedRatio * 100.0;

                // 磁盘压力模型：空间低于 15% 时压力急剧上升
                double pressure = usagePercent * 0.7;
                if (usedRatio > 0.85)
                    pressure += (usedRatio - 0.85) * 200.0;

                return ((int)Math.Clamp(Math.Round(pressure), 0, 100), usagePercent);
            }
        }
        catch { }

        return (15, 30.0);
    }

    private (int Pressure, int ActiveConnCount, double LatencyMs) CalculateNetworkPressure(bool hasOnlineServer, double heartbeatDiffSec)
    {
        int connCount = 0;
        try
        {
            var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = ipProperties.GetActiveTcpConnections();
            connCount = tcpConnections.Length;
        }
        catch { }

        double latency = 5.0;
        int pressure = 10;

        if (hasOnlineServer)
        {
            // 基于心跳更新延迟评估网络传输健康度
            if (heartbeatDiffSec < 15)
                latency = 8.0 + (heartbeatDiffSec * 2.0);
            else if (heartbeatDiffSec < 30)
                latency = 30.0 + (heartbeatDiffSec * 3.0);
            else
                latency = 120.0;

            pressure = (int)Math.Clamp(Math.Round((latency / 80.0) * 50.0 + Math.Min(connCount * 0.2, 40.0)), 5, 100);
        }

        return (pressure, connCount, latency);
    }

    private int CalculateMainThreadPressure(bool hasOnlineServer, double heartbeatDiffSec, int cpuPressure, int totalPlayers)
    {
        if (!hasOnlineServer)
            return 0;

        // SCPSL 游戏服目标 Tick 为 50-60 TPS (每帧 16-20ms)
        // 当单核压力剧增或心跳出现明显延迟滞后，主线程压力迅速上升
        double basePressure = 20.0;
        if (totalPlayers > 0)
            basePressure += Math.Min(totalPlayers * 1.5, 45.0);

        if (cpuPressure > 70)
            basePressure += (cpuPressure - 70) * 1.2;

        if (heartbeatDiffSec > 15)
            basePressure += Math.Min((heartbeatDiffSec - 15) * 5.0, 30.0);

        return (int)Math.Clamp(Math.Round(basePressure), 0, 100);
    }

    private static string GenerateDiagnosis(int cpu, int mem, int disk, int net, int mainThread, int game, string status)
    {
        if (status == "Overload" || mainThread >= 95)
            return "游戏主线程负载已达临界极限，可能出现明显掉帧或网络同步延迟，建议排查卡顿插件或减少单服承载人数。";

        if (cpu >= 80 && mainThread >= 75)
            return "检测到 CPU 单核心与游戏主线程存在较高计算压力，建议关注高耗时插件事件或实体运算。";

        if (mem >= 85)
            return "系统可用物理内存较低，建议排查是否存在未释放的大数据缓存或内存泄露风险。";

        if (disk >= 85)
            return "磁盘驱动器可用空间紧缺或读写吞吐较高，请确保存放日志与存档的分区剩余容量充足。";

        if (net >= 80)
            return "网络吞吐或连接活跃度较高，请持续关注游戏客户端连接 Ping 值与丢包状况。";

        if (game >= 80)
            return "当前全服在线玩家已接近预设容量上限，业务吞吐良好，处于高峰运营状态。";

        return "各维度监控指标均衡平稳，系统资源运转良好，暂未发现瓶颈风险。";
    }

    private static ServerStatusModel CreateFallbackStatus(int players, int maxPlayers)
    {
        var items = new List<BottleneckItem>
        {
            new("mainThread", "游戏主线程", 0),
            new("cpu", "CPU综合压力", 0),
            new("game", "游戏业务压力", 0),
            new("memory", "内存压力", 0),
            new("network", "网络压力", 0),
            new("disk", "磁盘压力", 0)
        };

        return new ServerStatusModel(
            Load: 0,
            Status: "Idle",
            StatusText: "空闲",
            Cpu: 0,
            Memory: 0,
            Disk: 0,
            Network: 0,
            MainThread: 0,
            Game: 0,
            Players: players,
            MaxPlayers: maxPlayers,
            PrimaryBottleneck: "无 (0%)",
            SecondaryBottleneck: "无 (0%)",
            Bottlenecks: items,
            Diagnosis: "监控初始化中，正在建立基线数据采集...",
            SmoothLoad: 0,
            PeakLoad: 0,
            Details: new ServerStatusDetails(0, 0, 0, 0, 0, 0, 0, 0, 0),
            UpdatedAt: DateTime.Now);
    }
}
