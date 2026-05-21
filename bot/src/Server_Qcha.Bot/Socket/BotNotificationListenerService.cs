using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Bot;
using Server.Qcat.Configuration;
using EleCho.GoCqHttpSdk;
using EleCho.GoCqHttpSdk.Message;

namespace Server.Qcat.Socket;

public sealed class BotNotificationListenerService : BackgroundService
{
    private readonly SocketServerOptions _socketOpts;
    private readonly BotOptions _botOpts;
    private readonly BotSessionAccessor _sessionAccessor;
    private readonly ILogger<BotNotificationListenerService> _log;
    private TcpListener? _listener;

    public BotNotificationListenerService(
        IOptions<SocketServerOptions> socketOpts,
        IOptions<BotOptions> botOpts,
        BotSessionAccessor sessionAccessor,
        ILogger<BotNotificationListenerService> log)
    {
        _socketOpts = socketOpts.Value;
        _botOpts = botOpts.Value;
        _sessionAccessor = sessionAccessor;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_socketOpts.NotificationPort <= 0)
        {
            _log.LogInformation("NotificationPort is not configured. Proactive notification service is disabled.");
            return;
        }

        if (!IPAddress.TryParse(_socketOpts.NotificationHost, out var ip))
        {
            ip = IPAddress.Any;
        }

        _log.LogInformation("Starting notification listener on {IP}:{Port}", ip, _socketOpts.NotificationPort);
        _listener = new TcpListener(ip, _socketOpts.NotificationPort);
        _listener.Start();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Notification listener encountered an error.");
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                
                var ms = new MemoryStream();
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }

                string payload = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                if (string.IsNullOrWhiteSpace(payload))
                    return;

                if (!string.IsNullOrEmpty(_socketOpts.AuthToken))
                {
                    int splitIdx = payload.IndexOf("||", StringComparison.Ordinal);
                    if (splitIdx < 0)
                    {
                        _log.LogWarning("Unauthorized notification received (No token).");
                        return;
                    }

                    string reqToken = payload.Substring(0, splitIdx);
                    if (reqToken != _socketOpts.AuthToken)
                    {
                        _log.LogWarning("Unauthorized notification received (Invalid token).");
                        return;
                    }

                    payload = payload.Substring(splitIdx + 2);
                }

                _log.LogInformation("Received proactive notification: {Payload}", payload);

                long targetGroupId = _botOpts.AcTargetGroupId;
                if (targetGroupId <= 0 && _botOpts.NotifyGroupIds != null && _botOpts.NotifyGroupIds.Length > 0)
                {
                    targetGroupId = _botOpts.NotifyGroupIds[0];
                }

                if (targetGroupId <= 0)
                {
                    _log.LogWarning("No target QQ group configured for AC notifications. Dropping message.");
                    return;
                }

                var session = _sessionAccessor.Session;
                if (session == null)
                {
                    _log.LogWarning("QQ Bot session not active. Drop message.");
                    return;
                }

                await session.SendGroupMessageAsync(targetGroupId, new CqMessage(payload));
                _log.LogInformation("Proactive notification pushed to group {GroupId}", targetGroupId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error handling client connection in NotificationListener");
            }
        }
    }
}
