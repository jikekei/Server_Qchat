using System.Net;
using Server.Qcat.Bot;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin;
using Qchat.Security;
using Server.Qcat.Web;

namespace Server.Qcat.Tests;

public class SecurityFixTests
{
    [Fact]
    public void SharedSecret_RejectsEmptyAndRetiredDefault()
    {
        Assert.True(SharedSecretPolicy.IsRejected(null));
        Assert.True(SharedSecretPolicy.IsRejected("  "));
        Assert.True(SharedSecretPolicy.IsRejected(SharedSecretPolicy.RetiredDefaultToken));
        Assert.True(SharedSecretPolicy.IsRejected("  " + SharedSecretPolicy.RetiredDefaultToken + " "));
        Assert.False(SharedSecretPolicy.IsRejected("local-only-token-24chars"));
        Assert.Equal(SharedSecretPolicy.RetiredDefaultToken, TcpAuthEnvelope.RetiredDefaultToken);
        Assert.True(TcpAuthEnvelope.IsRejectedToken(TcpAuthEnvelope.RetiredDefaultToken));
    }

    [Fact]
    public void SharedSecret_FixedTimeEquals_MatchesExactText()
    {
        Assert.True(SharedSecretPolicy.FixedTimeEquals("abc", "abc"));
        Assert.False(SharedSecretPolicy.FixedTimeEquals("abc", "abd"));
        Assert.False(SharedSecretPolicy.FixedTimeEquals("abc", "abcd"));
        Assert.False(SharedSecretPolicy.FixedTimeEquals(null, "abc"));
    }

    [Fact]
    public void TcpAuth_RoundTrip_AndRejectsLegacyReplayAndSkew()
    {
        const string token = "unit-test-token-value";
        const string payload = "allrest";
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1_800_000_000);
        Assert.True(TcpAuthEnvelope.TrySeal(token, payload, now, "0123456789abcdef", out string wire, out string? sealError), sealError);
        Assert.StartsWith("v2|", wire);
        Assert.DoesNotContain(token + "||", wire);

        var cache = new TcpAuthNonceCache();
        Assert.True(TcpAuthEnvelope.TryUnseal(token, wire, cache, now, out string? opened, out string? openError), openError);
        Assert.Equal(payload, opened);

        Assert.False(TcpAuthEnvelope.TryUnseal(token, wire, cache, now, out _, out string? replayError));
        Assert.Contains("已使用", replayError);

