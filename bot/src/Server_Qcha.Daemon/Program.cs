using System.Diagnostics;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin;
using Server.Qcat.LocalAdmin.Models;

AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n[Daemon 致命错误] 守护进程遇到未处理异常：{e.ExceptionObject}");
    Console.ResetColor();
    Console.WriteLine("\n按任意键退出...");
    if (!Console.IsInputRedirected)
    {
        try { Console.ReadKey(); } catch { }
    }
};

const string MutexName = @"Local\Server_Qcha_Daemon_SingleInstance";
Mutex? singleInstanceMutex = null;
try
{
    singleInstanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
    if (!isNewInstance)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("【提示】检测到 Server_Qcha.Daemon 已经在后台运行中，无需重复启动。本实例自动退出。");
        Console.ResetColor();
        return;
    }
}
catch
{
    // 忽略非特权环境互斥体创建失败
}

var builder = WebApplication.CreateBuilder(args);

// 确保 data/ 目录存在并完成迁移
string contentRoot = builder.Environment.ContentRootPath;
DataDirectoryManager.EnsureDataDirectoryAndMigrate(contentRoot);

// 配置 LocalAdmin
builder.Services.Configure<LocalAdminOptions>(builder.Configuration.GetSection("LocalAdmin"));
var localAdminConfig = builder.Configuration.GetSection("LocalAdmin").Get<LocalAdminOptions>() ?? new LocalAdminOptions();

string listenUri = string.IsNullOrWhiteSpace(localAdminConfig.DaemonUri)
    ? "http://127.0.0.1:10090"
    : localAdminConfig.DaemonUri;

builder.WebHost.UseUrls(listenUri);

// 注册 LocalAdmin 管理器与内嵌提供方
builder.Services.AddSingleton<LocalAdminManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LocalAdminManager>());
builder.Services.AddSingleton<ILocalAdminProvider, EmbeddedLocalAdminProvider>();

var app = builder.Build();

// 守护进程认证中间件（校验 X-Daemon-Token）
app.Use(async (context, next) =>
{
    var options = context.RequestServices.GetRequiredService<IOptions<LocalAdminOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.DaemonToken))
    {
        // 允许直接访问根路径、ping 或 status
        if (context.Request.Path != "/" && !context.Request.Path.StartsWithSegments("/api/daemon/ping") && !context.Request.Path.StartsWithSegments("/api/daemon/status"))
        {
            if (!context.Request.Headers.TryGetValue("X-Daemon-Token", out var token) ||
                !string.Equals(token.ToString(), options.DaemonToken, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "未授权：Daemon Token 不匹配" });
                return;
            }
        }
    }
    await next();
});

// Daemon API 路由
var api = app.MapGroup("/api/daemon");

api.MapGet("/ping", (LocalAdminManager manager) =>
{
    int running = manager.Instances.Count(i => i.Running);
    return Results.Json(new DaemonPingResult("ok", "2.0.0", Environment.ProcessId, manager.Instances.Count, running));
});

api.MapGet("/status", (LocalAdminManager manager, IOptions<LocalAdminOptions> options) =>
{
    var proc = Process.GetCurrentProcess();
    proc.Refresh();
    double wsMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 1);
    double privMb = Math.Round(proc.PrivateMemorySize64 / (1024.0 * 1024.0), 1);
    double uptime = Math.Round((DateTime.UtcNow - proc.StartTime.ToUniversalTime()).TotalSeconds, 0);
    int running = manager.Instances.Count(i => i.Running);

    return Results.Json(new DaemonStatusResult(
        Online: true,
        Status: "running",
        Version: "2.0.0",
        Pid: Environment.ProcessId,
        StartTime: proc.StartTime,
        UptimeSeconds: uptime,
        MemoryWorkingSetMb: wsMb,
        MemoryPrivateMb: privMb,
        ThreadCount: proc.Threads.Count,
        ServerCount: manager.Instances.Count,
        RunningServerCount: running,
        ListenUri: options.Value.DaemonUri,
        Message: "守护进程正常运行中"));
});

api.MapGet("/servers", async (ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.ListServersAsync(ct);
    return Results.Json(res);
});

