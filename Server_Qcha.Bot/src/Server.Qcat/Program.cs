using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Qcat.Bot;
using Server.Qcat.Configuration;
using Server.Qcat.Data;
using Server.Qcat.Socket;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<GoCqHttpOptions>(builder.Configuration.GetSection("GoCqHttp"));
builder.Services.Configure<SocketServerOptions>(builder.Configuration.GetSection("SocketServer"));
builder.Services.Configure<MySqlOptions>(builder.Configuration.GetSection("MySql"));
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection("Bot"));

builder.Services.AddSingleton<BotSessionAccessor>();
builder.Services.AddSingleton<SocketCommandClient>();
builder.Services.AddSingleton<PlayerRepository>();
builder.Services.AddSingleton<CommandRouter>();

builder.Services.AddHostedService<GoCqHttpBotService>();

var host = builder.Build();
await host.RunAsync();
