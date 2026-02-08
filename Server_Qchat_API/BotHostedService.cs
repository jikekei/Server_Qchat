using EleCho.GoCqHttpSdk;
using EleCho.GoCqHttpSdk.Message;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace Server_Qchat_API;

public sealed class BotHostedService : BackgroundService
{
    private readonly IOptions<AppOptions> _options;
    private readonly ScplistApiClient _api;
    private readonly ILogger<BotHostedService> _logger;

    public BotHostedService(IOptions<AppOptions> options, ScplistApiClient api, ILogger<BotHostedService> logger)
    {
        _options = options;
        _api = api;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = _options.Value;

        Console.WriteLine();
        Console.WriteLine("开发者：yiming 3037240065 liseximt@outlook.com");
        Console.WriteLine("[DIRSystem]: 使用前请确保打开正向WebSocket 6700 端口，服务器端开放对应 API 端口");
        Console.WriteLine();

        var servers = ReadServersInteractive(opt.DefaultServers);

        // Keep the process alive even if ws://localhost:6700 is not ready yet.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting ws: {WsBaseUri}", opt.WsBaseUri);

                var session = new CqWsSession(new CqWsSessionOptions
                {
                    BaseUri = new Uri(opt.WsBaseUri),
                });

                session.UseGroupMessage(async context =>
                {
                    try
                    {
                        var text = context.Message?.Text ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(text))
                            return;

                        if (!text.Contains(opt.CommandKeyword, StringComparison.OrdinalIgnoreCase))
                            return;

                        var tasks = servers.Select(id => _api.GetPlayersAsync(id, stoppingToken)).ToArray();
                        var results = await Task.WhenAll(tasks);

                        var sb = new StringBuilder();
                        for (var i = 0; i < servers.Count; i++)
                        {
                            if (i > 0) sb.AppendLine();
                            sb.Append("当前 - ").Append(i + 1).Append("服").AppendLine();
                            sb.Append("在线人数：").Append(results[i]?.ToString() ?? "0");
                        }

                        await session.SendGroupMessageAsync(context.GroupId, new CqMessage(sb.ToString()));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error handling group message");
                    }
                });

                // EleCho SDK doesn't expose CancellationToken on RunAsync. We rely on process shutdown.
                await session.RunAsync();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "WebSocket connect/run failed; retrying in 3s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // shutting down
                }
            }
        }
    }

    private static List<int> ReadServersInteractive(int[] defaults)
    {
        Console.WriteLine("请输入服务器查询id(多个用'*'或','隔开，如:31140*31150*31160):");
        var input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
            return defaults?.Length > 0 ? defaults.ToList() : new List<int>();

        var tokens = input.Split(new[] { '*', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>();

        foreach (var t in tokens)
        {
            if (int.TryParse(t, out var id))
                list.Add(id);
        }

        return list.Count > 0 ? list : (defaults?.Length > 0 ? defaults.ToList() : new List<int>());
    }
}

