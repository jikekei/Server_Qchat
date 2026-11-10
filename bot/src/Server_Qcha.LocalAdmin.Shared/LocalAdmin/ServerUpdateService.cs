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
/// 通过 SteamCMD 更新托管的 SCP:SL 专用服务器（Steam App 996560），尽量做到「热更新」。
///
/// 暂存切换流程（默认，服务端目录可整体切换时）：
/// <list type="number">
/// <item>把现有安装复制到同级暂存目录 <c>&lt;安装目录&gt;.qcha-staging</c>，正在运行的服务器不受影响；</item>
/// <item>对暂存目录运行 <c>steamcmd +force_install_dir &lt;暂存&gt; +login anonymous +app_update 996560 [-beta x] [validate] +quit</c>；</item>
/// <item>版本未变化且服务器在运行 → 什么都不做；否则进入「等待回合结束」：向游戏下发 <c>restartnextround</c>，
///       游戏在本回合结束重启时退出，本服务拦截该退出（不让实例按旧文件自动重启）；</item>
/// <item>目录切换：现有安装 → <c>.qcha-rollback</c>，暂存 → 安装目录，随后启动并做启动检查（等待心跳）；</item>
/// <item>新版本启动失败或检查期内崩溃 → 自动切回 <c>.qcha-rollback</c> 并重新启动旧版本。</item>
/// </list>
/// 兜底：服务器空闲（没有玩家）时立即切换；超过 <see cref="LocalAdminOptions.UpdatePendingTimeoutMinutes"/> 未等到回合结束则立即停服切换；
/// 面板可「立即更新」或「取消」。目录无法整体切换时退回原地更新（先停服、更新、再启动）。
///
/// 运行在真正托管游戏进程的那一侧（Daemon 守护进程或 Embedded 内嵌模式的 Bot 进程）。
/// 同一台服务器同时只允许一个更新任务；SteamCMD 自身不支持并发，多台服务器的下载阶段会排队串行执行。
/// </summary>
public sealed class ServerUpdateService : IHostedService, IDisposable
{
    public const string StagingSuffix = ".qcha-staging";
    public const string RollbackSuffix = ".qcha-rollback";
    public const string FailedSuffix = ".qcha-failed";

    private const int MaxLogLines = 400;
    private static readonly Regex BuildIdPattern = new("\"buildid\"\\s+\"(\\d+)\"", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>SteamCMD 在 Linux 上会输出 ANSI 控制序列（如 ESC[0m），写入日志前去掉。</summary>
    private static readonly Regex AnsiPattern = new(@"\x1B\[[0-9;?]*[A-Za-z]|\x1B[()][0-9A-Za-z]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>例：Update state (0x61) downloading, progress: 41.87 (250763255 / 598962208)</summary>
    private static readonly Regex ProgressPattern = new(@"Update state \(0x[0-9a-fA-F]+\)\s*([^,]+),\s*progress:\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

        string steamCmd = ResolveSteamCmd(out string? steamCmdError);
        if (steamCmdError is not null)
            return Fail(steamCmdError, instance);

        string installDir = ResolveInstallDirectory(instance.Definition, out string? dirError);
        if (dirError is not null)
            return Fail(dirError, instance);

        string? branch = ResolveBranch(request.BetaBranch, instance.Definition, out string? branchError);
        if (branchError is not null)
            return Fail(branchError, instance);

        string mode = NormalizeMode(request.Mode ?? _options.UpdateDefaultMode, out string? modeError);
        if (modeError is not null)
            return Fail(modeError, instance);

        if (_options.SteamAppId <= 0)
            return Fail($"SteamAppId 配置不合法：{_options.SteamAppId}", instance);

        if (!instance.TryBeginUpdate())
            return Fail($"[{instance.Name}] 已有更新任务在进行中，请等待其完成（或在更新窗口中取消）", instance);

        bool wasRunning = instance.Running;
        var job = new UpdateJob
        {
            Kind = JobKind.Update,
            ServerId = instance.Id,
            StartedAt = DateTime.Now,
            BetaBranch = branch,
            Validate = request.Validate ?? _options.UpdateValidate,
            RestartAfter = request.RestartAfter ?? wasRunning,
            RequestedMode = mode,
            WasRunning = wasRunning,
            RequestedBy = requestedBy,
            SteamCmdPath = steamCmd,
            InstallDirectory = installDir,
            Cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token),
        };
        _jobs[instance.Id] = job;

        Log(instance, job, "info", $"更新任务已创建：App {_options.SteamAppId}，分支 {(branch ?? "public（正式版）")}，" +
                                   $"方式 {DescribeMode(mode)}，安装目录 {installDir}{(requestedBy is null ? "" : "，发起人 " + requestedBy)}");

        _ = Task.Run(() => RunJobAsync(instance, job));

        string msg = !wasRunning
            ? $"已开始更新 [{instance.Name}]"
            : mode == "now"
                ? $"已开始更新 [{instance.Name}]：下载完成后立即重启到新版本"
                : $"已开始更新 [{instance.Name}]：先在后台下载，下载完成后将在当前回合结束时切换到新版本，玩家不受额外影响";
        return new LocalUpdateStartResult(true, msg, null, BuildStatus(instance, job));
    }

    /// <summary>取消更新：下载阶段直接中止；等待回合结束阶段撤销 restartnextround。切换文件期间不可取消。</summary>
    public LocalUpdateStartResult CancelUpdate(string id, string? requestedBy)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Fail($"本地服务器实例不存在：{id}");

        if (!_jobs.TryGetValue(instance.Id, out var job) || job.FinishedAt is not null)
            return Fail("当前没有进行中的更新任务", instance);

        switch (job.State)
        {
            case "queued":
            case "staging":
            case "updating":
                Log(instance, job, "warn", $"{requestedBy ?? "面板"} 取消了更新，正在中止…");
                job.CancelRequested = true;
                try { job.Cts.Cancel(); } catch (ObjectDisposedException) { }
                return new LocalUpdateStartResult(true, "已请求取消更新", null, BuildStatus(instance, job));

            case "pending":
                Log(instance, job, "warn", $"{requestedBy ?? "面板"} 取消了等待中的更新");
                job.Trigger("cancel");
                return new LocalUpdateStartResult(true, "已取消等待中的更新，服务器将继续以当前版本运行", null, BuildStatus(instance, job));

            default:
                return Fail("正在切换文件或检查新版本，此时无法取消，请稍候", instance);
        }
    }

    /// <summary>「立即更新」：跳过等待回合结束，马上停服切换。</summary>
    public LocalUpdateStartResult ApplyNow(string id, string? requestedBy)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Fail($"本地服务器实例不存在：{id}");

        if (!_jobs.TryGetValue(instance.Id, out var job) || job.FinishedAt is not null)
            return Fail("当前没有进行中的更新任务", instance);

        if (job.State is "queued" or "staging" or "updating")
        {
            job.ApplyImmediately = true;
            Log(instance, job, "info", $"{requestedBy ?? "面板"} 选择下载完成后立即切换，不再等待回合结束");
            return new LocalUpdateStartResult(true, "下载完成后将立即重启到新版本", null, BuildStatus(instance, job));
        }

        if (job.State != "pending")
            return Fail("更新已在切换中，无需再次触发", instance);

        Log(instance, job, "warn", $"{requestedBy ?? "面板"} 选择立即更新（不等待回合结束），玩家将掉线并在服务器重启后重新连接");
        job.Trigger("forceNow");
        return new LocalUpdateStartResult(true, "正在立即切换到新版本", null, BuildStatus(instance, job));
    }

