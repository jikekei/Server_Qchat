using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.Services;
using Server.Qcat.Socket;
using Server.Qcat.Web;
using Xunit;

namespace Server.Qcat.Tests;

public sealed class ServerStatusMonitorTests
{
    [Fact]
    public void InitialStatus_ReturnsValidStructure()
    {
        var socketOptions = Options.Create(new SocketServerOptions());
        var registry = new ServerRegistry(socketOptions, NullLogger<ServerRegistry>.Instance);
        var history = new PlayerHistoryTracker(
            registry,
            new FakeGateway(),
            NullLogger<PlayerHistoryTracker>.Instance,
            new TestHostEnvironment());

        var service = new ServerStatusMonitorService(
            registry,
            history,
            NullLogger<ServerStatusMonitorService>.Instance);

        var status = service.GetCurrentStatus();

        Assert.NotNull(status);
        Assert.InRange(status.Load, 0, 100);
        Assert.Contains(status.Status, new[] { "Idle", "Normal", "Medium", "High", "Critical", "Overload" });
        Assert.NotNull(status.StatusText);
        Assert.NotNull(status.PrimaryBottleneck);
        Assert.NotNull(status.SecondaryBottleneck);
        Assert.Equal(6, status.Bottlenecks.Count);
        Assert.NotNull(status.Details);
    }

    private sealed class FakeGateway : IServerCommandGateway
    {
        public string Name => "Fake";
        public Task<ServerCommandResult> SendAsync(ServerInfo server, string command, System.Threading.CancellationToken ct = default)
        {
            return Task.FromResult(ServerCommandResult.Fail("Not implemented"));
        }
    }

    private sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
