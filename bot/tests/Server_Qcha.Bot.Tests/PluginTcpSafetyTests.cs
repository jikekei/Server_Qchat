using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Qchat.GameAdmin;
using Qchat.Security;
using SocketServer;

namespace Server.Qcat.Tests;

public class PluginTcpSafetyTests
{
    [Fact]
    public async Task DeadlineClosesAndReturnsEvenIfReadIgnoresCancellationAndClose()
    {
        using var stuck = new UncooperativeStream();
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => TcpDeadline.Run(
            () => AdminFrame.ReadBody(stuck, CancellationToken.None, AdminFrame.MaxRequestLength),
            stuck.Dispose, 100, CancellationToken.None));
        Assert.True(stuck.Closed);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.False(stuck.ReadTask.IsCompleted);
        // Observe the late failure too, as it would occur after disposing a real Mono socket.
        stuck.Fail();
    }

    [Fact]
    public async Task ShutdownClosesUncooperativeReadWithoutWaitingForItsDeadline()
    {
        using var stuck = new UncooperativeStream(); using var stop = new CancellationTokenSource();
        var pending = TcpDeadline.Run(() => AdminFrame.ReadBody(stuck, stop.Token), stuck.Dispose, 15000, stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(stuck.Closed); stuck.Fail();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(307201)]
    [InlineData(2097152)]
    public async Task InboundLengthIsRejectedUsingOnlyTheHeader(int length)
    {
        using var headerOnly = new MemoryStream(Header(length)[4..]);
        // InvalidDataException, not EOF: no body read or allocation was attempted.
        await Assert.ThrowsAsync<InvalidDataException>(() => AdminFrame.ReadBody(headerOnly, default, AdminFrame.MaxRequestLength));
    }

    [Fact]
    public async Task RequestLimitDoesNotTruncateLargeResponses()
    {
        var payload = new string('x', AdminFrame.MaxRequestLength + 1);
        using var outgoingRequest = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => AdminFrame.Write(outgoingRequest, payload, default, AdminFrame.MaxRequestLength));
        Assert.Equal(0, outgoingRequest.Length);
        using var response = new MemoryStream(); await AdminFrame.Write(response, payload, default);
        response.Position = 0; Assert.Equal(payload, await AdminFrame.Read(response, default));
    }

    [Theory]
    [InlineData(0)] // Silent socket: blocks before any header allocation.
    [InlineData(1)] // Partial magic: blocks while reading the rest of the header.
    [InlineData(8)] // Maximum admitted body length, followed by no body and no token.
    public async Task ActualListenerClosesStalledUnauthenticatedConnections(int bytesToSend)
    {
        var port = FreePort(); int dispatched = 0;
        var server = new TcpCommandServer("127.0.0.1", port, _ => { Interlocked.Increment(ref dispatched); return "ok"; }, () => "test-token");
        server.Start();
        try
        {
            using var peer = new TcpClient(); await peer.ConnectAsync(IPAddress.Loopback, port);
            if (bytesToSend > 0) await peer.GetStream().WriteAsync(Header(AdminFrame.MaxRequestLength).AsMemory(0, bytesToSend));
            await Closed(peer, TimeSpan.FromSeconds(5));
            Assert.Equal(0, dispatched);
            // A timed-out connection must release its slot, and the listener remains usable.
            Assert.Equal("ok", await Authenticated(port));
        }
        finally { server.Stop(); }
    }

    [Fact]
    public async Task SlowHeaderTrickleDoesNotExtendTheTotalRequestDeadline()
    {
        var port = FreePort(); var server = new TcpCommandServer("127.0.0.1", port, _ => throw new Exception("Must not dispatch"), () => "test-token");
        server.Start();
        try
        {
            using var peer = new TcpClient(); await peer.ConnectAsync(IPAddress.Loopback, port);
            var header = Header(AdminFrame.MaxRequestLength);
            await peer.GetStream().WriteAsync(header.AsMemory(0, 1));
            var closed = Closed(peer, TimeSpan.FromSeconds(4));
            for (int i = 1; i < 5; i++)
            {
                await Task.Delay(300);
                await peer.GetStream().WriteAsync(header.AsMemory(i, 1));
            }
            await closed;
        }
        finally { server.Stop(); }
    }

    [Fact]
    public async Task ActualListenerRejectsOversizedHeaderImmediately()
    {
        var port = FreePort(); var server = new TcpCommandServer("127.0.0.1", port, _ => throw new Exception("Must not dispatch"), () => "test-token");
        server.Start();
        try
        {
            using var peer = new TcpClient(); await peer.ConnectAsync(IPAddress.Loopback, port);
            await peer.GetStream().WriteAsync(Header(AdminFrame.MaxRequestLength + 1));
            await Closed(peer, TimeSpan.FromSeconds(1));
        }
        finally { server.Stop(); }
    }

    [Fact]
    public async Task ActualListenerRejectsExcessConnectionsAndClosesAllOnStop()
    {
        var port = FreePort(); var server = new TcpCommandServer("127.0.0.1", port, _ => "ok", () => "test-token");
        var clients = new List<TcpClient>(); server.Start();
        try
        {
            for (int i = 0; i < TcpCommandServer.MaxConcurrentClients; i++)
            {
                var peer = new TcpClient(); clients.Add(peer); await peer.ConnectAsync(IPAddress.Loopback, port);
                await peer.GetStream().WriteAsync(Header(AdminFrame.MaxRequestLength));
            }
            // Wait for admission, rather than relying on a sleep or client-side TCP connect completion.
            var connections = typeof(TcpCommandServer).GetField("_connections", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(server)!;
            await WaitForCount(connections, TcpCommandServer.MaxConcurrentClients);
            using var excess = new TcpClient(); await excess.ConnectAsync(IPAddress.Loopback, port);
            await Closed(excess, TimeSpan.FromSeconds(1));
            server.Stop();
            await Task.WhenAll(clients.Select(c => Closed(c, TimeSpan.FromSeconds(1))));
            server.Start(); Assert.Equal("ok", await Authenticated(port));
        }
        finally { server.Stop(); foreach (var peer in clients) peer.Dispose(); }
    }

    [Fact]
    public async Task ActualListenerStillAuthenticatesLegacyAndFramedCommands()
    {
        var port = FreePort(); int dispatched = 0;
        var server = new TcpCommandServer("127.0.0.1", port, command => { Interlocked.Increment(ref dispatched); return command; }, () => "test-token");
        server.Start();
        try
        {
            Assert.Equal("ok", await Authenticated(port));
            using var legacy = new TcpClient(); await legacy.ConnectAsync(IPAddress.Loopback, port);
            Assert.True(TcpAuthEnvelope.TrySeal("test-token", "legacy", out var wire, out _));
            await legacy.GetStream().WriteAsync(System.Text.Encoding.UTF8.GetBytes(wire));
            var reply = new byte[100]; var count = await legacy.GetStream().ReadAsync(reply).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("legacy", System.Text.Encoding.UTF8.GetString(reply, 0, count));
            using var unauthenticated = new TcpClient(); await unauthenticated.ConnectAsync(IPAddress.Loopback, port);
            await AdminFrame.Write(unauthenticated.GetStream(), "bad-signature", default);
            Assert.Equal("Unauthorized", await AdminFrame.Read(unauthenticated.GetStream(), default));
            Assert.Equal(2, dispatched);
        }
        finally { server.Stop(); }
    }

    private static async Task WaitForCount(object connections, int count)
    {
        var gate = typeof(TcpConnectionLimit).GetField("gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(connections)!;
        var clients = (HashSet<TcpClient>)typeof(TcpConnectionLimit).GetField("clients", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(connections)!;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(1))
        {
            lock (gate) { if (clients.Count == count) return; }
            await Task.Delay(5);
        }
        Assert.Fail("Listener did not admit the expected number of clients");
    }

    private static async Task<string> Authenticated(int port)
    {
        using var peer = new TcpClient(); await peer.ConnectAsync(IPAddress.Loopback, port);
        Assert.True(TcpAuthEnvelope.TrySeal("test-token", "ok", out var wire, out _));
        await AdminFrame.Write(peer.GetStream(), wire, default, AdminFrame.MaxRequestLength);
        return await AdminFrame.Read(peer.GetStream(), default).WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task Closed(TcpClient peer, TimeSpan timeout)
    {
        try { Assert.Equal(0, await peer.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(timeout)); }
        catch (IOException) { } // TCP reset is also an explicit close.
        catch (SocketException) { }
    }

    private static byte[] Header(int count) => [81, 71, 65, 49, (byte)(count >> 24), (byte)(count >> 16), (byte)(count >> 8), (byte)count];
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class UncooperativeStream : Stream
    {
        private readonly TaskCompletionSource<int> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Closed { get; private set; }
        public Task<int> ReadTask => pending.Task;
        public void Fail() => pending.TrySetException(new IOException("Socket closed"));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => pending.Task;
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
