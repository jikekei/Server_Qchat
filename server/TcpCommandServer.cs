#if NET8_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Qchat.Security;
using Qchat.GameAdmin;
using Log = Exiled.API.Features.Log;

namespace SocketServer
{
    internal sealed class TcpCommandServer
    {
        internal const int MaxConcurrentClients = 32;
        private const int RequestTimeoutMs = 2000;
        private const int ResponseTimeoutMs = 15000;
        private TcpConnectionLimit _connections;
        private readonly IPAddress _ip;
        private readonly int _port;
        private readonly Func<string, string> _dispatch;
        private readonly Func<string> _authToken;
        private readonly TcpAuthNonceCache _nonces = new TcpAuthNonceCache();

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptLoop;

        public TcpCommandServer(string ip, int port, Func<string, string> dispatch, Func<string> authToken)
        {
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("ip is required", nameof(ip));
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            _authToken = authToken ?? (() => string.Empty);

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
            _connections = new TcpConnectionLimit(MaxConcurrentClients);

            var listener = _listener;
            var connections = _connections;
            var token = _cts.Token;
            _acceptLoop = Task.Run(() => AcceptLoop(listener, connections, token));
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

            _connections.Dispose();
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

        private async Task AcceptLoop(TcpListener listener, TcpConnectionLimit connections, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (!connections.TryAdd(client))
                    {
                        client.Close();
                        continue;
                    }
                    // Admission happens before scheduling: at most MaxConcurrentClients workers can exist.
                    _ = Task.Run(() => HandleClient(client, connections, ct));
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

        private async Task HandleClient(TcpClient client, TcpConnectionLimit connections, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;

                    using (var stream = client.GetStream())
                    {
                        var remoteIP = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                        var incoming = await TcpDeadline.Run(() => ReadRequest(stream, ct), client.Close, RequestTimeoutMs, ct).ConfigureAwait(false);
                        bool framed = incoming.Item1;
                        string request = incoming.Item2;
                        if (string.IsNullOrWhiteSpace(request))
                        {
                            await TcpDeadline.Run(() => WriteUtf8Async(stream, "empty command", ct), client.Close, ResponseTimeoutMs, ct).ConfigureAwait(false);
                            return;
                        }

                        string token = _authToken();
                        string commandToDispatch;
                        string authError;
                        if (!TcpAuthEnvelope.TryUnseal(token, request, _nonces, out commandToDispatch, out authError))
                        {
                            Log.Warn($"[Server_Qcha] 拒绝来自 {remoteIP} 的未授权连接：{authError}");
                            await TcpDeadline.Run(() => framed ? AdminFrame.Write(stream, "Unauthorized", ct)
                                : WriteUtf8Async(stream, "Unauthorized", ct), client.Close, ResponseTimeoutMs, ct).ConfigureAwait(false);
                            return;
                        }

                        Log.Debug($"[Server_Qcha] 收到命令 [{commandToDispatch}] 来自 {remoteIP}");

                        string response;
                        try
                        {
                            response = _dispatch(commandToDispatch);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("SocketServer dispatch failed: " + ex);
                            response = "server error";
                        }

                        if (string.IsNullOrEmpty(response))
                            response = "ok";

                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        Log.Debug($"[Server_Qcha] 命令 [{commandToDispatch}] 执行完成，响应长度 {responseBytes.Length} 字节");
                        await TcpDeadline.Run(() => framed ? AdminFrame.Write(stream, response, ct)
                            : stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct), client.Close, ResponseTimeoutMs, ct).ConfigureAwait(false);
                    }
                }
                catch (TimeoutException) { }
                catch (OperationCanceledException) { }
                catch (System.IO.InvalidDataException) { }
                catch (System.IO.EndOfStreamException) { }
                catch (System.IO.IOException) { }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested) Log.Error("SocketServer client failed: " + ex);
                }
                finally
                {
                    connections.Remove(client);
                }
            }
        }

        private static async Task<Tuple<bool, string>> ReadRequest(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[4096];
            // The hard deadline covers header and body together, including slow trickles of bytes.
            await AdminFrame.ReadExactly(stream, buffer, 0, 1, ct).ConfigureAwait(false);
            if (buffer[0] == 81)
            {
                await AdminFrame.ReadExactly(stream, buffer, 1, 3, ct).ConfigureAwait(false);
                if (Encoding.ASCII.GetString(buffer, 0, 4) != "QGA1") throw new System.IO.InvalidDataException("Invalid frame magic");
                return Tuple.Create(true, await AdminFrame.ReadBody(stream, ct, AdminFrame.MaxRequestLength).ConfigureAwait(false));
            }
            int read = await stream.ReadAsync(buffer, 1, buffer.Length - 1, ct).ConfigureAwait(false);
            return Tuple.Create(false, Encoding.UTF8.GetString(buffer, 0, read + 1).Trim('\0', '\r', '\n', ' ', '\t'));
        }

        private static Task WriteUtf8Async(NetworkStream stream, string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return stream.WriteAsync(bytes, 0, bytes.Length, ct);
        }
    }
}

