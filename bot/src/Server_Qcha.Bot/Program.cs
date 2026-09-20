using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Qcat.Bot;
using Server.Qcat.Configuration;
using Server.Qcat.Data;
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
builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<GoCqHttpOptions>(builder.Configuration.GetSection("GoCqHttp"));
builder.Services.Configure<SocketServerOptions>(builder.Configuration.GetSection("SocketServer"));
builder.Services.Configure<MySqlOptions>(builder.Configuration.GetSection("MySql"));
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection("Bot"));
builder.Services.Configure<WebPanelOptions>(builder.Configuration.GetSection("WebPanel"));

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
// 命令下发网关：当前走插件 TCP 通道，后期可替换为 LocalAdmin 网关实现
builder.Services.AddSingleton<IServerCommandGateway, PluginCommandGateway>();

builder.Services.AddHostedService<GoCqHttpBotService>();
builder.Services.AddHostedService<BotNotificationListenerService>();

var app = builder.Build();

if (webOptions.Enabled)
{
    // 建表 + 确保内置管理员存在（按配置随机重置密码并输出到日志）
    app.Services.GetRequiredService<PanelDatabase>().EnsureCreated();
    await app.Services.GetRequiredService<PanelAuthService>().InitializeAsync();

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapPanelApi();
}
else
{
    app.Logger.LogInformation("Web 面板已按配置禁用（WebPanel:Enabled = false）");
}

await app.RunAsync();
