using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Qchat.Security;
using System.Net.Sockets;
using System.Text;

namespace Server.Qcat.Socket;

public sealed class SocketCommandClient
{
    private readonly SocketServerOptions _opts;
    private readonly ILogger<SocketCommandClient> _log;

    public SocketCommandClient(IOptions<SocketServerOptions> opts, ILogger<SocketCommandClient> log)
    {
        _opts = opts.Value;
        _log = log;
    }

    public async Task<Qchat.GameAdmin.AdminReply> SendAdminAsync(ServerInfo server, Qchat.GameAdmin.AdminRequest request, CancellationToken ct)
    {
        // Probe using the existing protocol before sending a new framed message to an older plugin.
        var capability = await SendAsync(server.ConnectHost, server.Port, "game-admin-capabilities", ct);
        if (capability != "QGA1")
            return new() { Code = capability is null ? "offline" : "upgrade", Error = capability is null ? "服务器离线或超时" : "该插件不支持游戏权限管理，请升级对应的 EXILED / LabAPI 插件" };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var client = new TcpClient();
        await client.ConnectAsync(server.ConnectHost, server.Port, timeout.Token);
        using var stream = client.GetStream();
        if (!TcpAuthEnvelope.TrySeal(_opts.AuthToken, "game-admin&" + Qchat.GameAdmin.AdminJson.Write(request), out var wire, out var error))
            throw new InvalidOperationException(error);
        await Qchat.GameAdmin.AdminFrame.Write(stream, wire, timeout.Token);
        var response = await Qchat.GameAdmin.AdminFrame.Read(stream, timeout.Token);
        if (response == "Unauthorized") return new() { Code = "unauthorized", Error = "游戏服鉴权失败，请检查共享 Token" };
        return Qchat.GameAdmin.AdminJson.Read<Qchat.GameAdmin.AdminReply>(response);
    }

    public async Task<string?> SendAsync(string host, int port, string text, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= Math.Max(1, _opts.Retries); attempt++)
        {
            try
            {
                using var client = new TcpClient();

                var connectTask = client.ConnectAsync(host, port);
                var connectWinner = await Task.WhenAny(connectTask, Task.Delay(_opts.ConnectTimeoutMs, ct));
                if (connectWinner != connectTask)
                {
                    _log.LogWarning("TCP connect timeout to {Host}:{Port} (attempt {Attempt}/{Retries})", host, port, attempt, _opts.Retries);
                    continue;
                }

                using var stream = client.GetStream();
                if (!TcpAuthEnvelope.TrySeal(_opts.AuthToken, text, out string payload, out string? authError))
                {
                    _log.LogError("拒绝发送命令：{Reason}", authError);
                    return null;
                }

                byte[] bytes = Encoding.UTF8.GetBytes(payload);

                _log.LogInformation("TCP send to {Host}:{Port}: {Text}", host, port, text);
                await stream.WriteAsync(bytes, ct);

                var buffer = new byte[4096];
                var readTask = stream.ReadAsync(buffer, ct).AsTask();
                var readWinner = await Task.WhenAny(readTask, Task.Delay(_opts.ReadTimeoutMs, ct));
                if (readWinner != readTask)
                {
                    _log.LogWarning("TCP read timeout from {Host}:{Port}", host, port);
                    return null;
                }

                int count = await readTask;
                if (count <= 0)
                    return null;

                return Encoding.UTF8.GetString(buffer, 0, count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "TCP send failed to {Host}:{Port} (attempt {Attempt}/{Retries})", host, port, attempt, _opts.Retries);
            }

            if (attempt < _opts.Retries)
                await Task.Delay(_opts.RetryDelayMs, ct);
        }

        return null;
    }
}

