using System.Diagnostics;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin.Models;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// 内嵌模式下的 LocalAdmin 提供方：直接调用同一进程内的 LocalAdminManager。
/// </summary>
public sealed class EmbeddedLocalAdminProvider : ILocalAdminProvider
{
    private readonly LocalAdminManager _manager;
    private readonly LocalAdminOptions _options;

    public EmbeddedLocalAdminProvider(LocalAdminManager manager, IOptions<LocalAdminOptions> options)
    {
        _manager = manager;
        _options = options.Value;
    }

    public string Mode => "Embedded";

    public Task<LocalAdminListResult> ListServersAsync(CancellationToken ct = default)
    {
        var servers = _manager.Instances.Select(i => i.GetStatus()).ToList();
        var definitions = _manager.Definitions.ToList();

        var result = new LocalAdminListResult(
            _manager.Enabled,
            servers.Count,
            _manager.UsingConfigFile ? "config-file" : "appsettings",
            _manager.ConfigPath,
            ConsoleCaptureLevels.DescribeTyped(),
            servers,
            definitions);

        return Task.FromResult(result);
    }

    public Task<LocalServerStatus?> GetStatusAsync(string id, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        return Task.FromResult(instance?.GetStatus());
    }

    public Task<LocalPollResult?> PollAsync(string id, long? after, int? limit, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Task.FromResult<LocalPollResult?>(null);

        long afterSeq = after.GetValueOrDefault();
        int take = Math.Clamp(limit ?? 400, 1, 2000);

        var lines = instance.ReadLines(afterSeq, take, out long firstSeq, out long lastSeq, out bool truncated);
        var dtos = lines.Select(l => l.ToDto()).ToList();

        var result = new LocalPollResult(instance.GetStatus(), firstSeq, lastSeq, truncated, dtos);
        return Task.FromResult<LocalPollResult?>(result);
    }

    public Task<LocalSaveResult> CreateServerAsync(LocalServerSaveRequest request, CancellationToken ct = default)
    {
        var def = request.ToDefinition();
        var result = _manager.Save(def, originalId: null, out string? warning);
        return Task.FromResult(new LocalSaveResult(result.Success, result.Message, result.Error, warning));
    }

    public Task<LocalSaveResult> UpdateServerAsync(string id, LocalServerSaveRequest request, CancellationToken ct = default)
    {
        var def = request.ToDefinition();
        var result = _manager.Save(def, originalId: id, out string? warning);
        return Task.FromResult(new LocalSaveResult(result.Success, result.Message, result.Error, warning));
    }

    public Task<LocalAdminResult> DeleteServerAsync(string id, CancellationToken ct = default)
    {
        return Task.FromResult(_manager.Remove(id));
    }

    public Task<LocalAdminResult> StartAsync(string id, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Task.FromResult(LocalAdminResult.Fail($"本地服务器实例不存在：{id}"));

        return Task.FromResult(instance.Start());
    }

    public Task<LocalAdminResult> StopAsync(string id, bool force, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Task.FromResult(LocalAdminResult.Fail($"本地服务器实例不存在：{id}"));

        return Task.FromResult(instance.Stop(force));
    }

    public Task<LocalAdminResult> RestartAsync(string id, bool force, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Task.FromResult(LocalAdminResult.Fail($"本地服务器实例不存在：{id}"));

        return Task.FromResult(instance.Restart(force));
    }

    public async Task<LocalAdminResult> SendConsoleAsync(string id, string command, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return LocalAdminResult.Fail($"本地服务器实例不存在：{id}");

        return await instance.SendConsoleAsync(command, ct);
    }

    public Task<LocalAdminResult> ClearConsoleAsync(string id, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Task.FromResult(LocalAdminResult.Fail($"本地服务器实例不存在：{id}"));

        instance.ClearBuffer();
        return Task.FromResult(LocalAdminResult.Ok("控制台缓冲已清空"));
    }

    public Task<LocalAdminResult> SetHeartbeatAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Task.FromResult(LocalAdminResult.Fail($"本地服务器实例不存在：{id}"));

        return Task.FromResult(instance.SetHeartbeatEnabled(enabled));
    }

    public Task<LocalConsoleLevelResult> SetConsoleLevelAsync(string id, string? level, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Task.FromResult(new LocalConsoleLevelResult(false, null, $"本地服务器实例不存在：{id}", null));

        var result = instance.SetConsoleLevel(level);
        string? persistWarning = null;
        if (result.Success)
        {
            var persist = _manager.PersistConsoleLevel(id, instance.ConsoleLevelKey);
            if (!persist.Success)
                persistWarning = persist.Error;
        }

        return Task.FromResult(new LocalConsoleLevelResult(result.Success, result.Message, result.Error, persistWarning));
    }

    public Task<LocalAdminResult> CancelRestartAsync(string id, CancellationToken ct = default)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Task.FromResult(LocalAdminResult.Fail($"本地服务器实例不存在：{id}"));

        return Task.FromResult(instance.CancelRestartCountdown());
    }

    public Task<LocalConfigInfo> GetConfigAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_manager.GetConfigInfo());
    }

    public Task<LocalAdminResult> ResaveConfigAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_manager.ResaveConfig(out _, out _));
    }

    public Task<ScpslDiscoveryDto> FindExecutablesAsync(CancellationToken ct = default)
    {
        var result = ScpslLocator.Search(_options.DefaultExecutablePath);
        return Task.FromResult(result.ToDto());
    }

    public Task<DaemonPingResult?> PingAsync(CancellationToken ct = default)
    {
        int running = _manager.Instances.Count(i => i.Running);
        return Task.FromResult<DaemonPingResult?>(new DaemonPingResult(
            "ok", "embedded", Environment.ProcessId, _manager.Instances.Count, running));
    }

    public Task<DaemonStatusResult> GetDaemonStatusAsync(CancellationToken ct = default)
    {
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        double wsMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 1);
        double privMb = Math.Round(proc.PrivateMemorySize64 / (1024.0 * 1024.0), 1);
        double uptime = Math.Round((DateTime.UtcNow - proc.StartTime.ToUniversalTime()).TotalSeconds, 0);
        int running = _manager.Instances.Count(i => i.Running);

        return Task.FromResult(new DaemonStatusResult(
            Online: true,
            Status: "running (embedded)",
            Version: "2.0.0",
            Pid: Environment.ProcessId,
            StartTime: proc.StartTime,
            UptimeSeconds: uptime,
            MemoryWorkingSetMb: wsMb,
            MemoryPrivateMb: privMb,
            ThreadCount: proc.Threads.Count,
            ServerCount: _manager.Instances.Count,
            RunningServerCount: running,
            ListenUri: "embedded",
            Message: "当前运行于内嵌模式（跟随主程序）"));
    }

    public Task<LocalAdminResult> StartDaemonAsync(CancellationToken ct = default)
    {
        return Task.FromResult(LocalAdminResult.Ok("当前为内嵌模式，LocalAdmin 已随主程序运行"));
    }

    public Task<LocalAdminResult> StopDaemonAsync(CancellationToken ct = default)
    {
        return Task.FromResult(LocalAdminResult.Fail("当前为内嵌模式，请直接关闭主程序"));
    }

    public Task<LocalAdminResult> RestartDaemonAsync(CancellationToken ct = default)
    {
        return Task.FromResult(LocalAdminResult.Fail("当前为内嵌模式，请直接重启主程序"));
    }

    public Task<LocalAdminResult> ShutdownDaemonAsync(CancellationToken ct = default)
    {
        return Task.FromResult(LocalAdminResult.Fail("当前处于内嵌模式，无需关闭独立守护进程"));
    }
}
