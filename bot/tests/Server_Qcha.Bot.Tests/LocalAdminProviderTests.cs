using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin;
using Server.Qcat.LocalAdmin.Models;
using Xunit;

namespace Server_Qcha.Bot.Tests;

public class LocalAdminProviderTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public void LocalAdminOptions_DefaultValues_AreDaemonOriented()
    {
        var options = new LocalAdminOptions();
        Assert.Equal("Daemon", options.Mode);
        Assert.Equal("http://127.0.0.1:10090", options.DaemonUri);
        Assert.Equal("QchaSecret_123", options.DaemonToken);
        Assert.True(options.AutoStartDaemon);
    }

    [Fact]
    public void MappingExtensions_ToDefinition_MapsFieldsCorrectly()
    {
        var req = new LocalServerSaveRequest(
            Id: "test_srv",
            Name: "测试服",
            ExecutablePath: @"C:\Games\SCPSL.exe",
            WorkingDirectory: @"C:\Games",
            GamePort: 7778,
            ExtraArguments: "-nographics",
            AutoStart: true,
            EnableHeartbeat: true,
            HeartbeatSpanMaxThreshold: 45,
            HeartbeatRestartInSeconds: 15,
            RestartOnCrash: true,
            RestartLimit: 5,
            RestartTimeWindowSeconds: 600,
            GracefulStopTimeoutSeconds: 20,
            LaToSlBufferSize: 30000,
            SlToLaBufferSize: 250000,
            DisableAnsiColors: true,
            RedirectStandardStreams: true,
            ConsoleLevel: "warn");

        var def = req.ToDefinition();

        Assert.Equal("test_srv", def.Id);
        Assert.Equal("测试服", def.Name);
        Assert.Equal(@"C:\Games\SCPSL.exe", def.ExecutablePath);
        Assert.Equal(@"C:\Games", def.WorkingDirectory);
        Assert.Equal(7778, def.GamePort);
        Assert.Equal("-nographics", def.ExtraArguments);
        Assert.True(def.AutoStart);
        Assert.True(def.EnableHeartbeat);
        Assert.Equal(45, def.HeartbeatSpanMaxThreshold);
        Assert.Equal(15, def.HeartbeatRestartInSeconds);
        Assert.True(def.RestartOnCrash);
        Assert.Equal(5, def.RestartLimit);
        Assert.Equal(600, def.RestartTimeWindowSeconds);
        Assert.Equal(20, def.GracefulStopTimeoutSeconds);
        Assert.Equal(30000, def.LaToSlBufferSize);
        Assert.Equal(250000, def.SlToLaBufferSize);
        Assert.True(def.DisableAnsiColors);
        Assert.True(def.RedirectStandardStreams);
        Assert.Equal("warn", def.ConsoleLevel);
    }

    [Fact]
    public void MappingExtensions_ToDto_ConvertsConsoleLine()
    {
        var line = new ConsoleLine(
            Seq: 101,
            Time: new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc),
            Kind: ConsoleLineKind.Output,
            Color: 12, // Red
            Text: "Server error occurred",
            ColorHex: "#ff0000");

        var dto = line.ToDto();

        Assert.Equal(101, dto.Seq);
        Assert.Equal("output", dto.Kind);
        Assert.Equal(12, dto.Color);
        Assert.Equal("Server error occurred", dto.Text);
        Assert.Equal("#ff0000", dto.ColorHex);
    }

    [Fact]
    public async Task DaemonLocalAdminProvider_ListServers_SendsTokenAndParsesResponse()
    {
        string? capturedToken = null;
        var mockResponse = new LocalAdminListResult(
            Enabled: true,
            Total: 1,
            Source: "config-file",
            ConfigPath: "data/localadmin-servers.json",
            ConsoleLevels: ConsoleCaptureLevels.DescribeTyped(),
            Servers: new[]
            {
                new LocalServerStatus(
                    "srv1", "主服", @"C:\SCPSL.exe", @"C:\", 7777,
                    true, 12345, 10091, true, "active", true, DateTime.UtcNow,
                    0, 0, "none", 0, 4, false, DateTime.UtcNow, 100.0, true, null, "all")
            },
            Definitions: new[]
            {
                new LocalServerDefinition { Id = "srv1", Name = "主服", GamePort = 7777 }
            });

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Headers.TryGetValues("X-Daemon-Token", out var vals))
                capturedToken = vals.FirstOrDefault();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(mockResponse)
            };
        });

        var options = Options.Create(new LocalAdminOptions
        {
            DaemonUri = "http://127.0.0.1:10090",
            DaemonToken = "TestSecret_999",
            AutoStartDaemon = false
        });

        var client = new HttpClient(handler);
        var provider = new DaemonLocalAdminProvider(client, options, NullLogger<DaemonLocalAdminProvider>.Instance);

        var result = await provider.ListServersAsync();

        Assert.Equal("TestSecret_999", capturedToken);
        Assert.True(result.Enabled);
        Assert.Equal(1, result.Total);
        Assert.Single(result.Servers);
        Assert.Equal("srv1", result.Servers[0].Id);
        Assert.True(result.Servers[0].Running);
        Assert.Equal(12345, result.Servers[0].ProcessId);
    }

    [Fact]
    public async Task DaemonLocalAdminProvider_WhenDaemonUnreachable_ReturnsSafeFailure()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            throw new HttpRequestException("Connection refused");
        });

        var options = Options.Create(new LocalAdminOptions
        {
            DaemonUri = "http://127.0.0.1:10090",
            AutoStartDaemon = false
        });

        var client = new HttpClient(handler);
        var provider = new DaemonLocalAdminProvider(client, options, NullLogger<DaemonLocalAdminProvider>.Instance);

        var listResult = await provider.ListServersAsync();
        Assert.Equal("daemon-disconnected", listResult.Source);
        Assert.Empty(listResult.Servers);

        var actionResult = await provider.StartAsync("srv1");
        Assert.False(actionResult.Success);
        Assert.Contains("无法连接到 LocalAdmin 守护节点", actionResult.Error);
    }

    [Fact]
    public async Task DaemonLocalAdminProvider_ActionCommands_ExecuteCorrectly()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/start"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, response = "已启动" }) };

            if (req.RequestUri.AbsolutePath.EndsWith("/stop"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, response = "已停止" }) };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var options = Options.Create(new LocalAdminOptions
        {
            DaemonUri = "http://127.0.0.1:10090",
            AutoStartDaemon = false
        });

        var client = new HttpClient(handler);
        var provider = new DaemonLocalAdminProvider(client, options, NullLogger<DaemonLocalAdminProvider>.Instance);

        var startRes = await provider.StartAsync("srv1");
        Assert.True(startRes.Success);
        Assert.Equal("已启动", startRes.Message);

        var stopRes = await provider.StopAsync("srv1", force: false);
        Assert.True(stopRes.Success);
        Assert.Equal("已停止", stopRes.Message);
    }

    [Fact]
    public async Task DaemonLocalAdminProvider_GetDaemonStatusAsync_WhenOnline_ReturnsMetrics()
    {
        var mockStatus = new DaemonStatusResult(
            Online: true,
            Status: "running",
            Version: "2.0.0",
            Pid: 5432,
            StartTime: new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc),
            UptimeSeconds: 1200,
            MemoryWorkingSetMb: 45.8,
            MemoryPrivateMb: 38.2,
            ThreadCount: 16,
            ServerCount: 2,
            RunningServerCount: 1,
            ListenUri: "http://127.0.0.1:10090",
            Message: "运行中");

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/status"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(mockStatus)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var options = Options.Create(new LocalAdminOptions
        {
            DaemonUri = "http://127.0.0.1:10090",
            AutoStartDaemon = false
        });

        var client = new HttpClient(handler);
        var provider = new DaemonLocalAdminProvider(client, options, NullLogger<DaemonLocalAdminProvider>.Instance);

        var status = await provider.GetDaemonStatusAsync();

        Assert.True(status.Online);
        Assert.Equal("running", status.Status);
        Assert.Equal(5432, status.Pid);
        Assert.Equal(45.8, status.MemoryWorkingSetMb);
        Assert.Equal(38.2, status.MemoryPrivateMb);
        Assert.Equal(16, status.ThreadCount);
        Assert.Equal(2, status.ServerCount);
        Assert.Equal(1, status.RunningServerCount);
    }

    [Fact]
    public async Task DaemonLocalAdminProvider_GetDaemonStatusAsync_WhenOffline_ReturnsOfflineStatus()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            throw new HttpRequestException("Daemon not reachable");
        });

        var options = Options.Create(new LocalAdminOptions
        {
            DaemonUri = "http://127.0.0.1:10090",
            AutoStartDaemon = false
        });

        var client = new HttpClient(handler);
        var provider = new DaemonLocalAdminProvider(client, options, NullLogger<DaemonLocalAdminProvider>.Instance);

        var status = await provider.GetDaemonStatusAsync();

        Assert.False(status.Online);
        Assert.Contains(status.Status, new[] { "offline", "unresponsive" });
    }

    [Fact]
    public async Task DaemonLocalAdminProvider_StopDaemonAsync_InvokesShutdownEndpoint()
    {
        bool shutdownCalled = false;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shutdown"))
            {
                shutdownCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var options = Options.Create(new LocalAdminOptions
        {
            DaemonUri = "http://127.0.0.1:10090",
            AutoStartDaemon = false
        });

        var client = new HttpClient(handler);
        var provider = new DaemonLocalAdminProvider(client, options, NullLogger<DaemonLocalAdminProvider>.Instance);

        var stopRes = await provider.StopDaemonAsync();

        Assert.True(shutdownCalled);
        Assert.True(stopRes.Success);
    }
}
