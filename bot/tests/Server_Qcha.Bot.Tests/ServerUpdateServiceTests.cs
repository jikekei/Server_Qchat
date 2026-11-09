using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin;
using Server.Qcat.LocalAdmin.Models;
using Xunit;

namespace Server_Qcha.Bot.Tests;

/// <summary>
/// SteamCMD 更新测试。类 Unix 上用 bash 脚本模拟 steamcmd.sh，并用 FakeGame（tests/LocalAdmin.SmokeTest）
/// 模拟 SCPSL：restartnextround 开关、回合重启、空闲态与「坏掉的新版本」。
/// </summary>
public class ServerUpdateServiceTests
{
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string root) => ContentRootPath = root;
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Env : IAsyncDisposable
    {
        public required LocalAdminManager Manager { get; init; }
        public required ServerUpdateService Updater { get; init; }
        public required string Root { get; init; }
        public required string Live { get; init; }
        public LocalServerInstance Instance => Manager.Find("main")!;

        public async ValueTask DisposeAsync()
        {
            Updater.Dispose();
            await Manager.StopAsync(CancellationToken.None);
            try { Directory.Delete(Root, true); } catch { /* 忽略 */ }
        }
    }

    private static string FakeGameDll => Path.Combine(AppContext.BaseDirectory, "FakeGame.dll");

    /// <summary>
    /// 构造测试环境：root/scpsl 为现有安装（version v1，build 100），root/steamcmd/steamcmd.sh 为假 SteamCMD。
    /// 假 SteamCMD 会把 force_install_dir 指向的目录改成 version=v2、build=<paramref name="newBuild"/>。
    /// </summary>
    private static Env Create(Action<LocalAdminOptions>? configure = null, string newBuild = "200", bool newVersionBroken = false, string? steamScriptBody = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "qcha-update-" + Guid.NewGuid().ToString("N"));
        string live = Path.Combine(root, "scpsl");
        Directory.CreateDirectory(Path.Combine(live, "steamapps"));
        File.WriteAllText(Path.Combine(live, "version.txt"), "v1");
        File.WriteAllText(Path.Combine(live, "steamapps", "appmanifest_996560.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"996560\"\n\t\"buildid\"\t\t\"100\"\n}\n");

        string exe = Path.Combine(live, OperatingSystem.IsWindows() ? "SCPSL.exe" : "SCPSL.x86_64");
        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(exe, $"#!/bin/bash\nexec '{Environment.ProcessPath}' '{FakeGameDll}' \"$@\"\n");
            File.SetUnixFileMode(exe, (UnixFileMode)Convert.ToInt32("755", 8));
        }

        string steamDir = Path.Combine(root, "steamcmd");
        Directory.CreateDirectory(steamDir);
        string script = Path.Combine(steamDir, "steamcmd.sh");
        File.WriteAllText(script, steamScriptBody ??
            "#!/bin/bash\n" +
            $"printf '%s\\n' \"$@\" > '{Path.Combine(steamDir, "args.txt")}'\n" +
            "dir=\"$2\"\n" +
            "printf '\\033[0m Update state (0x61) downloading, progress: 42.00 (1 / 2)\\n'\n" +
            "echo v2 > \"$dir/version.txt\"\n" +
            $"printf '\"AppState\"\\n{{\\n\\t\"buildid\"\\t\\t\"{newBuild}\"\\n}}\\n' > \"$dir/steamapps/appmanifest_996560.acf\"\n" +
            (newVersionBroken ? "touch \"$dir/broken.txt\"\n" : "") +
            "printf \"\\033[0mSuccess! App '996560' fully installed.\\n\"\n" +
            "exit 0\n");

        var options = new LocalAdminOptions
        {
            Enabled = true,
            Mode = "Embedded",
            WriteLogFiles = false,
            SteamCmdPath = steamDir,
            UpdateHealthCheckSeconds = 20,
            Servers =
            {
                new LocalServerDefinition
                {
                    Id = "main",
                    Name = "主服",
                    ExecutablePath = exe,
                    GamePort = 7777,
                    GracefulStopTimeoutSeconds = 5,
                    RestartOnCrash = false,
                },
            },
        };
        configure?.Invoke(options);

        var opts = Options.Create(options);
        var manager = new LocalAdminManager(opts, NullLoggerFactory.Instance, NullLogger<LocalAdminManager>.Instance, new TestHostEnvironment(root));
        manager.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var updater = new ServerUpdateService(manager, opts, NullLogger<ServerUpdateService>.Instance);
        return new Env { Manager = manager, Updater = updater, Root = root, Live = live };
    }

    private static async Task<LocalUpdateStatus> WaitAsync(ServerUpdateService updater, Func<LocalUpdateStatus, bool> until, int seconds = 60)
    {
        LocalUpdateStatus? status = null;
        for (int i = 0; i < seconds * 10; i++)
        {
            status = updater.GetStatus("main")!;
            if (until(status))
                return status;
            await Task.Delay(100);
        }

        throw new TimeoutException($"等待超时，当前状态 {status?.State}：\n" + string.Join("\n", status?.Lines.Select(l => l.Text) ?? Array.Empty<string>()));
    }

    private static Task<LocalUpdateStatus> WaitDoneAsync(ServerUpdateService updater, int seconds = 60) =>
        WaitAsync(updater, s => !s.Active, seconds);

    private static async Task StartGameAsync(Env env)
    {
        Assert.True(env.Instance.Start().Success);
        for (int i = 0; i < 100 && env.Instance.GetStatus().HeartbeatStatus != "active"; i++)
            await Task.Delay(100);
        Assert.Equal("active", env.Instance.GetStatus().HeartbeatStatus);
    }

    private static string ConsoleText(Env env) =>
        string.Join("\n", env.Instance.ReadLines(0, 5000, out _, out _, out _).Select(l => l.Text));

    // ==================== 纯函数 ====================

    [Fact]
    public void BuildArguments_MatchesSteamCmdSyntax()
    {
        var args = ServerUpdateService.BuildSteamCmdArguments("/srv/scpsl", 996560, "beta", validate: true);
        Assert.Equal(new[] { "+force_install_dir", "/srv/scpsl", "+login", "anonymous", "+app_update", "996560", "-beta", "beta", "validate", "+quit" }, args);

        var publicArgs = ServerUpdateService.BuildSteamCmdArguments("/srv/scpsl", 996560, null, validate: false);
        Assert.Equal(new[] { "+force_install_dir", "/srv/scpsl", "+login", "anonymous", "+app_update", "996560", "+quit" }, publicArgs);
    }

    [Fact]
    public void CleanLine_StripsAnsiFromRealSteamCmdOutput()
    {
        Assert.Equal("Success! App '996560' fully installed.", ServerUpdateService.CleanLine("\u001b[0mSuccess! App '996560' fully installed."));
        Assert.Equal("Unloading Steam API...OK", ServerUpdateService.CleanLine("\u001b[0mUnloading Steam API...\u001b[0mOK\r"));
    }

    [Fact]
    public void NormalizeMode_AcceptsKnownValues()
    {
        Assert.Equal("auto", ServerUpdateService.NormalizeMode(null, out _));
        Assert.Equal("roundEnd", ServerUpdateService.NormalizeMode("RoundEnd", out _));
        Assert.Equal("now", ServerUpdateService.NormalizeMode("now", out _));
        ServerUpdateService.NormalizeMode("whenever", out var err);
        Assert.NotNull(err);
    }

    [Fact]
    public void CanSwapDirectories_RequiresExecutableInsideInstallDir()
    {
        string root = Path.GetTempPath();
        var def = new LocalServerDefinition { ExecutablePath = Path.Combine(root, "a", "SCPSL.x86_64") };
        Assert.True(ServerUpdateService.CanSwapDirectories(def, Path.Combine(root, "a"), out _));
        Assert.False(ServerUpdateService.CanSwapDirectories(def, Path.Combine(root, "b"), out var problem));
        Assert.NotNull(problem);
    }

    // ==================== 预检 ====================

    [Fact]
    public async Task StartUpdate_MissingSteamCmd_FailsWithClearMessage()
    {
        await using var env = Create(o => o.SteamCmdPath = "/nonexistent/steamcmd.sh");
        var result = env.Updater.StartUpdate("main", new LocalUpdateRequest(), "tester");
        Assert.False(result.Success);
        Assert.Contains("SteamCMD", result.Error);
        Assert.False(result.Status!.SteamCmdFound);
        Assert.False(env.Instance.UpdateInProgress);
    }

    [Fact]
    public async Task StartUpdate_UnknownServerAndInvalidBranch_Rejected()
    {
        await using var env = Create();
        Assert.False(env.Updater.StartUpdate("nope", null, null).Success);
        Assert.Null(env.Updater.GetStatus("nope"));

        var def = env.Instance.Definition;
        Assert.Null(env.Updater.ResolveBranch("public", def, out var e1));
        Assert.Null(e1);
        Assert.Equal("beta", env.Updater.ResolveBranch("beta", def, out _));
        env.Updater.ResolveBranch("bad branch; rm -rf", def, out var e2);
        Assert.NotNull(e2);
    }

    // ==================== 已停止：暂存切换 + 手动回滚 ====================

    [Fact]
    public async Task StoppedServer_StagedUpdate_SwapsAndKeepsRollback()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var env = Create();
        var start = env.Updater.StartUpdate("main", new LocalUpdateRequest(BetaBranch: "beta"), "tester");
        Assert.True(start.Success, start.Error);
        Assert.False(env.Updater.StartUpdate("main", null, "tester").Success);   // 同一台不允许并发

        var status = await WaitDoneAsync(env.Updater);
        Assert.Equal("succeeded", status.State);
        Assert.Equal("staged", status.Mode);
        Assert.Equal("100", status.LiveBuildId);
        Assert.Equal("200", status.StagedBuildId);
        Assert.Equal(42.0, status.ProgressPercent);
        Assert.Contains(status.Lines, l => l.Text == "[SteamCMD] Success! App '996560' fully installed.");

        string[] passed = File.ReadAllLines(Path.Combine(env.Root, "steamcmd", "args.txt"));
        Assert.Equal(ServerUpdateService.StagingPathFor(env.Live), passed[1]);
        Assert.Equal(new[] { "-beta", "beta" }, passed[6..8]);

        Assert.Equal("v2", File.ReadAllText(Path.Combine(env.Live, "version.txt")).Trim());
        string rollback = ServerUpdateService.RollbackPathFor(env.Live);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(rollback, "version.txt")).Trim());
        Assert.False(Directory.Exists(ServerUpdateService.StagingPathFor(env.Live)));
        Assert.True(File.GetUnixFileMode(env.Instance.Definition.ExecutablePath).HasFlag(UnixFileMode.UserExecute));
        Assert.True(status.RollbackAvailable);
        Assert.False(env.Instance.Running);

        // 手动回滚：切回 v1，v2 变成新的回滚目录
        Assert.True(env.Updater.Rollback("main", "tester").Success);
        var rb = await WaitDoneAsync(env.Updater);
        Assert.Equal("succeeded", rb.State);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(env.Live, "version.txt")).Trim());
        Assert.Equal("v2", File.ReadAllText(Path.Combine(rollback, "version.txt")).Trim());
    }

    [Fact]
    public async Task SteamCmdReportsError_FailsAndLeavesLiveUntouched()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var env = Create(steamScriptBody: "#!/bin/bash\necho \"ERROR! Failed to install app '996560' (No subscription)\"\nexit 8\n");
        Assert.True(env.Updater.StartUpdate("main", null, null).Success);
        var status = await WaitDoneAsync(env.Updater);
        Assert.Equal("failed", status.State);
        Assert.Equal(8, status.ExitCode);
        Assert.Contains("No subscription", status.Error);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(env.Live, "version.txt")).Trim());
        Assert.False(Directory.Exists(ServerUpdateService.StagingPathFor(env.Live)));
    }

    // ==================== 运行中：回合结束切换 ====================

    [Fact]
    public async Task RunningServer_RoundEnd_SwitchesOnRoundRestart()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var env = Create();
        await StartGameAsync(env);
        int oldPid = env.Instance.GetStatus().ProcessId!.Value;

        Assert.True(env.Updater.StartUpdate("main", new LocalUpdateRequest(Mode: "roundEnd"), "tester").Success);
        var pending = await WaitAsync(env.Updater, s => s.State == "pending" || !s.Active);
        Assert.Equal("pending", pending.State);
        Assert.NotNull(pending.PendingDeadline);
        await WaitAsync(env.Updater, s => s.Lines.Any(l => l.Text.Contains("restartnextround")));

        // 等待期间服务器继续以旧版本运行
        Assert.True(env.Instance.Running);
        Assert.Equal(oldPid, env.Instance.GetStatus().ProcessId);
        Assert.Contains("Server WILL restart after next round", ConsoleText(env));

        // 模拟回合结束：游戏按 restartnextround 退出 → 切换 → 以新版本启动
        await env.Instance.SendConsoleAsync("roundrestart", CancellationToken.None);
        var done = await WaitDoneAsync(env.Updater);
        Assert.True(done.State == "succeeded", done.Error + "\n" + string.Join("\n", done.Lines.Select(l => l.Text)));
        Assert.Equal("roundRestart", done.TriggerReason);
        Assert.True(env.Instance.Running);
        Assert.NotEqual(oldPid, env.Instance.GetStatus().ProcessId);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(env.Live, "version.txt")).Trim());
        Assert.Contains("[FakeGame] version=v2", ConsoleText(env));
        Assert.Null(env.Instance.ExitInterceptor);
    }

    [Fact]
    public async Task RunningServer_Auto_WhenIdle_AppliesImmediately()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var env = Create();
        await StartGameAsync(env);
        await env.Instance.SendConsoleAsync("idle", CancellationToken.None);
        for (int i = 0; i < 50 && !env.Instance.IsIdle; i++)
            await Task.Delay(100);
        Assert.True(env.Instance.IsIdle);

        Assert.True(env.Updater.StartUpdate("main", new LocalUpdateRequest(), "tester").Success);
        var done = await WaitDoneAsync(env.Updater);
        Assert.True(done.State == "succeeded", done.Error + "\n" + ConsoleText(env));
        Assert.Equal("idle", done.TriggerReason);
        Assert.True(env.Instance.Running);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(env.Live, "version.txt")).Trim());
    }

    [Fact]
    public async Task RunningServer_CancelPending_KeepsOldVersionRunning()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var env = Create();
        await StartGameAsync(env);
        int oldPid = env.Instance.GetStatus().ProcessId!.Value;

        Assert.True(env.Updater.StartUpdate("main", new LocalUpdateRequest(Mode: "roundEnd"), "tester").Success);
        await WaitAsync(env.Updater, s => s.State == "pending");
        await WaitAsync(env.Updater, s => s.Lines.Any(l => l.Text.Contains("等待当前回合结束")));

        Assert.True(env.Updater.CancelUpdate("main", "tester").Success);
        var done = await WaitDoneAsync(env.Updater);
        Assert.Equal("canceled", done.State);
        Assert.Contains("Server WON'T restart after next round", ConsoleText(env));
        Assert.Equal(oldPid, env.Instance.GetStatus().ProcessId);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(env.Live, "version.txt")).Trim());
        Assert.False(Directory.Exists(ServerUpdateService.StagingPathFor(env.Live)));
        Assert.False(env.Instance.UpdateInProgress);
    }

    [Fact]
    public async Task RunningServer_ApplyNow_BrokenNewVersion_RollsBack()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var env = Create(newVersionBroken: true);
        await StartGameAsync(env);

        Assert.True(env.Updater.StartUpdate("main", new LocalUpdateRequest(Mode: "roundEnd"), "tester").Success);
        await WaitAsync(env.Updater, s => s.State == "pending");
        Assert.True(env.Updater.ApplyNow("main", "tester").Success);

        var done = await WaitDoneAsync(env.Updater);
        Assert.Equal("rolledback", done.State);
        Assert.Equal("forceNow", done.TriggerReason);
        Assert.Contains("回滚", done.Error);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(env.Live, "version.txt")).Trim());
        Assert.True(Directory.Exists(env.Live + ServerUpdateService.FailedSuffix));

        for (int i = 0; i < 50 && env.Instance.GetStatus().HeartbeatStatus != "active"; i++)
            await Task.Delay(100);
        Assert.True(env.Instance.Running);
    }

    [Fact]
    public async Task RunningServer_SameBuild_NoRestart()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var env = Create(newBuild: "100");
        await StartGameAsync(env);
        int oldPid = env.Instance.GetStatus().ProcessId!.Value;

        Assert.True(env.Updater.StartUpdate("main", new LocalUpdateRequest(Mode: "roundEnd"), "tester").Success);
        var done = await WaitDoneAsync(env.Updater);
        Assert.Equal("succeeded", done.State);
        Assert.Contains("已是最新版本", done.Message);
        Assert.Equal(oldPid, env.Instance.GetStatus().ProcessId);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(env.Live, "version.txt")).Trim());
    }

    [Fact]
    public async Task CrashAutoRestart_WhileStartBlocked_IsDeferredNotSpawned()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var env = Create(o => o.Servers[0].RestartOnCrash = true);
        await StartGameAsync(env);

        env.Instance.SetStartBlocked(true);              // 模拟「正在切换目录」
        await env.Instance.SendConsoleAsync("crash", CancellationToken.None);
        for (int i = 0; i < 60 && !ConsoleText(env).Contains("自动拉起推迟"); i++)
            await Task.Delay(100);

        Assert.Contains("自动拉起推迟到更新完成后", ConsoleText(env));
        Assert.False(env.Instance.Running);
        env.Instance.SetStartBlocked(false);
        Assert.True(env.Instance.ConsumeSuppressedAutoStart());
        Assert.False(env.Instance.ConsumeSuppressedAutoStart());
    }
}
