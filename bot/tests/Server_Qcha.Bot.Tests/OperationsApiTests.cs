using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Qcat.Web;
using Server.Qcat.LocalAdmin;
using Server.Qcat.Configuration;
using Server.Qcat.Bot;
using Server.Qcat.Data;
using Server.Qcat.Socket;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Server.Qcat.LocalAdmin.Models;
using System.Net.Http.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Server_Qcha.Bot.Tests;

public class OperationsApiTests
{
    public class ArchiveProvider : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(ILocalAdminProvider.ListServersAsync))
            {
                var status = new LocalServerStatus("main", "Main", "", "", 7777, false, null, 0, false, "", false, null, 0, 0, "", 0, 0, false, null, 0, false, null, "all");
                return Task.FromResult(new LocalAdminListResult(true, 1, "test", "", Array.Empty<ConsoleLevelDescription>(), new[] { status }, Array.Empty<LocalServerDefinition>()));
            }
            if (method.Name == nameof(ILocalAdminProvider.ReadArchiveAsync))
            {
                if ((string)args![0]! != "main") return Task.FromResult<LogArchiveResult?>(null);
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                var files = new[] { new ArchiveFile($"{DateTime.Now:yyyy-MM}/{today}/a.txt", 10), new ArchiveFile($"{DateTime.Now:yyyy-MM}/{today}/b.txt", 20), new ArchiveFile("2020-01-01.log", 30) };
                return Task.FromResult<LogArchiveResult?>(new(files, new[] { "sample log" }, 10, false));
            }
            throw new InvalidOperationException("Unexpected operation: " + method.Name);
        }
    }
    [Fact]
    public async Task HttpApiRejectsMissingWrongDisabledAndPanelSessionTokens()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string? panelConnectionString = null;
        Directory.CreateDirectory(root);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root });
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<OperationsApiStore>();
            builder.Services.AddSingleton<PanelLogBuffer>();
            builder.Services.AddSingleton<PanelAuthService>();
            builder.Services.AddSingleton<PanelDatabase>();
            builder.Services.AddSingleton<BotClientAccessor>();
            builder.Services.AddSingleton<CommandRouter>();
            builder.Services.AddSingleton<NapCatBotClient>();
            builder.Services.AddSingleton<BotBindingStore>();
            builder.Services.AddSingleton<PlayerRepository>();
            builder.Services.AddSingleton<SocketCommandClient>();
            builder.Services.AddSingleton<ServerRegistry>();
            builder.Services.AddOptions();
            builder.Services.Configure<SocketServerOptions>(options =>
            {
                options.AuthToken = "test-token";
                options.Ports = [];
            });
            builder.Services.AddSingleton(DispatchProxy.Create<ILocalAdminProvider, ArchiveProvider>());
            await using var app = builder.Build();
            app.MapOperationsApi();
            app.Urls.Add("http://127.0.0.1:0");
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ops/v1/panel/logs")).StatusCode);
            var store = app.Services.GetRequiredService<OperationsApiStore>();
            store.Save(true, false);
            var (initial, key) = store.CreateToken("initial");
            client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ops/v1/panel/logs")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", key);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/ops/v1/panel/logs")).StatusCode);
            var servers = await client.GetFromJsonAsync<JsonElement>("/api/ops/v1/log-servers");
            Assert.Equal(1, servers.GetProperty("count").GetInt32());
            Assert.Equal("main", servers.GetProperty("servers")[0].GetProperty("id").GetString());
            var dates = await client.GetFromJsonAsync<JsonElement>("/api/ops/v1/servers/main/log-dates");
            Assert.Equal(2, dates.GetProperty("dates").GetArrayLength());
            var files = await client.GetFromJsonAsync<JsonElement>("/api/ops/v1/servers/main/log-files?date=today");
            Assert.Equal(2, files.GetProperty("files").GetArrayLength());
            var all = await client.GetFromJsonAsync<JsonElement>("/api/ops/v1/servers/main/log-files");
            Assert.Equal(3, all.GetProperty("files").GetArrayLength());
            string file = files.GetProperty("files")[0].GetProperty("name").GetString()!;
            var content = await client.GetFromJsonAsync<JsonElement>("/api/ops/v1/servers/main/archive?file=" + Uri.EscapeDataString(file));
            Assert.Equal("sample log", content.GetProperty("lines")[0].GetString());
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.GetAsync("/api/ops/v1/servers/main/log-files?date=2026-99-99")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/api/ops/v1/servers/missing/log-dates")).StatusCode);
            var empty = await client.GetFromJsonAsync<JsonElement>("/api/ops/v1/servers/main/log-files?date=2021-01-01");
            Assert.Equal(0, empty.GetProperty("files").GetArrayLength());
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/operations/settings")).StatusCode);

            const string commandPath = "/api/ops/v1/bot/commands";
            Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync(commandPath, new { command = "/help" })).StatusCode);
            store.Save(true, true);
            var panelDatabase = app.Services.GetRequiredService<PanelDatabase>();
            panelConnectionString = panelDatabase.ConnectionString;
            panelDatabase.EnsureCreated();
            var accessor = app.Services.GetRequiredService<BotClientAccessor>();
            typeof(BotClientAccessor).GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(accessor, new[] { app.Services.GetRequiredService<NapCatBotClient>() });
            var help = await client.PostAsJsonAsync(commandPath, new { command = "/h" });
            help.EnsureSuccessStatusCode();
            var reply = await help.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("help", reply.GetProperty("command").GetString());
            Assert.Contains("/ban", reply.GetProperty("content").GetString());
            Assert.False(reply.TryGetProperty("success", out _));
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync(commandPath, new { command = "/stat" })).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync(commandPath, new { command = "/unknown" })).StatusCode);
            await CheckPlayerSummariesAsync(client, commandPath, app.Services.GetRequiredService<ServerRegistry>());

            const string tokensPath = "/api/operations/tokens";
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync(tokensPath, new { name = "monitor" })).StatusCode);
            const string adminPassword = "panel-test-password";
            await panelDatabase.CreateAccountAsync(new PanelAccount
            {
                Username = "test-admin",
                PasswordHash = PanelAuthService.HashPassword(adminPassword),
                Permissions = PanelPermission.AccountsManage,
                IsEnabled = true,
            });
            var (session, _) = await app.Services.GetRequiredService<PanelAuthService>()
                .LoginAsync("test-admin", adminPassword);
            client.DefaultRequestHeaders.Authorization = new("Bearer", session!.Token);
            using var created = await client.PostAsJsonAsync(tokensPath, new { name = "monitor" });
            Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
            Assert.True(created.Headers.CacheControl?.NoStore);
            var added = await created.Content.ReadFromJsonAsync<JsonElement>();
            string addedKey = added.GetProperty("key").GetString()!;
            string addedId = added.GetProperty("id").GetString()!;
            var settings = await client.GetFromJsonAsync<JsonElement>("/api/operations/settings");
            Assert.Equal(2, settings.GetProperty("tokens").GetArrayLength());
            Assert.All(settings.GetProperty("tokens").EnumerateArray(), token =>
                Assert.Equal(new[] { "id", "name" }, token.EnumerateObject().Select(property => property.Name)));
            client.DefaultRequestHeaders.Authorization = new("Bearer", addedKey);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/ops/v1/panel/logs")).StatusCode);
            using var invalidRound = await client.PostAsJsonAsync(commandPath, new { command = "/round" });
            Assert.Equal(HttpStatusCode.OK, invalidRound.StatusCode);
            var roundReply = await invalidRound.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("用法", roundReply.GetProperty("content").GetString());
            var audit = await panelDatabase.ListAuditAsync(100);
            Assert.Contains(audit, entry => entry.Username == $"api-key:{initial.Id}" &&
                entry.Action == "api." + commandPath && entry.Success);
            Assert.Contains(audit, entry => entry.Username == $"api-key:{addedId}" &&
                entry.Action == "api." + commandPath && !entry.Success);
            client.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);
            Assert.Equal(System.Net.HttpStatusCode.NoContent, (await client.DeleteAsync(tokensPath + "/" + addedId)).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", addedKey);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ops/v1/panel/logs")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", key);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/ops/v1/panel/logs")).StatusCode);

            store.Save(false, false);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync(commandPath, new { command = "/help" })).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ops/v1/panel/logs")).StatusCode);
            await app.StopAsync();
        }
        finally
        {
            if (panelConnectionString is not null)
            {
                using var connection = new SqliteConnection(panelConnectionString);
                SqliteConnection.ClearPool(connection);
            }
            Directory.Delete(root, true);
        }
    }

    private static async Task CheckPlayerSummariesAsync(HttpClient client, string path, ServerRegistry registry)
    {
        const string emptyMessage = "全服运行正常，当前暂无玩家在线，别等了，赶紧上线暖服！";
        const string emptyServer = "空服\r\n在线人数:0/30\r\n在线管理:0人";
        const string activeServer = "有人服\r\n在线人数:2/30\r\n在线管理:0人";
        using var empty = new TcpListener(IPAddress.Loopback, 0);
        using var active = new TcpListener(IPAddress.Loopback, 0);
        empty.Start();
        active.Start();
        int emptyPort = ((IPEndPoint)empty.LocalEndpoint).Port;
        int activePort = ((IPEndPoint)active.LocalEndpoint).Port;
        registry.Register(new ServerInfo { ConnectHost = "127.0.0.1", Port = emptyPort });
        registry.Register(new ServerInfo { ConnectHost = "127.0.0.1", Port = activePort });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var emptyReplies = ReplyToQueriesAsync(empty, timeout.Token, emptyServer, emptyServer, emptyServer, emptyServer);
        var activeReplies = ReplyToQueriesAsync(active, timeout.Token, activeServer, activeServer, "有人服详情", emptyServer, emptyServer, emptyServer);

        async Task<string> ReportAsync(string command)
        {
            using var response = await client.PostAsJsonAsync(path, new { command }, timeout.Token);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
            return payload.GetProperty("content").GetString()!;
        }

        string players = await ReportAsync("/cx");
        Assert.Contains("总在线人数: 2", players);
        Assert.DoesNotContain("空服", players);
        Assert.Equal("有人服详情", await ReportAsync("/info"));
        Assert.Equal(emptyMessage, await ReportAsync("/cx"));
        Assert.Equal(emptyMessage, await ReportAsync("/info"));
        registry.MarkOffline("127.0.0.1", emptyPort);
        Assert.Contains("部分服务器状态暂不可用", await ReportAsync("/cx"));
        await Task.WhenAll(emptyReplies, activeReplies);
    }

    private static async Task ReplyToQueriesAsync(TcpListener listener, CancellationToken ct, params string[] replies)
    {
        foreach (string reply in replies)
        {
            using var connection = await listener.AcceptTcpClientAsync(ct);
            using var stream = connection.GetStream();
            var buffer = new byte[1024];
            Assert.True(await stream.ReadAsync(buffer, ct) > 0);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(reply), ct);
        }
    }
    [Fact]
    public void ArchiveRejectsTraversalAndPagesUtf8WithoutCorruption()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var content = new string('中', 100000);
            File.WriteAllText(Path.Combine(root, "test.txt"), content, new System.Text.UTF8Encoding(false));
            Assert.Throws<ArgumentException>(() => LogArchive.Read(root, "../secret.txt", 0, default));
            var first = LogArchive.Read(root, "test.txt", 0, default);
            Assert.True(first.More);
            var second = LogArchive.Read(root, "test.txt", first.Next, default);
            Assert.False(second.More);
            Assert.Equal(content, string.Concat(first.Lines) + string.Concat(second.Lines));
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Env(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    [Fact]
    public void LegacyAndNewTokensPersistAndCanBeRevokedIndependently()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            const string legacyKey = "legacy-key";
            Directory.CreateDirectory(Path.Combine(root, "data"));
            string path = Path.Combine(root, "data", "operations-api.json");
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(legacyKey)));
            File.WriteAllText(path, JsonSerializer.Serialize(new { Enabled = true, AllowControl = false, KeyHash = hash }));

            var store = new OperationsApiStore(new Env(root));
            Assert.True(store.Validate(legacyKey, out bool allowControl));
            Assert.False(allowControl);
            var legacy = Assert.Single(store.Snapshot().Tokens);
            var (_, key) = store.CreateToken("monitor");
            Assert.DoesNotContain(key, File.ReadAllText(path));
            store = new OperationsApiStore(new Env(root));
            Assert.True(store.Validate(legacyKey, out _));
            Assert.True(store.Validate(key, out _));
            Assert.False(store.Validate("wrong", out _));

            Assert.NotNull(store.RevokeToken(legacy.Id));
            store = new OperationsApiStore(new Env(root));
            Assert.False(store.Validate(legacyKey, out _));
            Assert.True(store.Validate(key, out _));
            store.Save(false, false);
            Assert.False(store.Validate(key, out _));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task StdoutSurvivesDisplayFilteringAndUsesDatedTextArchive()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await using var instance = new LocalServerInstance(new LocalServerDefinition { Id = "test", ConsoleLevel = "off" },
                new LocalAdminOptions(), NullLoggerFactory.Instance, root);
            typeof(LocalServerInstance).GetMethod("Append", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(instance, new object[] { ConsoleLineKind.Stdout, (byte)7, "stdout-kept" });
            var file = Assert.Single(Directory.GetFiles(root, "*.txt", SearchOption.AllDirectories));
            Assert.Contains(Path.Combine(DateTime.Now.ToString("yyyy-MM"), DateTime.Now.ToString("yyyy-MM-dd")), file);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            Assert.Contains("stdout-kept", reader.ReadToEnd());
            Assert.Contains(instance.ReadLines(0, 100, out _, out _, out _), l => l.Text == "stdout-kept");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
