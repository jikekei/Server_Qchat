using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
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

    public async Task<string?> SendAsync(int port, string text, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= Math.Max(1, _opts.Retries); attempt++)
        {
            try
            {
                using var client = new TcpClient();

                var connectTask = client.ConnectAsync(_opts.Host, port);
                var connectWinner = await Task.WhenAny(connectTask, Task.Delay(_opts.ConnectTimeoutMs, ct));
                if (connectWinner != connectTask)
                {
                    _log.LogWarning("TCP connect timeout to {Host}:{Port} (attempt {Attempt}/{Retries})", _opts.Host, port, attempt, _opts.Retries);
                    continue;
                }

                using var stream = client.GetStream();
                byte[] bytes = Encoding.UTF8.GetBytes(text);

                _log.LogInformation("TCP send to {Host}:{Port}: {Text}", _opts.Host, port, text);
                await stream.WriteAsync(bytes, ct);

                var buffer = new byte[4096];
                var readTask = stream.ReadAsync(buffer, ct).AsTask();
                var readWinner = await Task.WhenAny(readTask, Task.Delay(_opts.ReadTimeoutMs, ct));
                if (readWinner != readTask)
                {
                    _log.LogWarning("TCP read timeout from {Host}:{Port}", _opts.Host, port);
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
                _log.LogWarning(ex, "TCP send failed to {Host}:{Port} (attempt {Attempt}/{Retries})", _opts.Host, port, attempt, _opts.Retries);
            }

            if (attempt < _opts.Retries)
                await Task.Delay(_opts.RetryDelayMs, ct);
        }

        return null;
    }
}

