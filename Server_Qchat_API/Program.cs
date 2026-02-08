using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Server_Qchat_API;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "SERVER_QCHAT_API_");

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection("App"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.WsBaseUri), "App:WsBaseUri is required")
    .ValidateOnStart();

builder.Services.AddHttpClient<ScplistApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.scplist.kr/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHostedService<BotHostedService>();

await builder.Build().RunAsync();
