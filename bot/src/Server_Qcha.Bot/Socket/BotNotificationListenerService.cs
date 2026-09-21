using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Bot;
using Server.Qcat.Configuration;

namespace Server.Qcat.Socket;

public class NotificationEnvelope
{
    public string Type { get; set; } = "";
    public NotificationData Data { get; set; } = new();
}

public class NotificationData
{
    public string Name { get; set; } = "";
    public string ConnectHost { get; set; } = "";
    public int Port { get; set; }
    public int GamePort { get; set; }
    public int SortOrder { get; set; }
    public string Message { get; set; } = "";
}

public sealed class BotNotificationListenerService : BackgroundService
{
    private readonly SocketServerOptions _socketOpts;
    private readonly IOptionsMonitor<BotOptions> _botOptsMonitor;
    private readonly IOptionsMonitor<OfficialQqOptions> _officialOptsMonitor;
    private readonly BotClientAccessor _botAccessor;
    private readonly OfficialQqBotClient _officialClient;
    private readonly ServerRegistry _registry;
    private readonly ILogger<BotNotificationListenerService> _log;
    private TcpListener? _listener;

    public BotNotificationListenerService(
        IOptions<SocketServerOptions> socketOpts,
        IOptionsMonitor<BotOptions> botOptsMonitor,
        IOptionsMonitor<OfficialQqOptions> officialOptsMonitor,
        BotClientAccessor botAccessor,
        OfficialQqBotClient officialClient,
        ServerRegistry registry,
        ILogger<BotNotificationListenerService> log)
    {
        _socketOpts = socketOpts.Value;
        _botOptsMonitor = botOptsMonitor;
        _officialOptsMonitor = officialOptsMonitor;
        _botAccessor = botAccessor;
        _officialClient = officialClient;
        _registry = registry;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_socketOpts.NotificationPort <= 0)
        {
            _log.LogInformation("NotificationPort 未配置，通知监听服务已禁用");
            return;
        }

        IPAddress ip;
        if (!IPAddress.TryParse(_socketOpts.NotificationHost, out ip!))
        {
            _log.LogWarning("NotificationHost \"{Host}\" 解析失败，回退使用 0.0.0.0", _socketOpts.NotificationHost);
            ip = IPAddress.Any;
        }

