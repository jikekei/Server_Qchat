using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin.Models;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// 独立守护模式下的 LocalAdmin 提供方：通过 HTTP 与 Server_Qcha.Daemon.exe 通信。
/// 机器人面板进程重启、热更新或退出时，守护进程与游戏服务端完全不受影响。
/// </summary>
public sealed class DaemonLocalAdminProvider : ILocalAdminProvider
{
    private readonly HttpClient _http;
    private readonly LocalAdminOptions _options;
    private readonly ILogger<DaemonLocalAdminProvider> _log;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly object _startLock = new();
    private DateTime _lastStartAttempt = DateTime.MinValue;

    public DaemonLocalAdminProvider(
        HttpClient httpClient,
        IOptions<LocalAdminOptions> options,
        ILogger<DaemonLocalAdminProvider> log)
    {
        _options = options.Value;
        _log = log;

        string baseUri = _options.DaemonUri.TrimEnd('/') + "/";
        httpClient.BaseAddress = new Uri(baseUri);
        httpClient.DefaultRequestHeaders.Remove("X-Daemon-Token");
        if (!string.IsNullOrWhiteSpace(_options.DaemonToken))
        {
            httpClient.DefaultRequestHeaders.Add("X-Daemon-Token", _options.DaemonToken);
        }
        httpClient.Timeout = TimeSpan.FromSeconds(15);
        _http = httpClient;
    }

    public string Mode => "Daemon";

    /// <summary>
    /// 在 Bot 启动时主动探测守护进程是否运行，若未运行则自动在后台拉起，
    /// 并等待其就绪。确保直接双击 Server_Qcha.Bot.exe 时也能自动享受独立守护架构。
    /// </summary>
    public bool EnsureDaemonRunningAtStartup()
    {
        if (!_options.AutoStartDaemon)
            return false;

        lock (_startLock)
        {
            // 1. 先探测是否已有存活的守护进程
            try
            {
                var ping = PingAsync().GetAwaiter().GetResult();
                if (ping is not null && string.Equals(ping.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogInformation("已连接到运行中的 LocalAdmin 守护进程 (PID: {Pid})，无需重新拉起", ping.Pid);
                    return true;
                }
            }
            catch
            {
                // 未响应说明未运行
            }

            // 2. 检查可执行文件
            string baseDir = AppContext.BaseDirectory;
            string daemonExe = Path.Combine(baseDir, "Server_Qcha.Daemon.exe");
            if (!File.Exists(daemonExe))
            {
                _log.LogWarning("未找到守护进程程序：{Path}，请确保 Server_Qcha.Daemon.exe 与 Bot 位于同一目录下", daemonExe);
                return false;
            }

            try
            {
                _log.LogInformation("检测到独立守护进程未运行，正在自动拉起 {Path}...", daemonExe);
                var psi = new ProcessStartInfo
                {
                    FileName = daemonExe,
                    WorkingDirectory = baseDir,
                    UseShellExecute = true,
                };

                try
                {
                    Process.Start(psi);
                }
                catch
                {
                    // 若 UseShellExecute 失败（如非交互环境），回退到 UseShellExecute = false
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = false;
                    Process.Start(psi);
                }

                // 3. 轮询等待其端口就绪（最多等待 8 秒）
                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed < TimeSpan.FromSeconds(8))
                {
                    Thread.Sleep(300);
                    var checkPing = PingAsync().GetAwaiter().GetResult();
                    if (checkPing is not null && string.Equals(checkPing.Status, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        _log.LogInformation("LocalAdmin 独立守护进程启动成功并已就绪 (PID: {Pid})！游戏服已由守护进程托管保护。", checkPing.Pid);
                        return true;
                    }
                }

                _log.LogWarning("守护进程已拉起，但等待就绪超时。Bot 将在后续请求时继续尝试连接。");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "自动拉起守护进程异常：{Message}", ex.Message);
                return false;
            }
        }
    }

    private void EnsureDaemonStarted()
    {
        if (!_options.AutoStartDaemon)
            return;

        lock (_startLock)
        {
            if (DateTime.UtcNow - _lastStartAttempt < TimeSpan.FromSeconds(15))
                return;

            _lastStartAttempt = DateTime.UtcNow;
            _ = Task.Run(() => EnsureDaemonRunningAtStartup());
        }
    }