    /// <summary>手动回滚：把上一次更新前的版本（.qcha-rollback）切换回来；当前版本保留为新的回滚目录。</summary>
    public LocalUpdateStartResult Rollback(string id, string? requestedBy)
    {
        var instance = _manager.Find(id);
        if (instance is null)
            return Fail($"本地服务器实例不存在：{id}");

        string installDir = ResolveInstallDirectory(instance.Definition, out string? dirError);
        if (dirError is not null)
            return Fail(dirError, instance);

        string rollback = installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + RollbackSuffix;
        if (!Directory.Exists(rollback))
            return Fail($"没有可回滚的旧版本（{rollback} 不存在）", instance);

        if (!instance.TryBeginUpdate())
            return Fail($"[{instance.Name}] 已有更新任务在进行中，请等待其完成", instance);

        var job = new UpdateJob
        {
            Kind = JobKind.Rollback,
            ServerId = instance.Id,
            StartedAt = DateTime.Now,
            WasRunning = instance.Running,
            RestartAfter = instance.Running,
            RequestedBy = requestedBy,
            RequestedMode = "now",
            InstallDirectory = installDir,
            Cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token),
        };
        _jobs[instance.Id] = job;
        Log(instance, job, "warn", $"{requestedBy ?? "面板"} 发起回滚：切换回上一版本（{rollback}）");
        _ = Task.Run(() => RunJobAsync(instance, job));
        return new LocalUpdateStartResult(true, job.WasRunning ? "正在回滚：服务器将重启到上一版本" : "正在回滚到上一版本", null, BuildStatus(instance, job));
    }

    // ==================================================================
    //  后台执行
    // ==================================================================

    private async Task RunJobAsync(LocalServerInstance instance, UpdateJob job)
    {
        var ct = job.Cts.Token;
        try
        {
            if (job.Kind == JobKind.Rollback)
            {
                await RunManualRollbackAsync(instance, job, ct);
                return;
            }

            if (CanSwapDirectories(instance.Definition, job.InstallDirectory, out string? swapProblem))
            {
                await RunStagedAsync(instance, job, ct);
                return;
            }

            if (instance.Running && job.RequestedMode != "now")
            {
                Finish(instance, job, false, null,
                    $"无法进行不停服的暂存更新：{swapProblem}。如可接受立即停服，请选择「立即更新」");
                return;
            }

            Log(instance, job, "warn", $"无法使用暂存切换（{swapProblem}），改为原地更新");
            await RunInPlaceAsync(instance, job, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Finish(instance, job, false, null,
                job.CancelRequested ? "更新已取消，正在运行的服务器未受影响" : "宿主进程正在关闭，更新已中止",
                state: job.CancelRequested ? "canceled" : "failed");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "更新服务器 [{Id}] 异常", instance.Id);
            Finish(instance, job, false, null, $"更新异常：{ex.Message}");
        }
        finally
        {
            instance.ExitInterceptor = null;
            job.Unsubscribe?.Invoke();
            instance.SetStartBlocked(false);

            // 更新期间被推迟的自动拉起：更新流程自己没把服务器拉起来时，在这里补上
            if (instance.ConsumeSuppressedAutoStart() && !instance.Running)
            {
                Log(instance, job, "info", "补上更新期间被推迟的自动拉起");
                instance.Start();
            }

            instance.EndUpdate();
            job.Cts.Dispose();
        }
    }

    // ---------------------- 暂存 + 回合结束切换 ----------------------

    private async Task RunStagedAsync(LocalServerInstance instance, UpdateJob job, CancellationToken ct)
    {
        string live = job.InstallDirectory;
        string staging = StagingPathFor(live);
        string rollback = RollbackPathFor(live);
        job.Mode = "staged";
        job.StagingDirectory = staging;
        job.LiveBuildId = ReadBuildId(live, _options.SteamAppId);

        // ---- 1. 准备暂存目录（复制现有安装，正在运行的服务器不受影响） ----
        job.State = "staging";
        if (Directory.Exists(staging))
        {
            Log(instance, job, "info", $"清理上次遗留的暂存目录：{staging}");
            await Task.Run(() => Directory.Delete(staging, recursive: true), ct);
        }

        long size = await Task.Run(() => DirectorySize(live), ct);
        string? spaceError = CheckFreeSpace(live, size);
        if (spaceError is not null)
        {
            if (instance.Running && job.RequestedMode != "now")
            {
                Finish(instance, job, false, null, spaceError + "。如可接受立即停服，请选择「立即更新」（原地更新不需要额外空间）");
                return;
            }

            Log(instance, job, "warn", spaceError + "，改为原地更新");
            await RunInPlaceAsync(instance, job, ct);
            return;
        }

        Log(instance, job, "info", $"复制现有安装到暂存目录（{size / 1024 / 1024} MB）：{staging}");
        var sw = Stopwatch.StartNew();
        int skipped = await Task.Run(() => CopyDirectory(live, staging, ct), ct);
        Log(instance, job, skipped > 0 ? "warn" : "info",
            $"复制完成，用时 {sw.Elapsed.TotalSeconds:F1} 秒" + (skipped > 0 ? $"（{skipped} 个文件无法读取已跳过，SteamCMD 校验时会补齐）" : ""));

        // ---- 2. SteamCMD 更新暂存目录 ----
        job.State = "updating";
        var (ok, error) = await RunSteamCmdLockedAsync(instance, job, staging, ct);
        if (!ok)
        {
            TryDeleteDirectory(instance, job, staging);
            Finish(instance, job, false, null, error + "（正在运行的服务器未受影响）");
            return;
        }

        job.StagedBuildId = ReadBuildId(staging, _options.SteamAppId);
        Log(instance, job, "info", $"暂存目录已更新：build {job.LiveBuildId ?? "?"} → {job.StagedBuildId ?? "?"}");

        if (instance.Running && job.StagedBuildId is not null && job.StagedBuildId == job.LiveBuildId)
        {
            TryDeleteDirectory(instance, job, staging);
            Finish(instance, job, true, $"已是最新版本（build {job.LiveBuildId}），无需重启服务器", null);
            return;
        }

        // ---- 3. 决定何时切换 ----
        string reason;
        if (!instance.Running)
        {
            reason = "stopped";
        }
        else
        {
            reason = await WaitForApplyMomentAsync(instance, job, ct);
            if (reason == "cancel")
            {
                await SetRestartNextRoundAsync(instance, job, enable: false, ct);
                TryDeleteDirectory(instance, job, staging);
                Finish(instance, job, false, "已取消，服务器继续以当前版本运行", null, state: "canceled");
                return;
            }
        }

        // ---- 4. 停服（若仍在运行）并切换目录 ----
        job.TriggerReason = reason;
        job.State = "applying";
        instance.SetStartBlocked(true);

        bool userStopped = reason == "userStopped";
        if (instance.Running)
        {
            if (job.RestartScheduled)
                await SetRestartNextRoundAsync(instance, job, enable: false, ct);
            if (!await StopInstanceAsync(instance, job, ct))
            {
                TryDeleteDirectory(instance, job, staging);
                Finish(instance, job, false, null, "无法停止服务器进程，已放弃本次更新（服务端文件未改动）");
                return;
            }
        }

        try
        {
            SwapIn(live, staging, rollback);
        }
        catch (Exception ex)
        {
            instance.SetStartBlocked(false);
            if (!userStopped && (job.WasRunning || job.RestartAfter || instance.ConsumeSuppressedAutoStart()))
                instance.Start();
            Finish(instance, job, false, null, $"切换目录失败：{ex.Message}（已恢复原有文件{(!userStopped && job.WasRunning ? "并重新启动" : "")}）");
            return;
        }

        Log(instance, job, "info", $"已切换到新版本（旧版本保留在 {rollback}，可随时回滚）");
        instance.SetStartBlocked(false);

        bool shouldStart = !userStopped && (job.WasRunning || job.RestartAfter || instance.ConsumeSuppressedAutoStart());
        if (!shouldStart)
        {
            Finish(instance, job, true, userStopped
                ? "已切换到新版本；服务器按你的操作保持停止"
                : $"更新完成（build {job.StagedBuildId ?? "?"}）", null);
            return;
        }

        // ---- 5. 启动并检查，失败自动回滚 ----
        await StartAndVerifyAsync(instance, job, live, rollback, ct);
    }

    /// <summary>等待切换时机，返回原因：roundRestart / exit / userStopped / idle / timeout / forceNow / cancel。</summary>
    private async Task<string> WaitForApplyMomentAsync(LocalServerInstance instance, UpdateJob job, CancellationToken ct)
    {
        if (job.RequestedMode == "now" || job.ApplyImmediately)
            return "forceNow";

        if (job.RequestedMode == "auto" && _options.UpdateApplyWhenIdle && instance.IsIdle)
        {
            Log(instance, job, "info", "服务器处于空闲态（没有玩家），立即切换到新版本");
            return "idle";
        }

        job.State = "pending";
        job.PendingSince = DateTime.Now;
        int timeoutMinutes = Math.Max(0, _options.UpdatePendingTimeoutMinutes);
        job.PendingDeadline = timeoutMinutes > 0 ? job.PendingSince.Value.AddMinutes(timeoutMinutes) : null;

        // 拦截游戏进程退出：本回合结束（restartnextround）、崩溃、面板重启 / 停止都会走到这里
        instance.ExitInterceptor = info =>
        {
            job.LastExit = info;
            if (job.State == "pending")
                job.Trigger(info.UserStopped ? "userStopped" : info.RestartRequested ? "roundRestart" : "exit");
            return true;
        };

        void OnRound()
        {
            if (job.State == "pending" && job.RestartScheduled)
                Log(instance, job, "info", "游戏报告回合重启，服务器即将按 restartnextround 重启并切换到新版本…");
        }
        void OnIdle()
        {
            if (job.RequestedMode == "auto" && _options.UpdateApplyWhenIdle && job.State == "pending")
            {
                Log(instance, job, "info", "服务器进入空闲态（没有玩家），立即切换到新版本");
                job.Trigger("idle");
            }
        }
        instance.RoundRestartObserved += OnRound;
        instance.IdleEntered += OnIdle;
        job.Unsubscribe = () =>
        {
            instance.RoundRestartObserved -= OnRound;
            instance.IdleEntered -= OnIdle;
        };

        job.RestartScheduled = await SetRestartNextRoundAsync(instance, job, enable: true, ct);
        Log(instance, job, "info", "新版本已就绪，等待当前回合结束后自动切换" +
            (job.PendingDeadline is null ? "" : $"（最迟 {job.PendingDeadline:HH:mm:ss}，超时将立即切换）"));

        Task delay = job.PendingDeadline is null
            ? Task.Delay(Timeout.Infinite, ct)
            : Task.Delay(job.PendingDeadline.Value - DateTime.Now, ct);
        var done = await Task.WhenAny(job.TriggerTask, delay);
        ct.ThrowIfCancellationRequested();

        if (done != job.TriggerTask)
        {
            Log(instance, job, "warn", $"等待回合结束超过 {timeoutMinutes} 分钟，立即切换");
            job.Trigger("timeout");
        }

        string reason = await job.TriggerTask;
        if (reason is "roundRestart" or "exit" or "userStopped")
            Log(instance, job, "info", reason switch
            {
                "roundRestart" => "回合结束，服务器已退出准备重启，开始切换到新版本",
                "userStopped" => "服务器已被手动停止，切换到新版本（之后保持停止）",
                _ => "服务器进程已退出，切换到新版本",
            });
        return reason;
    }

    /// <summary>
    /// 通过游戏内置的 <c>restartnextround</c>（开关式命令）设置 / 撤销「本回合结束后重启」，
    /// 并根据游戏回显 “Server WILL / WON'T restart after next round” 确认状态。
    /// </summary>
    private async Task<bool> SetRestartNextRoundAsync(LocalServerInstance instance, UpdateJob job, bool enable, CancellationToken ct)
    {
        if (!instance.Running)
            return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            long after = instance.LastSeq;
            var send = await instance.SendConsoleAsync("restartnextround", ct);
            if (!send.Success)
            {
                Log(instance, job, "warn", $"下发 restartnextround 失败：{send.Error ?? send.Message}");
                return false;
            }

            bool? state = null;
            for (int i = 0; i < 50 && state is null; i++)
            {
                await Task.Delay(100, ct);
                foreach (var line in instance.ReadLines(after, 500, out _, out _, out _))
                {
                    if (line.Text.Contains("WILL restart after next round", StringComparison.OrdinalIgnoreCase))
                        state = true;
                    else if (line.Text.Contains("WON'T restart after next round", StringComparison.OrdinalIgnoreCase))
                        state = false;
                }
            }

            if (state is null)
            {
                Log(instance, job, "warn", "未收到游戏对 restartnextround 的确认（控制台未连接或版本不支持）" +
                    (enable ? "，将依赖超时 / 空闲 / 手动「立即更新」兜底" : ""));
                return false;
            }

            if (state == enable)
            {
                Log(instance, job, "info", enable ? "已设置：本回合结束后重启服务器（restartnextround）" : "已撤销本回合结束后重启");
                return enable;
            }
            // 开关方向不对（例如管理员之前已手动设置过），再切换一次
        }

        return !enable;
    }

    private async Task StartAndVerifyAsync(LocalServerInstance instance, UpdateJob job, string live, string rollback, CancellationToken ct)
    {
        job.State = "starting";
        job.LastExit = null;
        // 检查期内的退出交给本服务处理（回滚），不让实例按崩溃策略反复重启
        instance.ExitInterceptor = info => { job.LastExit = info; return true; };

        var start = instance.Start();
        string? problem = start.Success ? null : $"启动失败：{start.Error ?? start.Message}";

        if (problem is null)
        {
            job.State = "verifying";
            problem = await VerifyStartupAsync(instance, job, ct);
        }

        if (problem is null)
        {
            instance.ExitInterceptor = null;
            Finish(instance, job, true, $"已更新到 build {job.StagedBuildId ?? ReadBuildId(live, _options.SteamAppId) ?? "?"}，服务器运行正常", null);
            return;
        }

        // ---- 自动回滚 ----
        job.State = "rollingback";
        Log(instance, job, "error", $"新版本{problem}，自动回滚到旧版本…");
        instance.SetStartBlocked(true);
        if (instance.Running)
            await StopInstanceAsync(instance, job, ct);

        try
        {
            string failed = live.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + FailedSuffix;
            if (Directory.Exists(failed))
                Directory.Delete(failed, recursive: true);
            Directory.Move(live, failed);
            Directory.Move(rollback, live);
            Log(instance, job, "warn", $"已切换回旧版本；失败的新版本保留在 {failed} 供排查");
        }
        catch (Exception ex)
        {
            instance.SetStartBlocked(false);
            Finish(instance, job, false, null, $"新版本{problem}，且回滚失败：{ex.Message}。请手动检查 {live} 与 {rollback}");
            return;
        }

        instance.SetStartBlocked(false);
        instance.ExitInterceptor = null;
        var restart = instance.Start();
        Finish(instance, job, false, null, $"新版本{problem}；已自动回滚到旧版本" +
            (restart.Success ? "并重新启动" : $"，但旧版本启动失败：{restart.Error ?? restart.Message}"), state: "rolledback");
    }

    /// <summary>启动检查：开启心跳时等待首个心跳，否则等待控制台连接并稳定运行 15 秒。返回问题描述，正常返回 null。</summary>
    private async Task<string?> VerifyStartupAsync(LocalServerInstance instance, UpdateJob job, CancellationToken ct)
    {
        int seconds = Math.Clamp(_options.UpdateHealthCheckSeconds, 15, 3600);
        var sw = Stopwatch.StartNew();
        Log(instance, job, "info", $"启动检查：最多等待 {seconds} 秒确认新版本正常运行…");

        while (sw.Elapsed.TotalSeconds < seconds)
        {
            await Task.Delay(1000, ct);
            var st = instance.GetStatus();
            if (!st.Running)
                return $"启动后进程退出（退出码 {job.LastExit?.ExitCode.ToString() ?? "?"}）";

            bool healthy = instance.Definition.EnableHeartbeat
                ? st.HeartbeatStatus == "active"
                : st.ConsoleConnected && sw.Elapsed.TotalSeconds >= 15;
            if (healthy)
            {
                Log(instance, job, "info", $"启动检查通过（{sw.Elapsed.TotalSeconds:F0} 秒）");
                return null;
            }
        }

        return $"在 {seconds} 秒内未{(instance.Definition.EnableHeartbeat ? "收到心跳" : "连上控制台")}";
    }

    private async Task RunManualRollbackAsync(LocalServerInstance instance, UpdateJob job, CancellationToken ct)
    {
        string live = job.InstallDirectory;
        string rollback = RollbackPathFor(live);
        string temp = live.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".qcha-swap";

        job.State = "applying";
        instance.SetStartBlocked(true);
        if (instance.Running && !await StopInstanceAsync(instance, job, ct))
        {
            Finish(instance, job, false, null, "无法停止服务器进程，已放弃回滚");
            return;
        }

        try
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
            Directory.Move(live, temp);
            Directory.Move(rollback, live);
            Directory.Move(temp, rollback);
        }
        catch (Exception ex)
        {
            if (!Directory.Exists(live) && Directory.Exists(temp))
                Directory.Move(temp, live);
            instance.SetStartBlocked(false);
            if (job.WasRunning)
                instance.Start();
            Finish(instance, job, false, null, $"回滚失败：{ex.Message}");
            return;
        }

        instance.SetStartBlocked(false);
        Log(instance, job, "info", $"已切换回上一版本（build {ReadBuildId(live, _options.SteamAppId) ?? "?"}）；刚才的版本保留在 {rollback}");
        if (job.WasRunning)
        {
            var start = instance.Start();
            if (!start.Success)
            {
                Finish(instance, job, false, null, $"已回滚，但启动失败：{start.Error ?? start.Message}");
                return;
            }
        }

        Finish(instance, job, true, job.WasRunning ? "已回滚到上一版本并重新启动" : "已回滚到上一版本", null);
    }

    // ---------------------- 原地更新（无法暂存时的兜底） ----------------------

    private async Task RunInPlaceAsync(LocalServerInstance instance, UpdateJob job, CancellationToken ct)
    {
        job.Mode = "inplace";
        instance.SetStartBlocked(true);

        if (instance.Running)
        {
            job.State = "stopping";
            if (!await StopInstanceAsync(instance, job, ct))
            {
                Finish(instance, job, false, null, "无法停止服务器进程，已取消更新（服务端文件未改动）");
                return;
            }
        }

        job.State = "updating";
        var (ok, error) = await RunSteamCmdLockedAsync(instance, job, job.InstallDirectory, ct);
        instance.SetStartBlocked(false);

        if (!ok)
        {
            if (job.WasRunning && job.RestartAfter)
            {
                job.State = "starting";
                Log(instance, job, "warn", "更新失败，正在用现有文件重新启动服务器…");
                var restart = instance.Start();
                Finish(instance, job, false, null, restart.Success
                    ? error + "；服务器已用原有文件重新启动"
                    : error + $"；且重新启动失败：{restart.Error ?? restart.Message}");
                return;
            }

            Finish(instance, job, false, null, error, restartBlocked: true);
            return;
        }

        if (job.RestartAfter)
        {
            job.State = "starting";
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

    // ---------------------- 公共步骤 ----------------------

    private async Task<bool> StopInstanceAsync(LocalServerInstance instance, UpdateJob job, CancellationToken ct)
    {
        if (!instance.Running)
            return true;

        Log(instance, job, "info", "停止服务器（下发 exit）…");
        instance.Stop(force: false);

        var graceful = TimeSpan.FromSeconds(instance.Definition.GracefulStopTimeoutSeconds + 15);
        if (!await instance.WaitForExitAsync(graceful, ct))
        {
            Log(instance, job, "warn", "等待超时，强制结束进程树…");
            instance.Stop(force: true);
            if (!await instance.WaitForExitAsync(TimeSpan.FromSeconds(20), ct))
                return false;
        }

        Log(instance, job, "info", "服务器已停止。");
        return true;
    }

    /// <summary>排队获取 SteamCMD 并运行，返回是否成功与错误描述。</summary>
    private async Task<(bool Ok, string? Error)> RunSteamCmdLockedAsync(LocalServerInstance instance, UpdateJob job, string targetDir, CancellationToken ct)
    {
        if (_steamCmdLock.CurrentCount == 0)
            Log(instance, job, "info", "SteamCMD 正被其它服务器的更新占用，已排队等待…");

        await _steamCmdLock.WaitAsync(ct);
        try
        {
            var (exitCode, sawSuccess, errorLine) = await RunSteamCmdAsync(instance, job, targetDir, ct);
            job.ExitCode = exitCode;

            if (exitCode is null)
                return (false, $"SteamCMD 运行超时（{Math.Clamp(_options.UpdateTimeoutMinutes, 1, 1440)} 分钟），已结束进程");
            if (sawSuccess)
                return (true, null);
            if (errorLine is not null)
                return (false, $"SteamCMD 报告错误（退出码 {exitCode}）：{errorLine}");
            if (exitCode == 0)
            {
                Log(instance, job, "warn", "SteamCMD 退出码为 0，但未看到 “Success! App … fully installed” 提示，请自行核对输出");
                return (true, null);
            }

            return (false, $"SteamCMD 以非零退出码 {exitCode} 结束，更新可能未完成");
        }
        finally
        {
            _steamCmdLock.Release();
        }
    }

    /// <returns>退出码（超时为 null）、是否看到成功提示、首个错误行。</returns>
    private async Task<(int? ExitCode, bool SawSuccess, string? ErrorLine)> RunSteamCmdAsync(
        LocalServerInstance instance, UpdateJob job, string targetDir, CancellationToken ct)
    {
        var args = BuildSteamCmdArguments(targetDir, _options.SteamAppId, job.BetaBranch, job.Validate);

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

            string text = CleanLine(data);
            if (text.Length == 0)
                return;

            var progress = ProgressPattern.Match(text);
            if (progress.Success && double.TryParse(progress.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double pct))
            {
                string phase = progress.Groups[1].Value.Trim();
                if (!phase.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                {
                    job.ProgressPhase = phase;
                    job.ProgressPercent = Math.Clamp(pct, 0, 100);
                }
            }
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

    /// <summary>去掉 ANSI 控制序列与其它不可见控制字符。</summary>
    public static string CleanLine(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";

        string text = AnsiPattern.Replace(raw, "");
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '\t' || !char.IsControl(c))
                sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private static string QuoteForDisplay(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    private void Finish(LocalServerInstance instance, UpdateJob job, bool success, string? message, string? error,
        bool restartBlocked = false, string? state = null)
    {
        job.State = state ?? (success ? "succeeded" : "failed");
        job.FinishedAt = DateTime.Now;
        job.Message = message;
        job.Error = error;

        if (success || state == "canceled")
        {
            Log(instance, job, "info", message ?? error ?? "更新完成");
            _log.LogInformation("服务器 [{Id}] 更新结束（{State}）：{Message}", instance.Id, job.State, message ?? error);
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
    //  目录切换相关（public static 便于测试）
    // ==================================================================

    public static string StagingPathFor(string live) => live.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + StagingSuffix;

    public static string RollbackPathFor(string live) => live.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + RollbackSuffix;

    /// <summary>暂存切换的前提：安装目录不是磁盘根目录，且可执行文件位于安装目录之内（切换后路径仍然有效）。</summary>
    public static bool CanSwapDirectories(LocalServerDefinition def, string installDir, out string? problem)
    {
        problem = null;
        string full = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent))
        {
            problem = "安装目录是磁盘根目录";
            return false;
        }

        string exe;
        try { exe = Path.GetFullPath(def.ExecutablePath); }
        catch (Exception) { problem = "可执行文件路径不合法"; return false; }

        var cmp = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!exe.StartsWith(full + Path.DirectorySeparatorChar, cmp))
        {
            problem = "可执行文件不在安装目录之内";
            return false;
        }

        return true;
    }

    /// <summary>live → rollback，staging → live；第二步失败时恢复 live。</summary>
    public static void SwapIn(string live, string staging, string rollback)
    {
        if (!Directory.Exists(staging))
            throw new DirectoryNotFoundException($"暂存目录不存在：{staging}");

        if (Directory.Exists(rollback))
            Directory.Delete(rollback, recursive: true);

        Directory.Move(live, rollback);
        try
        {
            Directory.Move(staging, live);
        }
        catch
        {
            Directory.Move(rollback, live);
            throw;
        }
    }

    /// <summary>读取 steamapps/appmanifest_&lt;appid&gt;.acf 中的 buildid。</summary>
    public static string? ReadBuildId(string installDir, int appId)
    {
        try
        {
            string path = Path.Combine(installDir, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(path))
                return null;
            var m = BuildIdPattern.Match(File.ReadAllText(path));
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>递归复制目录（保留符号链接与 Unix 权限），返回因无法读取而跳过的文件数。</summary>
    public static int CopyDirectory(string source, string destination, CancellationToken ct = default)
    {
        int skipped = 0;
        bool unix = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        Directory.CreateDirectory(destination);

        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, entry.Name);

            if (entry.LinkTarget is not null)
            {
                if (entry is DirectoryInfo)
                    Directory.CreateSymbolicLink(target, entry.LinkTarget);
                else
                    File.CreateSymbolicLink(target, entry.LinkTarget);
                continue;
            }

            if (entry is DirectoryInfo dir)
            {
                skipped += CopyDirectory(dir.FullName, target, ct);
                continue;
            }

            try
            {
                File.Copy(entry.FullName, target, overwrite: true);
                if (unix)
                    File.SetUnixFileMode(target, File.GetUnixFileMode(entry.FullName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        if (unix)
        {
            try { File.SetUnixFileMode(destination, File.GetUnixFileMode(source)); } catch { /* 忽略 */ }
        }

        return skipped;
    }

    private static long DirectorySize(string dir)
    {
        long total = 0;
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            total += f.Length;
        return total;
    }

    private static string? CheckFreeSpace(string live, long size)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(live)) ?? live;
            var drive = new DriveInfo(Path.GetDirectoryName(Path.GetFullPath(live)) ?? root);
            long need = (long)(size * 1.1) + 512L * 1024 * 1024;
            if (drive.AvailableFreeSpace < need)
                return $"磁盘空间不足：暂存更新约需 {need / 1024 / 1024} MB，可用 {drive.AvailableFreeSpace / 1024 / 1024} MB";
        }
        catch (Exception)
        {
            // 无法判断时不阻止
        }

        return null;
    }

    private void TryDeleteDirectory(LocalServerInstance instance, UpdateJob job, string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            Log(instance, job, "warn", $"清理目录失败：{dir}（{ex.Message}）");
        }
    }

    public static string NormalizeMode(string? raw, out string? error)
    {
        error = null;
        string m = (raw ?? "").Trim().ToLowerInvariant();
        switch (m)
        {
            case "":
            case "auto":
                return "auto";
            case "roundend":
            case "round-end":
            case "round_end":
                return "roundEnd";
            case "now":
            case "immediate":
                return "now";
            default:
                error = $"更新方式不合法：{raw}（可选 auto / roundEnd / now）";
                return "auto";
        }
    }

    private static string DescribeMode(string mode) => mode switch
    {
        "roundEnd" => "回合结束后切换",
        "now" => "立即更新",
        _ => "自动（运行中等回合结束，空闲或已停止立即）",
    };

    private LocalUpdateStatus BuildStatus(LocalServerInstance instance, UpdateJob? job)
    {
        string steamCmd = ResolveSteamCmd(out string? steamErr);
        string installDir = ResolveInstallDirectory(instance.Definition, out string? dirErr);
        string defaultBranch = ResolveBranch(null, instance.Definition, out _) ?? "public";
        string? rollback = installDir.Length > 0 ? RollbackPathFor(installDir) : null;
        bool rollbackAvailable = rollback is not null && Directory.Exists(rollback);

        return new LocalUpdateStatus(
            instance.Id,
            job?.State ?? "idle",
            job is not null && job.FinishedAt is null,
            job?.StartedAt,
            job?.FinishedAt,
            job is null || job.Kind == JobKind.Rollback ? null : (job.BetaBranch ?? "public"),
            job?.Validate ?? _options.UpdateValidate,
            job?.RestartAfter ?? false,
            job?.WasRunning ?? false,
            job?.ExitCode,
            job?.Message,
            job?.Error,
            job?.RequestedBy,
            job?.SnapshotLines() ?? Array.Empty<UpdateLogLineDto>(),
            job?.ProgressPhase,
            job?.ProgressPercent,
            job?.Mode ?? (job?.Kind == JobKind.Rollback ? "rollback" : "auto"),
            job?.StagingDirectory,
            job?.LiveBuildId ?? (installDir.Length > 0 && dirErr is null ? ReadBuildId(installDir, _options.SteamAppId) : null),
            job?.StagedBuildId,
            job?.PendingSince,
            job?.PendingDeadline,
            job?.TriggerReason,
            rollbackAvailable,
            rollbackAvailable ? rollback : null,
            steamCmd,
            steamErr is null,
            installDir,
            dirErr is null,
            defaultBranch,
            _options.SteamAppId,
            NormalizeMode(_options.UpdateDefaultMode, out _) ?? "auto",
            _options.UpdatePendingTimeoutMinutes);
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

    private enum JobKind
    {
        Update,
        Rollback,
    }

    private sealed class UpdateJob
    {
        private readonly object _sync = new();
        private readonly List<UpdateLogLineDto> _lines = new();
        private readonly TaskCompletionSource<string> _trigger = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _seq;

        public JobKind Kind { get; init; }
        public string ServerId { get; init; } = "";
        public volatile string State = "queued";
        public DateTime StartedAt { get; init; }
        public DateTime? FinishedAt { get; set; }
        public string? BetaBranch { get; init; }
        public bool Validate { get; init; }
        public bool RestartAfter { get; init; }
        public string RequestedMode { get; init; } = "auto";
        public bool WasRunning { get; init; }
        public string? RequestedBy { get; init; }
        public string SteamCmdPath { get; init; } = "";
        public string InstallDirectory { get; init; } = "";
        public CancellationTokenSource Cts { get; init; } = new();
        public int? ExitCode { get; set; }
        public int? SteamCmdPid { get; set; }
        public volatile string? ProgressPhase;
        public double? ProgressPercent { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }

        public string? Mode { get; set; }
        public string? StagingDirectory { get; set; }
        public string? LiveBuildId { get; set; }
        public string? StagedBuildId { get; set; }
        public DateTime? PendingSince { get; set; }
        public DateTime? PendingDeadline { get; set; }
        public string? TriggerReason { get; set; }
        public volatile bool CancelRequested;
        public volatile bool ApplyImmediately;
        public bool RestartScheduled { get; set; }
        public LocalExitInfo? LastExit { get; set; }
        public Action? Unsubscribe { get; set; }

        public Task<string> TriggerTask => _trigger.Task;

        public void Trigger(string reason) => _trigger.TrySetResult(reason);

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
