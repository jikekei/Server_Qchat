using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Qcat;
using Server.Qcat.Bot;
using Server.Qcat.Configuration;
using Server.Qcat.Data;
using Server.Qcat.LocalAdmin;
using Server.Qcat.Logging;
using Server.Qcat.Services;
using Server.Qcat.Socket;
using Server.Qcat.Web;

AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n[Bot 致命错误] 遇到未处理异常：{e.ExceptionObject}");
    Console.ResetColor();
    Console.WriteLine("\n按任意键退出...");
    if (!Console.IsInputRedirected)
    {
        try { Console.ReadKey(); } catch { }
    }
};

const string BotMutexName = @"Local\Server_Qcha_Bot_SingleInstance";
Mutex? botMutex = null;
try
{
    botMutex = new Mutex(true, BotMutexName, out bool isNewBot);
    if (!isNewBot)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("===================================================================");
        Console.WriteLine("【提示】检测到 Server_Qcha.Bot 已经在后台运行中，请勿重复启动！");
        Console.WriteLine("若需使用 Web 管理面板，请直接打开浏览器访问：http://127.0.0.1:8080");
        Console.WriteLine("===================================================================");
        Console.ResetColor();
        Console.WriteLine("\n按任意键退出...");
        if (!Console.IsInputRedirected)
        {
            try { Console.ReadKey(); } catch { }
        }
        return;
    }
}
catch
{
    // 忽略互斥体创建异常
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

// 确保独立本地数据目录 data/ 存在，并自动将根目录遗留旧文件无缝迁移至 data/
DataDirectoryManager.EnsureDataDirectoryAndMigrate(
    builder.Environment.ContentRootPath,
    msg => Console.WriteLine($"[DataMigrator] {msg}"));

// appsettings.json 由 Web SDK 默认加载；
// 这里补充本地覆盖文件，并把环境变量放到其后，保证 env var 优先级最高
// （例如可用 MySql__ConnectionString 覆盖数据库连接串）。
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// 面板可调的日志级别：独立成文件存放在 data/ 目录下，便于运行时被面板改写；
// 开 reloadOnChange 后，文件一变即触发配置重载，
// 框架会自动重绑 LoggerFilterOptions —— 因此改级别无需重启（详见 LogLevelStore）。
// 注意必须放在环境变量之前，保证 Logging__LogLevel__* 仍能覆盖它。
builder.Configuration.AddJsonFile(Path.Combine(DataDirectoryManager.DataDirName, LogLevelStore.FileName), optional: true, reloadOnChange: true);

// 面板可调的 QQ 机器人设置：独立成文件存放在 data/ 目录下，运行时热重载
builder.Configuration.AddJsonFile(Path.Combine(DataDirectoryManager.DataDirName, BotSettingsStore.FileName), optional: true, reloadOnChange: true);

builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<GoCqHttpOptions>(builder.Configuration.GetSection("GoCqHttp"));
builder.Services.Configure<OfficialQqOptions>(builder.Configuration.GetSection("OfficialQq"));
builder.Services.Configure<SocketServerOptions>(builder.Configuration.GetSection("SocketServer"));
builder.Services.Configure<MySqlOptions>(builder.Configuration.GetSection("MySql"));
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection("Bot"));
builder.Services.Configure<WebPanelOptions>(builder.Configuration.GetSection("WebPanel"));
builder.Services.Configure<LocalAdminOptions>(builder.Configuration.GetSection("LocalAdmin"));

