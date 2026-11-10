using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Qchat.GameAdmin;
using Qchat.Security;
using Server.Qcat.Configuration;
using Server.Qcat.Socket;
using Server.Qcat.Web;

namespace Server.Qcat.Tests;

public class GameAdminTests
{
    private static AdminSnapshot Snapshot() => new()
    {
        Revision = "original",
        Catalog = [new() { Key = "Kick", Name = "踢出" }, new() { Key = "Ban", Name = "封禁" }],
        Groups = [new() { Key = "base", Badge = "基础", PluginPermissions = ["plugin.*"] }, new() { Key = "staff", Badge = "管理", Permissions = ["Kick"], Inheritance = ["base"] }],
        Members = [new() { UserId = "123@steam", Group = "staff", Deny = ["Kick"], Allow = ["Ban"], PluginDeny = ["plugin.destroy"] }]
    };

    [Fact]
    public void OverridesWinOverGroupAndWildcard_OffWinsOverEverything()
    {
        var data = Snapshot(); var member = data.Members[0];
        Assert.Equal(new[] { "Ban" }, AdminPolicy.Native(data.Groups[1], member));
        Assert.True(AdminPolicy.Plugin(data, member, "plugin.read"));
        member.PluginAllow.Add("plugin.destroy");
        Assert.False(AdminPolicy.Plugin(data, member, "plugin.destroy"));
        member.PluginsEnabled = false;
        Assert.False(AdminPolicy.Plugin(data, member, "plugin.read"));
        member.PluginsEnabled = true;
        data.Groups[1].PluginsEnabled = false;
        Assert.True(AdminPolicy.Plugin(data, member, "plugin.read"));
        member.PluginsEnabled = null;
        Assert.False(AdminPolicy.Plugin(data, member, "plugin.read"));
    }

    [Fact]
    public void GroupEditPreservesPersonalOverridesAndOriginalSnapshot()
    {
        var data = Snapshot(); var updated = AdminJson.Clone(data.Groups[1]); updated.Permissions.Add("Ban");
        var next = AdminChanges.Apply(data, new() { Operation = "update-group", Key = "staff", Group = updated });
        Assert.Equal("Kick", Assert.Single(next.Members[0].Deny));
        Assert.DoesNotContain("Ban", data.Groups[1].Permissions);
        Assert.Contains("Ban", next.Groups[1].Permissions);
        Assert.Equal(new[] { "Ban" }, AdminPolicy.Native(next.Groups[1], next.Members[0]));
    }

    [Fact]
    public void DeleteReferencedGroupIsRejected_OfflineMemberCanBeRemoved()
    {
        var data = Snapshot();
        Assert.Throws<InvalidOperationException>(() => AdminChanges.Apply(data, new() { Operation = "delete-group", Key = "staff" }));
        Assert.Throws<InvalidOperationException>(() => AdminChanges.Apply(data, new() { Operation = "delete-group", Key = "base" }));
        var next = AdminChanges.Apply(data, new() { Operation = "delete-member", Key = "123@steam" });
        Assert.Empty(next.Members);
        next = AdminChanges.Apply(next, new() { Operation = "delete-group", Key = "staff" });
        Assert.DoesNotContain(next.Groups, g => g.Key == "staff");
    }

    [Fact]
    public void CopyCannotOverwriteOrIntroduceUnsupportedPermissions()
    {
        var data = Snapshot();
        Assert.Throws<InvalidOperationException>(() => AdminChanges.Apply(data, new() { Operation = "create-group", Group = data.Groups[1] }));
        var copy = AdminJson.Clone(data.Groups[1]); copy.Key = "other"; copy.Permissions.Add("FuturePermission");
        Assert.Contains("FuturePermission", Assert.Throws<InvalidOperationException>(() => AdminChanges.Apply(data, new() { Operation = "create-group", Group = copy })).Message);
        copy.Permissions.Remove("FuturePermission");
        var next = AdminChanges.Apply(data, new() { Operation = "create-group", Group = copy });
        Assert.DoesNotContain(next.Members, m => m.Group == "other");
    }

    [Fact]
    public void CycleAndConfigInjectionAreRejected()
    {
        var data = Snapshot(); data.Groups[0].Inheritance.Add("staff");
        Assert.Throws<InvalidOperationException>(() => AdminValidation.Validate(data));
        data = Snapshot(); data.Groups[0].Badge = "admin\nRoles:\n - injected";
        Assert.Throws<InvalidOperationException>(() => AdminValidation.Validate(data));
        data = Snapshot(); data.Members[0].UserId = "2";
        Assert.Throws<InvalidOperationException>(() => AdminValidation.Validate(data));
    }

