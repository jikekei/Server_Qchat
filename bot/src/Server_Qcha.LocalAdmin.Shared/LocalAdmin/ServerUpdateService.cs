using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin.Models;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// 通过 SteamCMD 更新托管的 SCP:SL 专用服务器（Steam App 996560）。
///
/// 流程：占用实例（禁止启动/重启/改配置）→ 若在运行则先优雅停止（超时强杀）→
/// 运行 <c>steamcmd +force_install_dir &lt;dir&gt; +login anonymous +app_update 996560 [-beta x] [validate] +quit</c>
/// → 输出实时写入该服务器控制台与任务日志 → 成功且需要时重新启动。
///
/// 运行在真正托管游戏进程的那一侧（Daemon 守护进程或 Embedded 内嵌模式的 Bot 进程）。
/// 同一台服务器同时只允许一个更新任务；SteamCMD 自身不支持并发，多台服务器的更新会排队串行执行。
/// </summary>
public sealed class ServerUpdateService : IHostedService, IDisposable
{
    private const int MaxLogLines = 400;
    private static readonly Regex BranchPattern = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly LocalAdminManager _manager;
    private readonly LocalAdminOptions _options;
    private readonly ILogger<ServerUpdateService> _log;
    private readonly ConcurrentDictionary<string, UpdateJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _steamCmdLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    public ServerUpdateService(LocalAdminManager manager, IOptions<LocalAdminOptions> options, ILogger<ServerUpdateService> log)
    {
        _manager = manager;
        _options = options.Value;
        _log = log;
    }

    // ==================================================================
    //  对外接口
    // ==================================================================

    /// <summary>查询某台服务器的更新状态（含环境预检）。服务器不存在返回 null。</summary>
    public LocalUpdateStatus? GetStatus(string id)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return null;