        try
        {
            _listener = new TcpListener(ip, _socketOpts.NotificationPort);
            _listener.Start();
            _log.LogInformation("通知监听服务已启动 → {Host}:{Port}", ip, _socketOpts.NotificationPort);
        }
        catch (SocketException ex)
        {
            _log.LogError(ex, "通知监听服务启动失败：端口 {Port} 无法绑定或已被占用（{Message}）。若已有机器人实例在运行，请先将其关闭。", _socketOpts.NotificationPort, ex.Message);
            return;
        }

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
            _log.LogError(ex, "通知监听服务遇到错误已退出");
        }
        finally
        {
            _listener.Stop();
        }
    }

    private const int MaxPayloadBytes = 64 * 1024; // 64 KB 上限，防范内存耗尽攻击
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5); // 5 秒读取超时，防范连接挂起

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ReadTimeout);

                var buffer = new byte[4096];
                var ms = new MemoryStream();
                int read;
                while ((read = await stream.ReadAsync(buffer, timeoutCts.Token)) > 0)
                {
                    if (ms.Length + read > MaxPayloadBytes)
                    {
                        _log.LogWarning("拒绝来自 {RemoteEndpoint} 的通知请求：数据量超出上限（>{Max} 字节）", remoteEndpoint, MaxPayloadBytes);
                        return;
                    }
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
                        _log.LogWarning("拒绝来自 {RemoteEndpoint} 的未授权通知请求：缺少 Token", remoteEndpoint);
                        return;
                    }

                    string reqToken = payload.Substring(0, splitIdx);
                    if (reqToken != _socketOpts.AuthToken)
                    {
                        _log.LogWarning("拒绝来自 {RemoteEndpoint} 的未授权通知请求：Token 不匹配", remoteEndpoint);
                        return;
                    }

                    payload = payload.Substring(splitIdx + 2);
                }

                NotificationEnvelope? envelope;
                try
                {
                    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                    envelope = JsonSerializer.Deserialize<NotificationEnvelope>(payload, options);
                }
                catch (Exception jsonEx)
                {
                    _log.LogWarning(jsonEx, "通知消息 JSON 解析失败 (来自 {RemoteEndpoint}): {Error}，原始数据长度 {Length} 字节", 
                        remoteEndpoint, jsonEx.Message, payload.Length);
                    return;
                }

                if (envelope == null || string.IsNullOrWhiteSpace(envelope.Type))
                {
                    _log.LogWarning("收到未知消息类型 \"\" (来自 {RemoteEndpoint})，已忽略", remoteEndpoint);
                    return;
                }

                string type = envelope.Type.ToLowerInvariant();
                var data = envelope.Data;

                if (type == "register")
                {
                    if (string.IsNullOrEmpty(data.ConnectHost))
                    {
                        _log.LogWarning("register 消息缺少必要字段 (来自 {RemoteEndpoint}): connectHost", remoteEndpoint);
                        return;
                    }
                    if (data.Port <= 0)
                    {
                        _log.LogWarning("register 消息缺少必要字段 (来自 {RemoteEndpoint}): port", remoteEndpoint);
                        return;
                    }

                    var serverInfo = new ServerInfo
                    {
                        Name = data.Name,
                        ConnectHost = data.ConnectHost,
                        Port = data.Port,
                        GamePort = data.GamePort,
                        SortOrder = data.SortOrder,
                        IsStatic = false,
                        IsOnline = true
                    };
                    _registry.Register(serverInfo);
                }
                else if (type == "unregister")
                {
                    if (string.IsNullOrEmpty(data.ConnectHost))
                    {
                        _log.LogWarning("unregister 消息缺少必要字段 (来自 {RemoteEndpoint}): connectHost", remoteEndpoint);
                        return;
                    }
                    if (data.Port <= 0)
                    {
                        _log.LogWarning("unregister 消息缺少必要字段 (来自 {RemoteEndpoint}): port", remoteEndpoint);
                        return;
                    }

                    _registry.Unregister(data.ConnectHost, data.Port);
                }
                else if (type == "heartbeat")
                {
                    if (string.IsNullOrEmpty(data.ConnectHost))
                    {
                        _log.LogWarning("heartbeat 消息缺少必要字段 (来自 {RemoteEndpoint}): connectHost", remoteEndpoint);
                        return;
                    }
                    if (data.Port <= 0)
                    {
                        _log.LogWarning("heartbeat 消息缺少必要字段 (来自 {RemoteEndpoint}): port", remoteEndpoint);
                        return;
                    }

                    var serverInfo = new ServerInfo
                    {
                        Name = data.Name,
                        ConnectHost = data.ConnectHost,
                        Port = data.Port,
                        GamePort = data.GamePort,
                        SortOrder = data.SortOrder,
                        IsStatic = false,
                        IsOnline = true
                    };
                    _registry.Heartbeat(data.ConnectHost, data.Port, serverInfo);
                }
                else if (type == "ac")
                {
                    if (string.IsNullOrEmpty(data.Message))
                    {
                        _log.LogWarning("ac 消息缺少必要字段 (来自 {RemoteEndpoint}): message", remoteEndpoint);
                        return;
                    }

                    string truncatedMsg = data.Message.Length > 80 ? data.Message.Substring(0, 80) : data.Message;
                    _log.LogInformation("收到 AC 推送: {Message}...", truncatedMsg);

                    await ForwardAcMessageAsync(data.Message, ct);
                }
                else
                {
                    _log.LogWarning("收到未知消息类型 \"{Type}\" (来自 {RemoteEndpoint})，已忽略", envelope.Type, remoteEndpoint);
                    return;
                }

                // 所有成功处理的包回复 "OK"
                byte[] okBytes = Encoding.UTF8.GetBytes("OK");
                await stream.WriteAsync(okBytes, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning("处理来自 {RemoteEndpoint} 的通知请求超时（超过 {Timeout}s 未完成传输）", remoteEndpoint, ReadTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "处理客户端连接时发生错误 (来自 {RemoteEndpoint})", remoteEndpoint);
            }
        }
    }

    /// <summary>
    /// 把游戏内 <c>.ac</c> 消息转发到 QQ。
    /// 按当前接入平台选择目标：NapCat 用群号，官方平台用 group_openid。
    /// </summary>
    private async Task ForwardAcMessageAsync(string message, CancellationToken ct)
    {
        var client = _botAccessor.Current;
        if (client is null || !client.IsConnected)
        {
            _log.LogWarning("AC 推送丢弃：QQ Bot 会话未激活");
            return;
        }

        string target;
        string platformTag;

        if (client.Platform == BotPlatform.OfficialQq)
        {
            var officialOpts = _officialOptsMonitor.CurrentValue;

            target = !string.IsNullOrWhiteSpace(officialOpts.AcTargetGroupOpenId)
                ? officialOpts.AcTargetGroupOpenId
                : officialOpts.NotifyGroupOpenIds.FirstOrDefault() ?? "";

            if (string.IsNullOrWhiteSpace(target))
                target = _officialClient.MostRecentGroupOpenId ?? "";

            platformTag = "官方模式";

            if (string.IsNullOrWhiteSpace(target))
            {
                _log.LogWarning("AC 推送丢弃：官方模式未配置 AcTargetGroupOpenId / NotifyGroupOpenIds，且尚未收到过任何群消息");
                return;
            }

            if (!client.SupportsActivePush)
            {
                _log.LogWarning("AC 推送丢弃：官方平台需显式开启「允许主动推送」后才能发送非被动回复消息（目标群 {Target}）", target);
                return;
            }
        }
        else
        {
            var botOpts = _botOptsMonitor.CurrentValue;
            long targetGroupId = botOpts.AcTargetGroupId;
            if (targetGroupId <= 0 && botOpts.NotifyGroupIds is { Length: > 0 })
                targetGroupId = botOpts.NotifyGroupIds[0];

            if (targetGroupId <= 0)
            {
                _log.LogWarning("AC 推送丢弃：未配置目标群号（AcTargetGroupId 和 NotifyGroupIds 均为空）");
                return;
            }

            target = targetGroupId.ToString();
            platformTag = "NapCat 模式";
        }

        var result = await client.SendGroupMessageAsync(target, message, BotReplyContext.None, ct);
        if (result.Success)
            _log.LogInformation("AC 推送已转发到群 {Target}（{Platform}）", target, platformTag);
        else
            _log.LogError("AC 推送转发到群 {Target} 失败（{Platform}）：{Error}", target, platformTag, result.Error);
    }
}