    private async Task<T?> SendAsync<T>(Func<Task<HttpResponseMessage>> sendFunc) where T : class
    {
        try
        {
            var res = await sendFunc();
            if (!res.IsSuccessStatusCode)
            {
                if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                string content = await res.Content.ReadAsStringAsync();
                _log.LogWarning("Daemon 返回非成功状态码 {StatusCode}: {Body}", res.StatusCode, content);
                return null;
            }
            return await res.Content.ReadFromJsonAsync<T>(JsonOptions);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning("无法连接到 LocalAdmin 守护进程 ({Uri})：{Message}", _options.DaemonUri, ex.Message);
            EnsureDaemonStarted();
            return null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "请求 LocalAdmin 守护进程异常：{Message}", ex.Message);
            return null;
        }
    }

    private async Task<LocalAdminResult> SendResultAsync(Func<Task<HttpResponseMessage>> sendFunc)
    {
        try
        {
            var res = await sendFunc();
            var body = await res.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            bool success = body.TryGetProperty("success", out var s) && s.GetBoolean();
            string? response = body.TryGetProperty("response", out var r) ? r.GetString() : null;
            string? error = body.TryGetProperty("error", out var e) ? e.GetString() : null;

            if (success)
                return LocalAdminResult.Ok(response ?? "操作成功");
            return LocalAdminResult.Fail(error ?? response ?? "操作失败");
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning("守护进程通信失败 ({Uri})：{Message}", _options.DaemonUri, ex.Message);
            EnsureDaemonStarted();
            return LocalAdminResult.Fail($"无法连接到 LocalAdmin 守护节点（{_options.DaemonUri}）：请确认 Server_Qcha.Daemon.exe 正在运行");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "与守护进程通信异常：{Message}", ex.Message);
            return LocalAdminResult.Fail($"守护节点请求异常：{ex.Message}");
        }
    }

    public async Task<LocalAdminListResult> ListServersAsync(CancellationToken ct = default)
    {
        var result = await SendAsync<LocalAdminListResult>(() => _http.GetAsync("api/daemon/servers", ct));
        if (result is not null)
            return result;

        return new LocalAdminListResult(
            _options.Enabled,
            0,
            "daemon-disconnected",
            "",
            ConsoleCaptureLevels.DescribeTyped(),
            Array.Empty<LocalServerStatus>(),
            Array.Empty<LocalServerDefinition>());
    }

    public Task<LocalServerStatus?> GetStatusAsync(string id, CancellationToken ct = default)
    {
        return SendAsync<LocalServerStatus>(() => _http.GetAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}", ct));
    }

    public Task<LocalPollResult?> PollAsync(string id, long? after, int? limit, CancellationToken ct = default)
    {
        string uri = $"api/daemon/servers/{Uri.EscapeDataString(id)}/poll?after={after.GetValueOrDefault()}&limit={limit.GetValueOrDefault(400)}";
        return SendAsync<LocalPollResult>(() => _http.GetAsync(uri, ct));
    }

    public async Task<LocalSaveResult> CreateServerAsync(LocalServerSaveRequest request, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("api/daemon/servers", request, ct);
            var body = await res.Content.ReadFromJsonAsync<LocalSaveResult>(JsonOptions);
            return body ?? new LocalSaveResult(false, null, "解析守护进程返回失败", null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "创建服务器请求失败：{Message}", ex.Message);
            return new LocalSaveResult(false, null, $"守护节点请求失败：{ex.Message}", null);
        }
    }

    public async Task<LocalSaveResult> UpdateServerAsync(string id, LocalServerSaveRequest request, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}", request, ct);
            var body = await res.Content.ReadFromJsonAsync<LocalSaveResult>(JsonOptions);
            return body ?? new LocalSaveResult(false, null, "解析守护进程返回失败", null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "更新服务器请求失败：{Message}", ex.Message);
            return new LocalSaveResult(false, null, $"守护节点请求失败：{ex.Message}", null);
        }
    }

    public Task<LocalAdminResult> DeleteServerAsync(string id, CancellationToken ct = default)
    {
        return SendResultAsync(() => _http.DeleteAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}", ct));
    }

    public Task<LocalAdminResult> StartAsync(string id, CancellationToken ct = default)
    {
        return SendResultAsync(() => _http.PostAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}/start", null, ct));
    }

    public Task<LocalAdminResult> StopAsync(string id, bool force, CancellationToken ct = default)
    {
        return SendResultAsync(() => _http.PostAsJsonAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}/stop", new LocalStopRequest(force), ct));
    }

    public Task<LocalAdminResult> RestartAsync(string id, bool force, CancellationToken ct = default)
    {
        return SendResultAsync(() => _http.PostAsJsonAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}/restart", new LocalRestartRequest(force), ct));
    }

    public Task<LocalAdminResult> SendConsoleAsync(string id, string command, CancellationToken ct = default)
    {
        return SendResultAsync(() => _http.PostAsJsonAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}/console", new LocalConsoleRequest(command), ct));
    }

    public Task<LocalAdminResult> ClearConsoleAsync(string id, CancellationToken ct = default)
    {
        return SendResultAsync(() => _http.PostAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}/console/clear", null, ct));
    }

    public Task<LocalAdminResult> SetHeartbeatAsync(string id, bool enabled, CancellationToken ct = default)
    {
        return SendResultAsync(() => _http.PostAsJsonAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}/heartbeat", new LocalHeartbeatRequest(enabled), ct));
    }

    public async Task<LocalConsoleLevelResult> SetConsoleLevelAsync(string id, string? level, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.PostAsJsonAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}/console-level", new LocalConsoleLevelRequest(level), ct);
            var body = await res.Content.ReadFromJsonAsync<LocalConsoleLevelResult>(JsonOptions);
            return body ?? new LocalConsoleLevelResult(false, null, "解析守护进程返回失败", null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "设置控制台级别请求失败：{Message}", ex.Message);
            return new LocalConsoleLevelResult(false, null, $"守护节点请求失败：{ex.Message}", null);
        }
    }

    public Task<LocalAdminResult> CancelRestartAsync(string id, CancellationToken ct = default)
    {
        return SendResultAsync(() => _http.PostAsync($"api/daemon/servers/{Uri.EscapeDataString(id)}/cancel-restart", null, ct));
    }

    public async Task<LocalConfigInfo> GetConfigAsync(CancellationToken ct = default)
    {
        var info = await SendAsync<LocalConfigInfo>(() => _http.GetAsync("api/daemon/config", ct));
        if (info is not null)
            return info;

        return new LocalConfigInfo(
            _options.Enabled,
            AppContext.BaseDirectory,
            "",
            false,
            "daemon-disconnected",
            0,
            new LocalGlobalConfigDto(
                _options.ConsoleBufferLines,
                _options.LogDirectory,
                _options.WriteLogFiles,
                _options.LogExpirationDays,
                _options.DefaultExecutablePath),
            Array.Empty<LocalServerConfigSummaryDto>());
    }

    public Task<LocalAdminResult> ResaveConfigAsync(CancellationToken ct = default)
    {
        return SendResultAsync(() => _http.PostAsync("api/daemon/config/resave", null, ct));
    }

    public async Task<ScpslDiscoveryDto> FindExecutablesAsync(CancellationToken ct = default)
    {
        var res = await SendAsync<ScpslDiscoveryDto>(() => _http.GetAsync("api/daemon/executables", ct));
        if (res is not null)
            return res;

        var localSearch = ScpslLocator.Search(_options.DefaultExecutablePath);
        return localSearch.ToDto();
    }

    public async Task<DaemonPingResult?> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var res = await _http.GetAsync("api/daemon/ping", cts.Token);
            if (!res.IsSuccessStatusCode)
                return null;
            return await res.Content.ReadFromJsonAsync<DaemonPingResult>(JsonOptions, cts.Token);
        }
        catch
        {
            return null;
        }
    }

    public async Task<DaemonStatusResult> GetDaemonStatusAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var res = await _http.GetAsync("api/daemon/status", cts.Token);
            if (res.IsSuccessStatusCode)
            {
                var status = await res.Content.ReadFromJsonAsync<DaemonStatusResult>(JsonOptions, cts.Token);
                if (status is not null)
                    return status;
            }
        }
        catch
        {
            // 忽略异常，转为探测系统进程表
        }

        // HTTP 连接失败，探测系统进程表确认原因
        try
        {
            var procs = Process.GetProcessesByName("Server_Qcha.Daemon");
            if (procs.Length > 0)
            {
                var p = procs[0];
                p.Refresh();
                double wsMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 1);
                double privMb = Math.Round(p.PrivateMemorySize64 / (1024.0 * 1024.0), 1);
                double uptime = Math.Round((DateTime.UtcNow - p.StartTime.ToUniversalTime()).TotalSeconds, 0);

                return new DaemonStatusResult(
                    Online: false,
                    Status: "unresponsive",
                    Version: "2.0.0",
                    Pid: p.Id,
                    StartTime: p.StartTime,
                    UptimeSeconds: uptime,
                    MemoryWorkingSetMb: wsMb,
                    MemoryPrivateMb: privMb,
                    ThreadCount: p.Threads.Count,
                    ServerCount: 0,
                    RunningServerCount: 0,
                    ListenUri: _options.DaemonUri,
                    Message: "守护进程已在运行，但 HTTP 接口未响应（可能正在启动或初始化中）");
            }
        }
        catch
        {
            // 忽略进程检查异常
        }

        return new DaemonStatusResult(
            Online: false,
            Status: "offline",
            Version: "2.0.0",
            Pid: null,
            StartTime: null,
            UptimeSeconds: 0,
            MemoryWorkingSetMb: 0,
            MemoryPrivateMb: 0,
            ThreadCount: 0,
            ServerCount: 0,
            RunningServerCount: 0,
            ListenUri: _options.DaemonUri,
            Message: "守护进程未运行");
    }

    public async Task<LocalAdminResult> StartDaemonAsync(CancellationToken ct = default)
    {
        // 先检查是否已经在运行
        var ping = await PingAsync(ct);
        if (ping is not null)
            return LocalAdminResult.Ok($"守护进程已在运行中 (PID: {ping.Pid})");

        bool ok = EnsureDaemonRunningAtStartup();
        if (ok)
            return LocalAdminResult.Ok("独立守护进程已成功拉起并就绪");

        return LocalAdminResult.Fail("未能成功拉起守护进程，请检查目录中是否存在 Server_Qcha.Daemon.exe");
    }

    public async Task<LocalAdminResult> StopDaemonAsync(CancellationToken ct = default)
    {
        try
        {
            // 1. 先尝试向 HTTP 接口下发优雅关机
            await _http.PostAsync("api/daemon/shutdown", null, ct);
        }
        catch
        {
            // 如果 HTTP 已经连不上，忽略错误并进入进程查杀
        }

        // 2. 等待最多 3 秒让进程自退出
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(3))
        {
            if (Process.GetProcessesByName("Server_Qcha.Daemon").Length == 0)
                return LocalAdminResult.Ok("守护进程已安全停止");
            await Task.Delay(300, ct);
        }

        // 3. 超时强杀
        int killed = 0;
        foreach (var p in Process.GetProcessesByName("Server_Qcha.Daemon"))
        {
            try
            {
                p.Kill();
                killed++;
            }
            catch
            {
            }
        }

        return LocalAdminResult.Ok(killed > 0 ? "守护进程已被强制终止" : "守护进程已停止");
    }

    public async Task<LocalAdminResult> RestartDaemonAsync(CancellationToken ct = default)
    {
        var stopRes = await StopDaemonAsync(ct);
        await Task.Delay(1000, ct);
        var startRes = await StartDaemonAsync(ct);

        if (startRes.Success)
            return LocalAdminResult.Ok("守护进程已成功重启并就绪");

        return LocalAdminResult.Fail($"守护进程已停止，但重新拉起失败：{startRes.Error}");
    }

    public Task<LocalAdminResult> ShutdownDaemonAsync(CancellationToken ct = default)
    {
        return StopDaemonAsync(ct);
    }
}