        _jobs.TryGetValue(instance.Id, out var job);
        return BuildStatus(instance, job);
    }

    /// <summary>发起更新；校验通过后立即返回，更新在后台执行，进度通过 <see cref="GetStatus"/> 轮询。</summary>
    public LocalUpdateStartResult StartUpdate(string id, LocalUpdateRequest? request, string? requestedBy)
    {
        request ??= new LocalUpdateRequest();

        if (!_manager.Enabled)
            return Fail("LocalAdmin 能力未启用：请先在 appsettings.json 设置 LocalAdmin:Enabled = true 并重启");

        var instance = _manager.Find(id);
        if (instance is null)
            return Fail($"本地服务器实例不存在：{id}");

        // ---- 预检：SteamCMD ----
        string steamCmd = ResolveSteamCmd(out string? steamCmdError);
        if (steamCmdError is not null)
            return Fail(steamCmdError, instance);

        // ---- 预检：安装目录 ----
        string installDir = ResolveInstallDirectory(instance.Definition, out string? dirError);
        if (dirError is not null)
            return Fail(dirError, instance);

        // ---- 预检：分支 ----
        string? branch = ResolveBranch(request.BetaBranch, instance.Definition, out string? branchError);
        if (branchError is not null)
            return Fail(branchError, instance);

        if (_options.SteamAppId <= 0)
            return Fail($"SteamAppId 配置不合法：{_options.SteamAppId}", instance);

        // ---- 占用实例，防止并发更新 ----
        if (!instance.TryBeginUpdate())
            return Fail($"[{instance.Name}] 已有更新任务在进行中，请等待其完成", instance);

        bool wasRunning = instance.Running;
        var job = new UpdateJob
        {
            ServerId = instance.Id,
            State = "queued",
            StartedAt = DateTime.Now,
            BetaBranch = branch,
            Validate = request.Validate ?? _options.UpdateValidate,
            RestartAfter = request.RestartAfter ?? wasRunning,
            WasRunning = wasRunning,
            RequestedBy = requestedBy,
            SteamCmdPath = steamCmd,
            InstallDirectory = installDir,
        };
        _jobs[instance.Id] = job;

        Log(instance, job, "info", $"更新任务已创建：App {_options.SteamAppId}，分支 {(branch ?? "public（正式版）")}，" +
                                   $"安装目录 {installDir}{(requestedBy is null ? "" : "，发起人 " + requestedBy)}");

        _ = Task.Run(() => RunJobAsync(instance, job));

        string msg = wasRunning
            ? $"已开始更新 [{instance.Name}]：将先停止服务器，更新完成后{(job.RestartAfter ? "自动重新启动" : "保持停止")}"
            : $"已开始更新 [{instance.Name}]";
        return new LocalUpdateStartResult(true, msg, null, BuildStatus(instance, job));
    }

    // ==================================================================
    //  后台执行
    // ==================================================================

    private async Task RunJobAsync(LocalServerInstance instance, UpdateJob job)
    {
        var ct = _shutdown.Token;
        bool lockTaken = false;

        try
        {
            if (_steamCmdLock.CurrentCount == 0)
                Log(instance, job, "info", "SteamCMD 正被其它服务器的更新占用，已排队等待…");

            await _steamCmdLock.WaitAsync(ct);
            lockTaken = true;

            // ---- 1. 停止服务器 ----
            if (instance.Running)
            {
                job.State = "stopping";
                Log(instance, job, "info", "服务器正在运行，先优雅停止（下发 exit）…");
                instance.Stop(force: false);

                var graceful = TimeSpan.FromSeconds(instance.Definition.GracefulStopTimeoutSeconds + 15);
                if (!await instance.WaitForExitAsync(graceful, ct))
                {
                    Log(instance, job, "warn", "等待超时，强制结束进程树…");
                    instance.Stop(force: true);
                    if (!await instance.WaitForExitAsync(TimeSpan.FromSeconds(20), ct))
                    {
                        Finish(instance, job, false, null, "无法停止服务器进程，已取消更新（服务端文件未改动）");
                        return;
                    }
                }

                Log(instance, job, "info", "服务器已停止。");
            }

            // ---- 2. 运行 SteamCMD ----
            job.State = "updating";
            var (exitCode, sawSuccess, errorLine) = await RunSteamCmdAsync(instance, job, ct);
            job.ExitCode = exitCode;

            bool success;
            string? error = null;
            if (exitCode is null)
            {
                success = false;
                error = $"SteamCMD 运行超时（{Math.Clamp(_options.UpdateTimeoutMinutes, 1, 1440)} 分钟），已结束进程";
            }
            else if (sawSuccess)
            {
                success = true;
            }
            else if (errorLine is not null)
            {
                success = false;
                error = $"SteamCMD 报告错误（退出码 {exitCode}）：{errorLine}";
            }
            else if (exitCode == 0)
            {
                success = true;
                Log(instance, job, "warn", "SteamCMD 退出码为 0，但未看到 “Success! App … fully installed” 提示，请自行核对输出");
            }
            else
            {
                success = false;
                error = $"SteamCMD 以非零退出码 {exitCode} 结束，更新可能未完成";
            }

            if (!success)
            {
                Finish(instance, job, false, null, error, restartBlocked: true);
                return;
            }

            // ---- 3. 按需重新启动 ----
            if (job.RestartAfter)
            {
                job.State = "starting";
                instance.EndUpdate();
                Log(instance, job, "info", "更新完成，正在重新启动服务器…");
                var start = instance.Start();
                if (!start.Success)
                {
                    Finish(instance, job, false, null, $"更新成功，但重新启动失败：{start.Error ?? start.Message}");
                    return;
                }

                Finish(instance, job, true, "更新完成，服务器已重新启动", null);
            }
            else
            {
                Finish(instance, job, true, job.WasRunning ? "更新完成，服务器保持停止状态" : "更新完成", null);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Finish(instance, job, false, null, "宿主进程正在关闭，更新已中止");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "更新服务器 [{Id}] 异常", instance.Id);
            Finish(instance, job, false, null, $"更新异常：{ex.Message}");
        }
        finally
        {
            instance.EndUpdate();
            if (lockTaken)
                _steamCmdLock.Release();
        }
    }

    /// <returns>退出码（超时为 null）、是否看到成功提示、首个错误行。</returns>
    private async Task<(int? ExitCode, bool SawSuccess, string? ErrorLine)> RunSteamCmdAsync(
        LocalServerInstance instance, UpdateJob job, CancellationToken ct)
    {
        var args = BuildSteamCmdArguments(job.InstallDirectory, _options.SteamAppId, job.BetaBranch, job.Validate);

        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Path.GetDirectoryName(job.SteamCmdPath) ?? Environment.CurrentDirectory,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        // Linux 上的 steamcmd.sh 可能没有可执行位，统一交给 bash 执行
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && job.SteamCmdPath.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = File.Exists("/bin/bash") ? "/bin/bash" : "bash";
            psi.ArgumentList.Add(job.SteamCmdPath);
        }
        else
        {
            psi.FileName = job.SteamCmdPath;
        }

        foreach (string a in args)
            psi.ArgumentList.Add(a);

        Log(instance, job, "info", $"执行：{job.SteamCmdPath} {string.Join(' ', args.Select(QuoteForDisplay))}");

        bool sawSuccess = false;
        string? errorLine = null;
        string appId = _options.SteamAppId.ToString();

        void OnLine(string? data, bool fromStderr)
        {
            if (string.IsNullOrWhiteSpace(data))
                return;

            string text = data.TrimEnd();
            if (text.Contains("Success!", StringComparison.OrdinalIgnoreCase) && text.Contains(appId, StringComparison.Ordinal))
                sawSuccess = true;

            bool isError = text.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                           || text.Contains("ERROR!", StringComparison.Ordinal)
                           || text.Contains("Failed to install", StringComparison.OrdinalIgnoreCase);
            if (isError && errorLine is null)
                errorLine = text;

            Log(instance, job, isError ? "error" : fromStderr ? "warn" : "steam", "[SteamCMD] " + text);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnLine(e.Data, false);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data, true);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Process.Start 返回 false");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"无法启动 SteamCMD（{job.SteamCmdPath}）：{ex.Message}", ex);
        }

        job.SteamCmdPid = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try { process.StandardInput.Close(); } catch { /* 不需要交互输入 */ }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(_options.UpdateTimeoutMinutes, 1, 1440)));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
            if (ct.IsCancellationRequested)
                throw;
            return (null, sawSuccess, errorLine);
        }
        finally
        {
            job.SteamCmdPid = null;
        }

        // 让异步读流把最后几行吐完
        try { process.WaitForExit(); } catch { /* 忽略 */ }

        int code = process.ExitCode;
        Log(instance, job, code == 0 ? "info" : "warn", $"SteamCMD 已退出，退出码 {code}");
        return (code, sawSuccess, errorLine);
    }

    private void Finish(LocalServerInstance instance, UpdateJob job, bool success, string? message, string? error, bool restartBlocked = false)
    {
        job.State = success ? "succeeded" : "failed";
        job.FinishedAt = DateTime.Now;
        job.Message = message;
        job.Error = error;

        if (success)
        {
            Log(instance, job, "info", message ?? "更新完成");
            _log.LogInformation("服务器 [{Id}] 更新完成：{Message}", instance.Id, message);
        }
        else
        {
            Log(instance, job, "error", "更新失败：" + error);
            if (restartBlocked && job.WasRunning)
                Log(instance, job, "warn", "由于更新失败，服务器不会自动重新启动，请检查后手动启动");
            _log.LogWarning("服务器 [{Id}] 更新失败：{Error}", instance.Id, error);
        }
    }

    private void Log(LocalServerInstance instance, UpdateJob job, string level, string text)
    {
        job.AddLine(level, text, MaxLogLines);
        try
        {
            instance.AppendUpdateLine(text, isError: level == "error", isWarning: level == "warn");
        }
        catch (Exception)
        {
            // 实例已释放时忽略
        }
    }

    // ==================================================================
    //  解析与校验（public static 便于测试）
    // ==================================================================

    /// <summary>拼装 SteamCMD 参数（不含可执行文件本身）。</summary>
    public static IReadOnlyList<string> BuildSteamCmdArguments(string installDir, int appId, string? betaBranch, bool validate)
    {
        var args = new List<string>
        {
            "+force_install_dir", installDir,
            "+login", "anonymous",
            "+app_update", appId.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(betaBranch))
        {
            args.Add("-beta");
            args.Add(betaBranch);
        }

        if (validate)
            args.Add("validate");

        args.Add("+quit");
        return args;
    }

    /// <summary>
    /// 解析分支：请求为 null 时沿用单服配置，再沿用全局配置；"" / "public" 表示正式版（返回 null）。
    /// </summary>
    public string? ResolveBranch(string? requested, LocalServerDefinition def, out string? error)
    {
        error = null;
        string raw = requested ?? (!string.IsNullOrWhiteSpace(def.BetaBranch) ? def.BetaBranch : _options.UpdateBetaBranch ?? "");
        raw = raw.Trim();

        if (raw.Length == 0 || raw.Equals("public", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!BranchPattern.IsMatch(raw))
        {
            error = $"分支名不合法：{raw}（只能包含英文字母、数字、点、下划线和连字符，长度 1-64）";
            return null;
        }

        return raw;
    }

    /// <summary>安装目录：优先单服 InstallDirectory，否则取可执行文件所在目录。必须是已存在的绝对路径。</summary>
    public static string ResolveInstallDirectory(LocalServerDefinition def, out string? error)
    {
        error = null;
        string dir = def.InstallDirectory?.Trim() ?? "";

        if (dir.Length == 0 && !string.IsNullOrWhiteSpace(def.ExecutablePath))
        {
            try
            {
                dir = Path.GetDirectoryName(Path.GetFullPath(def.ExecutablePath.Trim())) ?? "";
            }
            catch (Exception)
            {
                dir = "";
            }
        }

        if (dir.Length == 0)
        {
            error = "无法确定安装目录：请在服务器配置里填写「可执行文件」或「SteamCMD 安装目录」";
            return "";
        }

        if (dir.IndexOfAny(new[] { '\r', '\n', '\0', '"' }) >= 0)
        {
            error = "安装目录含有非法字符";
            return dir;
        }

        if (!Path.IsPathRooted(dir))
        {
            error = $"安装目录必须是绝对路径：{dir}";
            return dir;
        }

        if (!Directory.Exists(dir))
        {
            error = $"安装目录不存在：{dir}（请先确认服务端路径，或在服务器配置里修改「SteamCMD 安装目录」）";
            return dir;
        }

        return dir;
    }

    /// <summary>解析 SteamCMD 路径：配置优先，否则在 PATH 与常见位置查找。找不到时返回 error。</summary>
    public string ResolveSteamCmd(out string? error)
    {
        error = null;
        string configured = (_options.SteamCmdPath ?? "").Trim();

        if (configured.Length > 0)
        {
            string candidate = configured;
            if (Directory.Exists(candidate))
            {
                string? found = ExecutableNames().Select(n => Path.Combine(candidate, n)).FirstOrDefault(File.Exists);
                if (found is null)
                {
                    error = $"在配置的 SteamCMD 目录中没有找到 {string.Join(" / ", ExecutableNames())}：{candidate}";
                    return candidate;
                }
                candidate = found;
            }

            if (!File.Exists(candidate))
            {
                error = $"找不到 SteamCMD：{candidate}（请检查 LocalAdmin:SteamCmdPath 配置）";
                return candidate;
            }

            if (!IsAllowedSteamCmdName(Path.GetFileName(candidate)))
            {
                error = $"SteamCMD 文件名必须是 {string.Join(" / ", ExecutableNames())}：{candidate}";
                return candidate;
            }

            return Path.GetFullPath(candidate);
        }

        string? auto = AutoDetectSteamCmd();
        if (auto is null)
        {
            error = "未找到 SteamCMD：请安装 SteamCMD（https://developer.valvesoftware.com/wiki/SteamCMD），" +
                    "并在 appsettings.json 的 LocalAdmin:SteamCmdPath 中填写 " +
                    (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "steamcmd.exe" : "steamcmd.sh") + " 的完整路径";
            return "";
        }

        return auto;
    }

    private static string[] ExecutableNames() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? new[] { "steamcmd.exe" }
        : new[] { "steamcmd.sh", "steamcmd" };

    private static bool IsAllowedSteamCmdName(string? fileName) =>
        fileName is not null && (fileName.Equals("steamcmd.exe", StringComparison.OrdinalIgnoreCase)
                                 || fileName.Equals("steamcmd.sh", StringComparison.OrdinalIgnoreCase)
                                 || fileName.Equals("steamcmd", StringComparison.OrdinalIgnoreCase));

    private static string? AutoDetectSteamCmd()
    {
        var names = ExecutableNames();
        var dirs = new List<string>();

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
            dirs.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            dirs.Add(@"C:\steamcmd");
            dirs.Add(@"C:\SteamCMD");
            dirs.Add(@"D:\steamcmd");
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf)) dirs.Add(Path.Combine(pf, "steamcmd"));
            if (!string.IsNullOrEmpty(pf86)) dirs.Add(Path.Combine(pf86, "steamcmd"));
            if (!string.IsNullOrEmpty(home)) dirs.Add(Path.Combine(home, "steamcmd"));
        }
        else
        {
            if (!string.IsNullOrEmpty(home))
            {
                dirs.Add(Path.Combine(home, "steamcmd"));
                dirs.Add(Path.Combine(home, "Steam"));
                dirs.Add(Path.Combine(home, ".steam", "steamcmd"));
            }
            dirs.Add("/usr/games");
            dirs.Add("/opt/steamcmd");
            dirs.Add("/home/steam/steamcmd");
        }

        dirs.Add(AppContext.BaseDirectory);
        dirs.Add(Path.Combine(AppContext.BaseDirectory, "steamcmd"));

        foreach (string dir in dirs)
        {
            foreach (string name in names)
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim().Trim('"'), name);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch (Exception)
                {
                    // 非法路径片段，跳过
                }
            }
        }

        return null;
    }

    private static string QuoteForDisplay(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    private LocalUpdateStatus BuildStatus(LocalServerInstance instance, UpdateJob? job)
    {
        string steamCmd = ResolveSteamCmd(out string? steamErr);
        string installDir = ResolveInstallDirectory(instance.Definition, out string? dirErr);
        string defaultBranch = ResolveBranch(null, instance.Definition, out _) ?? "public";

        return new LocalUpdateStatus(
            instance.Id,
            job?.State ?? "idle",
            job is not null && job.FinishedAt is null,
            job?.StartedAt,
            job?.FinishedAt,
            job is null ? null : (job.BetaBranch ?? "public"),
            job?.Validate ?? _options.UpdateValidate,
            job?.RestartAfter ?? false,
            job?.WasRunning ?? false,
            job?.ExitCode,
            job?.Message,
            job?.Error,
            job?.RequestedBy,
            job?.SnapshotLines() ?? Array.Empty<UpdateLogLineDto>(),
            steamCmd,
            steamErr is null,
            installDir,
            dirErr is null,
            defaultBranch,
            _options.SteamAppId);
    }

    private LocalUpdateStartResult Fail(string error, LocalServerInstance? instance = null)
    {
        LocalUpdateStatus? status = null;
        if (instance is not null)
        {
            _jobs.TryGetValue(instance.Id, out var job);
            status = BuildStatus(instance, job);
        }
        return new LocalUpdateStartResult(false, null, error, status);
    }

    // ==================================================================
    //  生命周期
    // ==================================================================

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try { _shutdown.Cancel(); } catch (ObjectDisposedException) { }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _shutdown.Cancel(); } catch (ObjectDisposedException) { }
        _shutdown.Dispose();
        _steamCmdLock.Dispose();
    }

    private sealed class UpdateJob
    {
        private readonly object _sync = new();
        private readonly List<UpdateLogLineDto> _lines = new();
        private long _seq;

        public string ServerId { get; init; } = "";
        public volatile string State = "queued";
        public DateTime StartedAt { get; init; }
        public DateTime? FinishedAt { get; set; }
        public string? BetaBranch { get; init; }
        public bool Validate { get; init; }
        public bool RestartAfter { get; init; }
        public bool WasRunning { get; init; }
        public string? RequestedBy { get; init; }
        public string SteamCmdPath { get; init; } = "";
        public string InstallDirectory { get; init; } = "";
        public int? ExitCode { get; set; }
        public int? SteamCmdPid { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }

        public void AddLine(string level, string text, int max)
        {
            lock (_sync)
            {
                _lines.Add(new UpdateLogLineDto(++_seq, DateTime.Now, level, text));
                if (_lines.Count > max)
                    _lines.RemoveRange(0, _lines.Count - max);
            }
        }

        public IReadOnlyList<UpdateLogLineDto> SnapshotLines()
        {
            lock (_sync)
                return _lines.ToArray();
        }
    }
}