        Assert.False(TcpAuthEnvelope.TryUnseal(token, token + "||" + payload, new TcpAuthNonceCache(), now, out _, out _));
        Assert.False(TcpAuthEnvelope.TryUnseal("other-token-value", wire, new TcpAuthNonceCache(), now, out _, out _));
        Assert.False(TcpAuthEnvelope.TryUnseal(token, wire, new TcpAuthNonceCache(), now.AddMinutes(10), out _, out _));
        Assert.False(TcpAuthEnvelope.TrySeal("", payload, out _, out _));
    }

    [Fact]
    public void TcpAuth_AllowsPayloadThatContainsSeparator()
    {
        const string token = "unit-test-token-value";
        const string payload = "bc&hello|world";
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1_800_000_100);
        Assert.True(TcpAuthEnvelope.TrySeal(token, payload, now, "abcdef0123456789", out string wire, out _));
        Assert.True(TcpAuthEnvelope.TryUnseal(token, wire, new TcpAuthNonceCache(), now, out string? opened, out _));
        Assert.Equal(payload, opened);
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("local-1", true)]
    [InlineData("A_b-9", true)]
    [InlineData("", false)]
    [InlineData("../etc", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("主服", false)]
    public void ServerId_OnlyAllowsAsciiToken(string id, bool ok)
    {
        Assert.Equal(ok, LocalServerGuard.IsSafeId(id));
    }

    [Fact]
    public void Executable_Allowlist_RejectsOtherPrograms()
    {
        Assert.Null(LocalServerGuard.ValidateExecutable(@"C:\Games\SCPSL.exe"));
        Assert.Null(LocalServerGuard.ValidateExecutable("/opt/scpsl/SCPSL.x86_64"));
        Assert.NotNull(LocalServerGuard.ValidateExecutable(@"C:\Windows\System32\cmd.exe"));
        Assert.NotNull(LocalServerGuard.ValidateExecutable(@"C:\Windows\System32\cmd"));
        Assert.NotNull(LocalServerGuard.ValidateExecutable("msiexec.exe"));
        Assert.NotNull(LocalServerGuard.ValidateExecutable("python"));
        Assert.NotNull(LocalServerGuard.ValidateExecutable("run.bat"));
        Assert.NotNull(LocalServerGuard.ValidateExecutable(""));
    }

    [Fact]
    public void DatabasePassword_ReusedOnlyWhenEndpointUnchanged()
    {
        const string saved = "Server=127.0.0.1;Port=3306;Database=game;Uid=app;Pwd=pa$$word;";
        Assert.True(DbConnectionSecret.TryResolve(
            "Server=127.0.0.1;Database=game;Uid=app;Pwd=******;",
            saved,
            out string sameHost,
            out _));
        Assert.Contains("Pwd=pa$$word;", sameHost);
        Assert.DoesNotContain("******", sameHost);

        Assert.False(DbConnectionSecret.TryResolve(
            "Server=203.0.113.8;Port=3306;Database=game;Uid=app;Pwd=******;",
            saved,
            out _,
            out string? error));
        Assert.Contains("主机或端口", error);

        Assert.True(DbConnectionSecret.TryResolve(
            "Server=203.0.113.8;Pwd=fresh-secret;",
            saved,
            out string replaced,
            out _));
        Assert.Contains("fresh-secret", replaced);
        Assert.DoesNotContain("pa$$word", replaced);
    }

    [Fact]
    public void OfficialSecret_ReusedOnlyWhenEndpointUnchanged()
    {
        var saved = new OfficialQqOptions
        {
            ApiBase = "https://api.bot.qq.com",
            AppId = "123",
            ClientSecret = "saved-secret",
            Sandbox = false,
        };

        Assert.True(OfficialSecretPolicy.CanReuse(saved, "https://api.bot.qq.com", "123", false));
        Assert.False(OfficialSecretPolicy.CanReuse(saved, "https://evil.example", "123", false));
        Assert.False(OfficialSecretPolicy.CanReuse(saved, "https://api.bot.qq.com", "999", false));
        Assert.False(OfficialSecretPolicy.CanReuse(saved, "https://api.bot.qq.com", "123", true));
    }

    [Fact]
    public void AdminCommands_RequireExplicitList()
    {
        Assert.False(BotAdminPolicy.IsListed(Array.Empty<long>(), 1001));
        Assert.False(BotAdminPolicy.IsListed(new long[] { 1001 }, 1002));
        Assert.True(BotAdminPolicy.IsListed(new long[] { 1001 }, 1001));
        Assert.False(BotAdminPolicy.IsListed(Array.Empty<string>(), "openid"));
        Assert.True(BotAdminPolicy.IsListed(new[] { "openid" }, "openid"));
        Assert.False(BotAdminPolicy.MayObserveGroup(new[] { "allowed" }, "other"));
        Assert.True(BotAdminPolicy.MayObserveGroup(new[] { "allowed" }, "allowed"));
        Assert.True(BotAdminPolicy.MayObserveGroup(Array.Empty<string>(), "any"));
    }

    [Fact]
    public void DaemonHost_LoopbackRejectsForeignHost()
    {
        Assert.True(DaemonHostGuard.IsAllowed(IPAddress.Loopback, "127.0.0.1:10090", "127.0.0.1"));
        Assert.True(DaemonHostGuard.IsAllowed(IPAddress.Loopback, "localhost:10090", "127.0.0.1"));
        Assert.False(DaemonHostGuard.IsAllowed(IPAddress.Loopback, "evil.example:10090", "127.0.0.1"));
        Assert.False(DaemonHostGuard.IsAllowed(IPAddress.Loopback, "", "127.0.0.1"));

        var lan = IPAddress.Parse("192.0.2.10");
        Assert.True(DaemonHostGuard.IsAllowed(lan, "192.0.2.10:10090", "192.0.2.10"));
        Assert.True(DaemonHostGuard.IsAllowed(lan, "panel.internal:10090", "panel.internal"));
        Assert.False(DaemonHostGuard.IsAllowed(lan, "evil.example", "panel.internal"));
    }
}
