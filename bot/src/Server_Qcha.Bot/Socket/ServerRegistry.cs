using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;

namespace Server.Qcat.Socket;

public class ServerInfo
{
    public string Name { get; set; } = "";
    public string ConnectHost { get; set; } = "";
    public int Port { get; set; }
    public int GamePort { get; set; }
    public int SortOrder { get; set; }
    public bool IsStatic { get; set; }
    public bool IsOnline { get; set; } = true;
    public DateTime RegisteredAt { get; set; }
    public DateTime LastHeartbeat { get; set; }
}

public sealed class ServerRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, ServerInfo> _registry = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ServerRegistry> _log;
    private readonly SocketServerOptions _opts;
    private readonly CancellationTokenSource _cts = new();

    public ServerRegistry(IOptions<SocketServerOptions> opts, ILogger<ServerRegistry> log)
    {
        _opts = opts.Value;
        _log = log;

        // 加载静态 Ports
        if (_opts.Ports != null)
        {
            foreach (var port in _opts.Ports)
            {
                var staticServer = new ServerInfo
                {
                    Name = $"静态服-{port}",
                    ConnectHost = _opts.Host,
                    Port = port,
                    GamePort = 0,
                    SortOrder = 0,
                    IsStatic = true,
                    IsOnline = true,
                    RegisteredAt = DateTime.Now,
                    LastHeartbeat = DateTime.Now
                };
                string key = GetKey(_opts.Host, port);
                _registry[key] = staticServer;
                _log.LogInformation("从配置加载静态端口 → {Host}:{Port}（IsStatic=true，豁免心跳超时）", _opts.Host, port);
            }
            LogSummary();
        }

        // 启动后台清理任务，每 60 秒扫描一次
        _ = Task.Run(CleanupLoopAsync);
    }

    private string GetKey(string host, int port) => $"{host.Trim()}:{port}";

    public void Register(ServerInfo server)
    {
        string key = GetKey(server.ConnectHost, server.Port);
        
        _registry.AddOrUpdate(key, 
            _ => 
            {
                server.IsOnline = true;
                server.RegisteredAt = DateTime.Now;
                server.LastHeartbeat = DateTime.Now;
                _log.LogInformation("服务器注册 → [{Name}] {ConnectHost}:{Port} (gamePort={GamePort}, sortOrder={SortOrder})", 
                    server.Name, server.ConnectHost, server.Port, server.GamePort, server.SortOrder);
                return server;
            },
            (_, existing) =>
            {
                // 如果原本是静态的，被动态注册覆盖，则 IsStatic 改为 false
                bool wasStatic = existing.IsStatic;
                existing.Name = server.Name;
                existing.GamePort = server.GamePort;
                existing.SortOrder = server.SortOrder;
                existing.IsStatic = false; // 动态覆盖了静态
                existing.IsOnline = true;
                existing.LastHeartbeat = DateTime.Now;
                
                if (wasStatic)
                {
                    _log.LogInformation("静态端口 {Host}:{Port} 被游戏服动态注册覆盖（IsStatic → false）", server.ConnectHost, server.Port);
                }
                else
                {
                    _log.LogInformation("服务器更新注册 → [{Name}] {ConnectHost}:{Port} (gamePort={GamePort}, sortOrder={SortOrder})", 
                        server.Name, server.ConnectHost, server.Port, server.GamePort, server.SortOrder);
                }
                return existing;
            });

        LogSummary();
    }

    public void Unregister(string host, int port)
    {
        string key = GetKey(host, port);
        if (_registry.TryRemove(key, out _))
        {
            _log.LogInformation("服务器注销 → {ConnectHost}:{Port}", host, port);
            LogSummary();
        }
        else
        {
            _log.LogWarning("收到注销请求但服务器 {ConnectHost}:{Port} 不在注册表中（可能已被清理）", host, port);
        }
    }

    public void Heartbeat(string host, int port, ServerInfo fullInfo)
    {
        string key = GetKey(host, port);
        if (_registry.TryGetValue(key, out var existing))
        {
            existing.LastHeartbeat = DateTime.Now;
            if (!existing.IsOnline)
            {
                existing.IsOnline = true;
                _log.LogInformation("服务器 [{Name}] {ConnectHost}:{Port} 已恢复在线", existing.Name, existing.ConnectHost, existing.Port);
                LogSummary();
            }
            _log.LogDebug("心跳刷新 → {ConnectHost}:{Port}", host, port);
        }
        else
        {
            _log.LogInformation("心跳触发自动注册 → [{Name}] {ConnectHost}:{Port}（机器人重启后恢复）", fullInfo.Name, fullInfo.ConnectHost, fullInfo.Port);
            Register(fullInfo);
        }
    }

    public void MarkOffline(string host, int port)
    {
        string key = GetKey(host, port);
        if (_registry.TryGetValue(key, out var existing))
        {
            if (existing.IsOnline)
            {
                existing.IsOnline = false;
                _log.LogWarning("命令发送失败 → [{Name}] {ConnectHost}:{Port} 已标记为离线", existing.Name, existing.ConnectHost, existing.Port);
                LogSummary();
            }
        }
    }

    public List<ServerInfo> GetSorted()
    {
        var all = _registry.Values.Where(s => s.IsOnline).ToList();

        var groupA = all.Where(s => s.SortOrder > 0)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.GamePort)
                        .ThenBy(s => s.ConnectHost, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(s => s.Port);

        var groupB = all.Where(s => s.SortOrder <= 0)
                        .OrderBy(s => s.GamePort)
                        .ThenBy(s => s.ConnectHost, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(s => s.Port);

        var sorted = groupA.Concat(groupB).ToList();
        
        if (sorted.Count > 0)
        {
            var summary = string.Join(", ", sorted.Select((s, idx) => $"#{idx + 1}=[{s.Name}] {s.ConnectHost}:{s.Port}"));
            _log.LogDebug("当前排序：{Summary}", summary);
        }

        return sorted;
    }

    public int Count => _registry.Values.Count(s => s.IsOnline);

    private void LogSummary()
    {
        var list = _registry.Values;
        int onlineCount = list.Count(s => s.IsOnline);
        int offlineCount = list.Count(s => !s.IsOnline);
        int staticCount = list.Count(s => s.IsStatic);
        _log.LogInformation("注册表状态：在线 {OnlineCount} 台，离线 {OfflineCount} 台，静态 {StaticCount} 台", onlineCount, offlineCount, staticCount);
    }

    private async Task CleanupLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                _log.LogDebug("注册表清理扫描开始，当前在线 {Count} 台服务器", Count);
                var now = DateTime.Now;
                var removedKeys = new List<string>();

                foreach (var kvp in _registry)
                {
                    var s = kvp.Value;
                    if (s.IsStatic)
                    {
                        _log.LogDebug("跳过静态条目 {ConnectHost}:{Port}（手动配置，不受心跳超时管理）", s.ConnectHost, s.Port);
                        continue;
                    }

                    double diffSec = (now - s.LastHeartbeat).TotalSeconds;
                    if (diffSec > 90.0)
                    {
                        if (_registry.TryRemove(kvp.Key, out var removed))
                        {
                            removedKeys.Add(kvp.Key);
                            _log.LogWarning("服务器 [{Name}] {ConnectHost}:{Port} 心跳超时（最后心跳 {LastHeartbeat}，已超过 {Seconds} 秒），已自动移除", 
                                removed.Name, removed.ConnectHost, removed.Port, removed.LastHeartbeat.ToString("yyyy-MM-dd HH:mm:ss"), (int)diffSec);
                        }
                    }
                }

                if (removedKeys.Count > 0)
                {
                    LogSummary();
                }
                
                _log.LogDebug("注册表清理扫描完成，移除 {Removed} 台，剩余 {Remaining} 台在线", removedKeys.Count, Count);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "注册表清理扫描时发生异常");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