    [Fact]
    public void NativeConfigRoundTripPreservesUnrelatedFieldsAndComments()
    {
        const string original = "# heading\r\nMembers:\r\n - 123@steam: staff\r\n# preserve me\r\nRoles:\r\n - staff\r\nPermissions:\r\n - Kick: [staff]\r\n - Future: [other]\r\noverride_password: untouched\r\nOther:\r\n - arbitrary\r\n";
        var config = new NativeConfig(original);
        Assert.Equal("staff", config.Map("Members")["123@steam"]);
        config.Set("Members", null!, ["456@steam: staff"]);
        config.Set("staff_badge", "管理员");
        var output = config.ToString();
        Assert.Contains("# preserve me\r\n", output);
        Assert.Contains(" - Future: [other]\r\n", output);
        Assert.Contains("override_password: untouched\r\nOther:\r\n - arbitrary", output);
        Assert.DoesNotContain("123@steam", output);
        Assert.Equal("管理员", new NativeConfig(output).Scalar("staff_badge"));
    }

    [Fact]
    public void CommitRollsBackEveryFileOnApplyFailure_AndRecoversJournal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qga-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string a = Path.Combine(dir, "a"), b = Path.Combine(dir, "b"), journal = Path.Combine(dir, "journal");
            File.WriteAllText(a, "before"); var before = AdminFiles.Revision([a, b]); int calls = 0;
            Assert.Throws<IOException>(() => AdminFiles.Commit(journal, new() { [a] = "after", [b] = "new" }, () => { if (++calls == 1) throw new IOException("apply failed"); }));
            Assert.Equal("before", File.ReadAllText(a)); Assert.False(File.Exists(b)); Assert.False(File.Exists(journal)); Assert.Equal(before, AdminFiles.Revision([a, b]));
            AdminFiles.Atomic(journal, AdminJson.Write(new List<BackupFile> { new() { Path = a, Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("recovered")) } }));
            AdminFiles.Recover(journal); Assert.Equal("recovered", File.ReadAllText(a));
            Assert.NotEqual(before, AdminFiles.Revision([a, b]));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task FrameHandlesLongUtf8FragmentedMessagesAndRejectsTruncation()
    {
        var text = string.Concat(Enumerable.Repeat("权限😀中文", 9000));
        using var output = new MemoryStream(); await AdminFrame.Write(output, text, default);
        using var input = new FragmentedStream(output.ToArray());
        Assert.Equal(text, await AdminFrame.Read(input, default));
        using var truncated = new MemoryStream(output.ToArray()[..^1]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => AdminFrame.Read(truncated, default));
        using var oversized = new MemoryStream([81, 71, 65, 49, 127, 255, 255, 255]);
        await Assert.ThrowsAsync<InvalidDataException>(() => AdminFrame.Read(oversized, default));
    }

    [Fact]
    public async Task ClientProbesOldPluginWithoutSendingMutation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var peer = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(); using var stream = connection.GetStream();
            var bytes = new byte[4096]; await stream.ReadAsync(bytes); await stream.WriteAsync(Encoding.UTF8.GetBytes("unknown command"));
        });
        var client = new SocketCommandClient(Options.Create(new SocketServerOptions { AuthToken = "test-token", Retries = 1 }), NullLogger<SocketCommandClient>.Instance);
        var result = await client.SendAdminAsync(new() { ConnectHost = "127.0.0.1", Port = port }, new() { Operation = "delete-member" }, default);
        Assert.False(result.Success); Assert.Equal("upgrade", result.Code); await peer;
        Assert.False(listener.Pending());
    }

    [Fact]
    public void NewPermissionIsOnlyAddedToOwnerPreset()
    {
        Assert.True(PanelPermission.Owner.HasFlag(PanelPermission.GameAdminManage));
        Assert.False(PanelPermission.Admin.HasFlag(PanelPermission.GameAdminManage));
        Assert.False(((PanelPermission)((1L << 11) - 1)).HasFlag(PanelPermission.GameAdminManage));
        Assert.Contains("game-admin.manage", PanelPermissions.ToKeys(PanelPermission.Owner));
    }

    [Fact]
    public void JsonRoundTripPreservesNullableOverrideAndHighPermissionKeys()
    {
        var value = Snapshot(); value.Catalog.Add(new() { Key = "Bit63", Name = "高位权限" }); value.Groups[0].Permissions.Add("Bit63");
        var restored = AdminJson.Read<AdminSnapshot>(AdminJson.Write(value));
        Assert.Null(restored.Members[0].PluginsEnabled); Assert.Contains("Bit63", restored.Groups[0].Permissions);
        AdminValidation.Validate(restored);
    }

    [Fact]
    public async Task ApiEnforcesIndependentPermissionAndAuditsOfflineMutation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qga-api-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = dir });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<WebPanelOptions>(o => o.DatabasePath = Path.Combine(dir, "test.db"));
        builder.Services.Configure<SocketServerOptions>(o => { o.Ports = []; o.AuthToken = "test-only-token"; });
        builder.Services.AddSingleton<PanelDatabase>(); builder.Services.AddSingleton<PanelAuthService>();
        builder.Services.AddSingleton<ServerRegistry>(); builder.Services.AddSingleton<SocketCommandClient>();
        await using var app = builder.Build(); app.MapGameAdminApi();
        var db = app.Services.GetRequiredService<PanelDatabase>(); db.EnsureCreated();
        var auth = app.Services.GetRequiredService<PanelAuthService>();
        await db.CreateAccountAsync(new() { Username = "viewer", DisplayName = "viewer", PasswordHash = PanelAuthService.HashPassword("test-password"), Permissions = PanelPermission.Admin });
        await db.CreateAccountAsync(new() { Username = "manager", DisplayName = "manager", PasswordHash = PanelAuthService.HashPassword("test-password"), Permissions = PanelPermission.GameAdminManage });
        var registry = app.Services.GetRequiredService<ServerRegistry>();
        registry.Register(new() { Name = "fixture", ConnectHost = "127.0.0.1", Port = 12345 }); registry.MarkOffline("127.0.0.1", 12345);
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/game-admin/servers")).StatusCode);
            var viewer = (await auth.LoginAsync("viewer", "test-password")).Session!;
            http.DefaultRequestHeaders.Authorization = new("Bearer", viewer.Token);
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/servers/1/game-admin")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("/api/servers/1/game-admin/changes", new AdminRequest())).StatusCode);
            var manager = (await auth.LoginAsync("manager", "test-password")).Session!;
            http.DefaultRequestHeaders.Authorization = new("Bearer", manager.Token);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/game-admin/servers")).StatusCode);
            http.DefaultRequestHeaders.Add("X-Qcha-Server", "wrong-server");
            Assert.Equal(HttpStatusCode.Conflict, (await http.GetAsync("/api/servers/1/game-admin")).StatusCode);
            http.DefaultRequestHeaders.Remove("X-Qcha-Server"); http.DefaultRequestHeaders.Add("X-Qcha-Server", "127.0.0.1:12345");
            // Fewer than 256 Ki characters can still exceed 256 KiB when encoded as UTF-8.
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/servers/1/game-admin/changes", new AdminRequest { Operation = "delete-member", Key = new string('中', 100 * 1024), Revision = "test", RequestId = "oversized-utf8" })).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.PostAsJsonAsync("/api/servers/1/game-admin/changes", new AdminRequest { Operation = "delete-member", Key = "123@steam", Revision = "test", RequestId = "offline-test" })).StatusCode);
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString); await connection.OpenAsync();
            using var query = connection.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM panel_audit WHERE action = 'game-admin.delete-member' AND success = 0";
            Assert.Equal(1L, await query.ExecuteScalarAsync());
        }
        finally { await app.StopAsync(); }
        // SQLite pooling can retain a Windows file handle until the pool is cleared.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task FramedClientAuthenticatesAndReceivesMoreThan4096Bytes()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var peer = Task.Run(async () =>
        {
            using (var probe = await listener.AcceptTcpClientAsync())
            {
                var buffer = new byte[4096]; using var stream = probe.GetStream(); await stream.ReadAsync(buffer); await stream.WriteAsync(Encoding.UTF8.GetBytes("QGA1"));
            }
            using var connection = await listener.AcceptTcpClientAsync(); using var data = connection.GetStream();
            var wire = await AdminFrame.Read(data, default);
            Assert.True(TcpAuthEnvelope.TryUnseal("test-token", wire, new(), out var payload, out _));
            Assert.StartsWith("game-admin&", payload);
            var snap = Snapshot(); for (int i = 0; i < 200; i++) snap.Members.Add(new() { UserId = $"user{i}@steam", Group = "staff" });
            await AdminFrame.Write(data, AdminJson.Write(new AdminReply { Success = true, Snapshot = snap }), default);
        });
        var client = new SocketCommandClient(Options.Create(new SocketServerOptions { AuthToken = "test-token", Retries = 1 }), NullLogger<SocketCommandClient>.Instance);
        var reply = await client.SendAdminAsync(new() { ConnectHost = "127.0.0.1", Port = port }, new(), default);
        Assert.True(reply.Success); Assert.Equal(201, reply.Snapshot.Members.Count); await peer;
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => base.ReadAsync(buffer, offset, Math.Min(count, 3), cancellationToken);
    }
}
