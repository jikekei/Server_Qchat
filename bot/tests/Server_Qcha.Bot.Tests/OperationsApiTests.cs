using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Qcat.Web;
using Server.Qcat.LocalAdmin;
using Server.Qcat.Configuration;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Server.Qcat.LocalAdmin.Models;
using System.Net.Http.Json;
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
        Directory.CreateDirectory(root);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root });
            builder.Services.AddSingleton<OperationsApiStore>();
            builder.Services.AddSingleton<PanelLogBuffer>();
            builder.Services.AddSingleton<PanelAuthService>();
            builder.Services.AddSingleton<PanelDatabase>();
            builder.Services.AddOptions();
            builder.Services.AddSingleton(DispatchProxy.Create<ILocalAdminProvider, ArchiveProvider>());
            await using var app = builder.Build();
            app.MapOperationsApi();
            app.Urls.Add("http://127.0.0.1:0");
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ops/v1/panel/logs")).StatusCode);
            var store = app.Services.GetRequiredService<OperationsApiStore>();
            var key = store.Save(true, false, true)!;
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
            store.Save(false, false, false);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ops/v1/panel/logs")).StatusCode);
            await app.StopAsync();
        }
        finally { Directory.Delete(root, true); }
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
    public void KeysAreHashedPersistedRotatedAndDisabled()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OperationsApiStore(new Env(root));
            Assert.False(store.Validate(""));
            string key = store.Save(true, false, true)!;
            Assert.True(store.Validate(key));
            Assert.False(store.Validate("wrong"));
            Assert.DoesNotContain(key, File.ReadAllText(Path.Combine(root, "data", "operations-api.json")));
            Assert.True(new OperationsApiStore(new Env(root)).Validate(key));
            string next = store.Save(true, true, true)!;
            Assert.False(store.Validate(key));
            Assert.True(store.Validate(next));
            store.Save(false, false, false);
            Assert.False(store.Validate(next));
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
