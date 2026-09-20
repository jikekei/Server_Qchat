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

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

// appsettings.json 由 Web SDK 默认加载；
// 这里补充本地覆盖文件，并把环境变量放到其后，保证 env var 优先级最高
// （例如可用 MySql__ConnectionString 覆盖数据库连接串）。
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// 面板可调的日志级别：独立成文件，便于运行时被面板改写；
// 开 reloadOnChange 后，文件一变即触发配置重载，
// 框架会自动重绑 LoggerFilterOptions —— 因此改级别无需重启（详见 LogLevelStore）。
// 注意必须放在环境变量之前，保证 Logging__LogLevel__* 仍能覆盖它。
builder.Configuration.AddJsonFile(LogLevelStore.FileName, optional: true, reloadOnChange: true);

// 面板可调的 QQ 机器人设置：独立成文件，运行时热重载
builder.Configuration.AddJsonFile(BotSettingsStore.FileName, optional: true, reloadOnChange: true);

builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<GoCqHttpOptions>(builder.Configuration.GetSection("GoCqHttp"));
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
builder.Services.AddSingleton<BotSessionAccessor>();
builder.Services.AddSingleton<ServerRegistry>();
builder.Services.AddSingleton<SocketCommandClient>();
builder.Services.AddSingleton<PlayerRepository>();
builder.Services.AddSingleton<CommandRouter>();

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

// ---- LocalAdmin 能力：由机器人进程托管游戏服务端进程（默认关闭） ----
// 同一实例既作为托管服务启动/停止，也供面板接口查询与控制
builder.Services.AddSingleton<LocalAdminManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LocalAdminManager>());

// 机器人服务：注册为单例以便面板 API 查询状态与触发重连
builder.Services.AddSingleton<GoCqHttpBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GoCqHttpBotService>());
builder.Services.AddHostedService<BotNotificationListenerService>();

// 玩家历史采样与统计服务（为总览仪表盘和折线图提供数据）
builder.Services.AddSingleton<PlayerHistoryTracker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PlayerHistoryTracker>());

var app = builder.Build();

// 注册退出拦截与多重确认保护（防止误关程序导致所有游戏服掉线）
ConsoleExitHandler.Initialize(app.Services.GetRequiredService<IHostApplicationLifetime>(), app.Logger);

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

await app.RunAsync();