var webOptions = builder.Configuration.GetSection("WebPanel").Get<WebPanelOptions>() ?? new WebPanelOptions();
if (webOptions.Enabled)
{
    string host = string.IsNullOrWhiteSpace(webOptions.Host) ? "0.0.0.0" : webOptions.Host;
    int port = webOptions.Port is > 0 and <= 65535 ? webOptions.Port : 8080;
    builder.WebHost.UseUrls($"http://{host}:{port}");
}
else
{
    // 面板关闭时仍承载 Host，但仅绑定回环随机端口，不占用固定端口、不对外暴露
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

// ---- 机器人既有服务 ----
// BotClientAccessor 持有「当前生效的接入实现」，供通知推送与面板使用，
// 上层不再直接依赖具体的 OneBot 会话对象。
builder.Services.AddSingleton<BotClientAccessor>();
builder.Services.AddSingleton<ServerRegistry>();
builder.Services.AddSingleton<SocketCommandClient>();
builder.Services.AddSingleton<PlayerRepository>();
builder.Services.AddSingleton<CommandRouter>();
builder.Services.AddSingleton<BotBindingStore>();

// ---- QQ 官方 Bot API 接入所需 ----
builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
builder.Services.AddSingleton<OfficialQqTokenProvider>();

// ---- Web 面板服务 ----
builder.Services.AddSingleton<PanelDatabase>();
builder.Services.AddSingleton<PanelAuthService>();
builder.Services.AddSingleton<GameDbRepository>();
// 日志级别：读写 ContentRoot 下的 logging-level.json，改动即时生效
builder.Services.AddSingleton(sp => new LogLevelStore(
    sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<LogLevelStore>>()));
// 机器人设置：读写 ContentRoot 下的 bot-settings.json，改动即时生效
builder.Services.AddSingleton(sp => new BotSettingsStore(
    sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<BotSettingsStore>>()));
// 命令下发网关：当前走插件 TCP 通道，后期可替换为 LocalAdmin 网关实现
builder.Services.AddSingleton<IServerCommandGateway, PluginCommandGateway>();

// ---- LocalAdmin 能力：支持 Daemon 独立守护架构（默认）与 Embedded 内嵌模式 ----
var localAdminOptions = builder.Configuration.GetSection("LocalAdmin").Get<LocalAdminOptions>() ?? new LocalAdminOptions();
bool isDaemonMode = !string.Equals(localAdminOptions.Mode, "Embedded", StringComparison.OrdinalIgnoreCase);

if (isDaemonMode)
{
    // 独立守护模式：面板与游戏服解耦，Bot 重启或升级完全不影响 SCPSL 运行
    builder.Services.AddHttpClient<DaemonLocalAdminProvider>();
    builder.Services.AddSingleton<ILocalAdminProvider>(sp => sp.GetRequiredService<DaemonLocalAdminProvider>());
}
else
{
    // 内嵌模式：单进程运行，LocalAdmin 随 Bot 进程退出
    builder.Services.AddSingleton<LocalAdminManager>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<LocalAdminManager>());
    builder.Services.AddSingleton<ILocalAdminProvider, EmbeddedLocalAdminProvider>();
}

// 机器人接入：NapCat 与 QQ 官方两套实现都注册为单例，但不由宿主直接启动，
// 而是交给 BotHostService 依据 Bot:Mode 启停，从而支持运行时热切换。
builder.Services.AddSingleton<NapCatBotClient>();
builder.Services.AddSingleton<OfficialQqBotClient>();

// 注意：AddHostedService<T>() 只会把 T 注册为 IHostedService，并不会注册 T 本身。
// 而 BotHostService 还要被 Minimal API（面板接口）以具体类型注入，
// 若缺少下面这行 AddSingleton，参数绑定器会把它当成请求体推断，
// 进而在首次匹配路由时抛 InvalidOperationException —— 表现为面板所有接口全部 500。
builder.Services.AddSingleton<BotHostService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BotHostService>());

builder.Services.AddHostedService<BotNotificationListenerService>();

// 玩家历史采样与统计服务（为总览仪表盘和折线图提供数据）
builder.Services.AddSingleton<PlayerHistoryTracker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PlayerHistoryTracker>());
builder.Services.AddSingleton<ServerStatusMonitorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ServerStatusMonitorService>());

var app = builder.Build();

// 注册退出拦截保护（Daemon 模式安全退出；Embedded 模式二次确认防游戏服掉线）
ConsoleExitHandler.Initialize(app.Services.GetRequiredService<IHostApplicationLifetime>(), app.Logger, isDaemonMode);

if (isDaemonMode && localAdminOptions.Enabled && localAdminOptions.AutoStartDaemon)
{
    // 独立守护模式：若未检测到运行中的 Daemon，自动在后台拉起 Server_Qcha.Daemon.exe
    // 确保无论通过脚本还是直接双击 Server_Qcha.Bot.exe，均能自动享受守护架构
    try
    {
        var daemonProvider = app.Services.GetRequiredService<DaemonLocalAdminProvider>();
        daemonProvider.EnsureDaemonRunningAtStartup();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "检查/拉起 LocalAdmin 守护进程异常：{Message}", ex.Message);
    }
}

if (webOptions.Enabled)
{
    // 建表 + 确保内置管理员存在（按配置随机重置密码并输出到日志）
    app.Services.GetRequiredService<PanelDatabase>().EnsureCreated();
    await app.Services.GetRequiredService<PanelAuthService>().InitializeAsync();

    // 注入安全响应头（防御 MIME 嗅探、点击劫持等）
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        await next();
    });

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapPanelApi();
    app.MapLocalAdminApi();
    app.MapLoggingApi();
    app.MapBotApi();
    app.MapDatabaseApi();
}
else
{
    app.Logger.LogInformation("Web 面板已按配置禁用（WebPanel:Enabled = false）");
}

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n[Bot 运行错误] {ex.Message}\n{ex}");
    Console.ResetColor();
    Console.WriteLine("\n按任意键退出...");
    if (!Console.IsInputRedirected)
    {
        try { Console.ReadKey(); } catch { }
    }
}