api.MapGet("/servers/{id}", async (string id, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var status = await provider.GetStatusAsync(id, ct);
    return status is not null ? Results.Json(status) : Results.NotFound(new { error = $"服务器不存在：{id}" });
});

api.MapGet("/servers/{id}/poll", async (string id, long? after, int? limit, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var poll = await provider.PollAsync(id, after, limit, ct);
    return poll is not null ? Results.Json(poll) : Results.NotFound(new { error = $"服务器不存在：{id}" });
});

api.MapPost("/servers", async (LocalServerSaveRequest req, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.CreateServerAsync(req, ct);
    return Results.Json(res, statusCode: res.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
});

api.MapPut("/servers/{id}", async (string id, LocalServerSaveRequest req, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.UpdateServerAsync(id, req, ct);
    return Results.Json(res, statusCode: res.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
});

api.MapDelete("/servers/{id}", async (string id, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.DeleteServerAsync(id, ct);
    return Results.Json(new { success = res.Success, response = res.Message, error = res.Error },
        statusCode: res.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
});

api.MapPost("/servers/{id}/start", async (string id, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.StartAsync(id, ct);
    return Results.Json(new { success = res.Success, response = res.Message, error = res.Error });
});

api.MapPost("/servers/{id}/stop", async (string id, LocalStopRequest? req, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.StopAsync(id, req?.Force ?? false, ct);
    return Results.Json(new { success = res.Success, response = res.Message, error = res.Error });
});

api.MapPost("/servers/{id}/restart", async (string id, LocalRestartRequest? req, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.RestartAsync(id, req?.Force ?? false, ct);
    return Results.Json(new { success = res.Success, response = res.Message, error = res.Error });
});

api.MapPost("/servers/{id}/console", async (string id, LocalConsoleRequest req, ILocalAdminProvider provider, CancellationToken ct) =>
{
    string command = (req.Command ?? string.Empty).Trim();
    if (command.Length == 0)
        return Results.Json(new { error = "命令不能为空" }, statusCode: StatusCodes.Status400BadRequest);

    var res = await provider.SendConsoleAsync(id, command, ct);
    return Results.Json(new { success = res.Success, response = res.Message, error = res.Error });
});

api.MapPost("/servers/{id}/console/clear", async (string id, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.ClearConsoleAsync(id, ct);
    return Results.Json(new { success = res.Success, response = res.Message, error = res.Error });
});

api.MapPost("/servers/{id}/heartbeat", async (string id, LocalHeartbeatRequest req, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.SetHeartbeatAsync(id, req.Enabled ?? true, ct);
    return Results.Json(new { success = res.Success, response = res.Message, error = res.Error });
});

api.MapPost("/servers/{id}/console-level", async (string id, LocalConsoleLevelRequest req, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.SetConsoleLevelAsync(id, req.Level, ct);
    return Results.Json(res);
});

api.MapPost("/servers/{id}/cancel-restart", async (string id, ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.CancelRestartAsync(id, ct);
    return Results.Json(new { success = res.Success, response = res.Message, error = res.Error });
});

api.MapGet("/config", async (ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.GetConfigAsync(ct);
    return Results.Json(res);
});

api.MapPost("/config/resave", async (ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.ResaveConfigAsync(ct);
    return Results.Json(new { success = res.Success, response = res.Message, error = res.Error });
});

api.MapGet("/executables", async (ILocalAdminProvider provider, CancellationToken ct) =>
{
    var res = await provider.FindExecutablesAsync(ct);
    return Results.Json(res);
});

api.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
{
    Task.Run(async () =>
    {
        await Task.Delay(500);
        lifetime.StopApplication();
    });
    return Results.Json(new { success = true, response = "守护进程正在关闭..." });
});

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("================================================================================");
Console.WriteLine("  Qcha LocalAdmin 独立守护进程 (Server_Qcha.Daemon)");
Console.WriteLine($"  进程 PID: {Environment.ProcessId}");
Console.WriteLine($"  监听地址: {listenUri}");
Console.WriteLine("  提示：本守护进程负责托管 SCPSL 游戏服并维持控制台长连接。");
Console.WriteLine("  当关闭或重启 Server_Qcha.Bot 面板时，游戏服不受任何影响，玩家不会断开！");
Console.WriteLine("================================================================================");
Console.ResetColor();

await app.RunAsync();
