using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin;
using Server.Qcat.LocalAdmin.Models;
using Xunit;

namespace Server_Qcha.Bot.Tests;

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

    private static (LocalAdminManager Manager, ServerUpdateService Updater, string Root) Create(Action<LocalAdminOptions>? configure = null, string? installDir = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "qcha-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string serverDir = installDir ?? Path.Combine(root, "scpsl");
        Directory.CreateDirectory(serverDir);

        var options = new LocalAdminOptions
        {
            Enabled = true,
            Mode = "Embedded",
            WriteLogFiles = false,
            SteamCmdPath = Path.Combine(root, "missing", OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh"),
            Servers =
            {
                new LocalServerDefinition
                {
                    Id = "main",
                    Name = "主服",
                    ExecutablePath = Path.Combine(serverDir, OperatingSystem.IsWindows() ? "SCPSL.exe" : "SCPSL.x86_64"),
                    GamePort = 7777,
                },
            },
        };
        configure?.Invoke(options);

        var opts = Options.Create(options);
        var manager = new LocalAdminManager(opts, NullLoggerFactory.Instance, NullLogger<LocalAdminManager>.Instance, new TestHostEnvironment(root));
        manager.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var updater = new ServerUpdateService(manager, opts, NullLogger<ServerUpdateService>.Instance);
        return (manager, updater, root);
    }

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
    public async Task StartUpdate_MissingSteamCmd_FailsWithClearMessage()
    {
        var (manager, updater, root) = Create();
        try
        {
            var result = updater.StartUpdate("main", new LocalUpdateRequest(), "tester");
            Assert.False(result.Success);
            Assert.Contains("SteamCMD", result.Error);
            Assert.NotNull(result.Status);
            Assert.False(result.Status!.SteamCmdFound);
            Assert.False(manager.Find("main")!.UpdateInProgress);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartUpdate_UnknownServer_Fails()
    {
        var (manager, updater, root) = Create();
        try
        {
            var result = updater.StartUpdate("nope", null, null);
            Assert.False(result.Success);
            Assert.Contains("nope", result.Error);
            Assert.Null(updater.GetStatus("nope"));
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartUpdate_InvalidBranch_Rejected()
    {
        var (manager, updater, root) = Create(o => o.SteamCmdPath = "");
        try
        {
            var def = manager.Find("main")!.Definition;
            Assert.Null(updater.ResolveBranch("public", def, out var e1));
            Assert.Null(e1);
            Assert.Equal("beta", updater.ResolveBranch("beta", def, out _));
            updater.ResolveBranch("bad branch; rm -rf", def, out var e2);
            Assert.NotNull(e2);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StartUpdate_WithFakeSteamCmd_RunsAndSucceeds()
    {
        if (OperatingSystem.IsWindows())
            return; // 用 bash 脚本模拟 steamcmd.sh，仅在类 Unix 上运行

        string fakeDir = Path.Combine(Path.GetTempPath(), "qcha-fake-steamcmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fakeDir);
        string script = Path.Combine(fakeDir, "steamcmd.sh");
        string argsFile = Path.Combine(fakeDir, "args.txt");
        File.WriteAllText(script,
            "#!/bin/bash\n" +
            $"printf '%s\\n' \"$@\" > '{argsFile}'\n" +
            "echo 'Redirecting stderr to log'\n" +
            "printf '\\033[0m Update state (0x61) downloading, progress: 42.00 (1 / 2)\\n'\n" +
            "printf \"\\033[0mSuccess! App '996560' fully installed.\\n\"\n" +
            "exit 0\n");

        var (manager, updater, root) = Create(o =>
        {
            o.SteamCmdPath = fakeDir;          // 目录形式也应能解析
            o.UpdateBetaBranch = "public";
        });
        try
        {
            var start = updater.StartUpdate("main", new LocalUpdateRequest(BetaBranch: "beta"), "tester");
            Assert.True(start.Success, start.Error);

            // 同一台服务器不允许并发更新
            var second = updater.StartUpdate("main", new LocalUpdateRequest(), "tester");
            if (updater.GetStatus("main")!.Active)
                Assert.False(second.Success);

            LocalUpdateStatus? status = null;
            for (int i = 0; i < 100; i++)
            {
                status = updater.GetStatus("main");
                if (status is { Active: false })
                    break;
                await Task.Delay(100);
            }

            Assert.NotNull(status);
            Assert.Equal("succeeded", status!.State);
            Assert.Equal(0, status.ExitCode);
            Assert.Contains(status.Lines, l => l.Text == "[SteamCMD] Success! App '996560' fully installed.");
            Assert.Equal(42.0, status.ProgressPercent);
            Assert.Equal("downloading", status.ProgressPhase);
            Assert.False(manager.Find("main")!.UpdateInProgress);

            string[] passed = File.ReadAllLines(argsFile);
            Assert.Equal("+force_install_dir", passed[0]);
            Assert.Equal("-beta", passed[6]);
            Assert.Equal("beta", passed[7]);
            Assert.Equal("+quit", passed[^1]);

            // 更新输出同时写进了该服务器的控制台缓冲
            var lines = manager.Find("main")!.ReadLines(0, 500, out _, out _, out _);
            Assert.Contains(lines, l => l.Text.Contains("[更新]") && l.Text.Contains("SteamCMD"));
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            Directory.Delete(root, true);
            Directory.Delete(fakeDir, true);
        }
    }

    [Fact]
    public async Task StartUpdate_SteamCmdReportsError_Fails()
    {
        if (OperatingSystem.IsWindows())
            return;

        string fakeDir = Path.Combine(Path.GetTempPath(), "qcha-fake-steamcmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fakeDir);
        File.WriteAllText(Path.Combine(fakeDir, "steamcmd.sh"),
            "#!/bin/bash\necho \"ERROR! Failed to install app '996560' (No subscription)\"\nexit 8\n");

        var (manager, updater, root) = Create(o => o.SteamCmdPath = Path.Combine(fakeDir, "steamcmd.sh"));
        try
        {
            Assert.True(updater.StartUpdate("main", null, null).Success);

            LocalUpdateStatus? status = null;
            for (int i = 0; i < 100; i++)
            {
                status = updater.GetStatus("main");
                if (status is { Active: false })
                    break;
                await Task.Delay(100);
            }

            Assert.Equal("failed", status!.State);
            Assert.Equal(8, status.ExitCode);
            Assert.Contains("No subscription", status.Error);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            Directory.Delete(root, true);
            Directory.Delete(fakeDir, true);
        }
    }
}
