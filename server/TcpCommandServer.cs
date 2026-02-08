using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Log = Exiled.API.Features.Log;

namespace SocketServer
{
    internal sealed class TcpCommandServer
    {
        private readonly IPAddress _ip;
        private readonly int _port;
        private readonly Func<string, string> _dispatch;

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptLoop;

        public TcpCommandServer(string ip, int port, Func<string, string> dispatch)
        {
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("ip is required", nameof(ip));
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));

            _ip = IPAddress.Parse(ip);
            _port = port;
        }

        public void Start()
        {
            if (_listener != null)
                return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(_ip, _port);
            _listener.Start(backlog: 50);

            _acceptLoop = Task.Run(() => AcceptLoop(_cts.Token));
        }

        public void Stop()
        {
            var listener = _listener;
            if (listener == null)
                return;

            try
            {
                _cts.Cancel();
            }
            catch { }

            try
            {
                // This will break AcceptTcpClientAsync.
                listener.Stop();
            }
            catch { }

            _listener = null;

            try
            {
                _acceptLoop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            try { _cts.Dispose(); } catch { }
            _cts = null;
            _acceptLoop = null;
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClient(client, ct));
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (ct.IsCancellationRequested)
                        return;
                }
                catch (Exception ex)
                {
                    Log.Error("SocketServer accept failed: " + ex);
                    try { client?.Close(); } catch { }
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task HandleClient(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;

                    using (var stream = client.GetStream())
                    {
                        // Read a single command frame. The client always closes after sending one command.
                        var buffer = new byte[4096];

                        string request = await ReadOnceWithTimeout(stream, buffer, 2000, ct).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(request))
                        {
                            await WriteUtf8Async(stream, "empty command", ct).ConfigureAwait(false);
                            return;
                        }

                        string response;
                        try
                        {
                            response = _dispatch(request);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("SocketServer dispatch failed: " + ex);
                            response = "server error";
                        }

                        if (string.IsNullOrEmpty(response))
                            response = "ok";

                        await WriteUtf8Async(stream, response, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("SocketServer client failed: " + ex);
                }
            }
        }

        private static async Task<string> ReadOnceWithTimeout(NetworkStream stream, byte[] buffer, int timeoutMs, CancellationToken ct)
        {
            var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
            var delayTask = Task.Delay(timeoutMs, ct);
            var winner = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
            if (winner != readTask)
                return null;

            int count = await readTask.ConfigureAwait(false);
            if (count <= 0)
                return null;

            return Encoding.UTF8.GetString(buffer, 0, count).Trim('\0', '\r', '\n', ' ', '\t');
        }

        private static Task WriteUtf8Async(NetworkStream stream, string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return stream.WriteAsync(bytes, 0, bytes.Length, ct);
        }
    }
}

