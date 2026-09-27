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
    public void SharedSecret_FlagsEmptyAndLegacyDefaultAsWeak()
    {
        Assert.True(SharedSecretPolicy.IsWeak(null));
        Assert.True(SharedSecretPolicy.IsWeak("  "));
        Assert.True(SharedSecretPolicy.IsWeak(SharedSecretPolicy.LegacyDefaultToken));
        Assert.True(SharedSecretPolicy.IsWeak("  " + SharedSecretPolicy.LegacyDefaultToken + " "));
        Assert.False(SharedSecretPolicy.IsWeak("local-only-token-24chars"));
        Assert.True(SharedSecretPolicy.IsLegacyDefault(SharedSecretPolicy.LegacyDefaultToken));
        Assert.False(SharedSecretPolicy.IsLegacyDefault(""));
        Assert.Equal(SharedSecretPolicy.LegacyDefaultToken, TcpAuthEnvelope.LegacyDefaultToken);
        Assert.True(TcpAuthEnvelope.IsWeakToken(TcpAuthEnvelope.LegacyDefaultToken));
        Assert.True(TcpAuthEnvelope.IsWeakToken(""));
        Assert.True(TcpAuthEnvelope.IsWeakToken(null!));
        Assert.False(TcpAuthEnvelope.IsWeakToken("local-only-token-24chars"));
    }

    [Fact]
    public void SharedSecret_WeakTokensProduceWarningInsteadOfRejection()
    {
        var problems = SharedSecretPolicy.DescribeWeakSecrets(
            checkAuthToken: true,
            authToken: SharedSecretPolicy.LegacyDefaultToken,
            checkDaemonToken: true,
            daemonToken: "");
        Assert.Equal(2, problems.Count);
        Assert.Contains("SocketServer:AuthToken", problems[0]);
        Assert.Contains(SharedSecretPolicy.LegacyDefaultToken, problems[0]);
        Assert.Contains("LocalAdmin:DaemonToken", problems[1]);
        Assert.Contains("为空", problems[1]);

        var banner = string.Join("\n", SharedSecretPolicy.BuildStartupWarning(problems));
        Assert.Contains("安全警告", banner);
        Assert.Contains("安全风险", banner);
        Assert.Contains("尽快修改", banner);
        Assert.Contains("继续启动", banner);
        Assert.DoesNotContain("拒绝启动", banner);

        // 写警告只输出、不抛异常、不等待按键，调用方随后继续启动。
        var original = Console.Out;
        try
        {
            using var sw = new StringWriter();
            Console.SetOut(sw);
            SharedSecretPolicy.WriteStartupWarning(problems);
            Assert.Contains("SocketServer:AuthToken", sw.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Empty(SharedSecretPolicy.DescribeWeakSecrets(true, "local-only-token-24chars", true, "another-local-token-24c"));
        Assert.Empty(SharedSecretPolicy.DescribeWeakSecrets(false, "", false, ""));
    }

    [Fact]
    public void PluginWarning_OnlyForWeakToken()
    {
        string[] legacy = TcpAuthEnvelope.BuildWeakTokenWarning(TcpAuthEnvelope.LegacyDefaultToken);
        Assert.NotEmpty(legacy);
        string text = string.Join("\n", legacy);
        Assert.Contains("auth_token", text);
        Assert.Contains(TcpAuthEnvelope.LegacyDefaultToken, text);
        Assert.Contains("安全风险", text);
        Assert.Contains("SocketServer:AuthToken", text);
        Assert.DoesNotContain("拒绝", text);

        Assert.Contains("为空", string.Join("\n", TcpAuthEnvelope.BuildWeakTokenWarning("")));
        Assert.Empty(TcpAuthEnvelope.BuildWeakTokenWarning("local-only-token-24chars"));
    }

    [Theory]
    [InlineData("QchaSecret_123")]
    [InlineData("")]
    public void TcpAuth_WeakTokenStillWorksEndToEnd(string token)
    {
        // 默认密钥或空密钥只告警，v2 HMAC 封套照常工作，双方密钥一致即可通信。
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1_800_000_200);
        Assert.True(TcpAuthEnvelope.TrySeal(token, "allrest", now, "0123456789abcdef", out string wire, out string? sealError), sealError);
        Assert.StartsWith("v2|", wire);
        Assert.True(TcpAuthEnvelope.TryUnseal(token, wire, new TcpAuthNonceCache(), now, out string? opened, out string? openError), openError);
        Assert.Equal("allrest", opened);
        Assert.False(TcpAuthEnvelope.TryUnseal("local-only-token-24chars", wire, new TcpAuthNonceCache(), now, out _, out _));
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
        Assert.False(TcpAuthEnvelope.TryUnseal("", wire, new TcpAuthNonceCache(), now, out _, out _));
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
    public void AdminCommands_NonEmptyListIsAuthoritative()
    {
        Assert.False(BotAdminPolicy.IsListed(Array.Empty<long>(), 1001));
        Assert.False(BotAdminPolicy.IsListed(new long[] { 1001 }, 1002));
        Assert.True(BotAdminPolicy.IsListed(new long[] { 1001 }, 1001));
        Assert.False(BotAdminPolicy.IsListed(Array.Empty<string>(), "openid"));
        Assert.True(BotAdminPolicy.IsListed(new[] { "openid" }, "openid"));

        // 配置了名单：群主/管理员角色、通知名单都不再授予管理权限。
        long[] admins = { 1001 };
        Assert.True(BotAdminPolicy.IsNapCatGroupAdmin(admins, 1001, isGroupOwnerOrAdmin: false));
        Assert.False(BotAdminPolicy.IsNapCatGroupAdmin(admins, 1002, isGroupOwnerOrAdmin: true));
        Assert.True(BotAdminPolicy.IsNapCatPrivateAdmin(admins, new long[] { 2002 }, 1001));
        Assert.False(BotAdminPolicy.IsNapCatPrivateAdmin(admins, new long[] { 2002 }, 2002));

        string[] openIds = { "admin-openid" };
        Assert.True(BotAdminPolicy.IsOfficialGroupAdmin(openIds, "admin-openid", "member"));
        Assert.False(BotAdminPolicy.IsOfficialGroupAdmin(openIds, "other", "owner"));
        Assert.True(BotAdminPolicy.IsOfficialC2CAdmin(openIds, "admin-openid"));
        Assert.False(BotAdminPolicy.IsOfficialC2CAdmin(openIds, "other"));
        Assert.False(BotAdminPolicy.MayObserveGroup(new[] { "allowed" }, "other"));
        Assert.True(BotAdminPolicy.MayObserveGroup(new[] { "allowed" }, "allowed"));
        Assert.True(BotAdminPolicy.MayObserveGroup(Array.Empty<string>(), "any"));
    }

    [Fact]
    public void AdminCommands_EmptyListFallsBackToOriginalLogic()
    {
        // 名单为空时沿用原有判定，管理指令不会整体失效。
        Assert.False(BotAdminPolicy.HasList(Array.Empty<long>()));
        Assert.False(BotAdminPolicy.HasList((long[]?)null));
        Assert.False(BotAdminPolicy.HasList(Array.Empty<string>()));

        // NapCat 群聊：群主/管理员可用，普通成员不可用。
        Assert.True(BotAdminPolicy.IsNapCatGroupAdmin(Array.Empty<long>(), 1001, isGroupOwnerOrAdmin: true));
        Assert.False(BotAdminPolicy.IsNapCatGroupAdmin(Array.Empty<long>(), 1001, isGroupOwnerOrAdmin: false));
        Assert.True(BotAdminPolicy.IsNapCatGroupAdmin(null, 1001, isGroupOwnerOrAdmin: true));

        // NapCat 私聊：通知名单内的用户可用（私聊入口本身只对通知名单开放）。
        Assert.True(BotAdminPolicy.IsNapCatPrivateAdmin(Array.Empty<long>(), new long[] { 2002 }, 2002));
        Assert.False(BotAdminPolicy.IsNapCatPrivateAdmin(Array.Empty<long>(), new long[] { 2002 }, 3003));
        Assert.False(BotAdminPolicy.IsNapCatPrivateAdmin(Array.Empty<long>(), Array.Empty<long>(), 2002));

        // 官方群聊：member_role 为 admin/owner 可用。
        Assert.True(BotAdminPolicy.IsOfficialGroupAdmin(Array.Empty<string>(), "u1", "admin"));
        Assert.True(BotAdminPolicy.IsOfficialGroupAdmin(Array.Empty<string>(), "u1", "OWNER"));
        Assert.False(BotAdminPolicy.IsOfficialGroupAdmin(Array.Empty<string>(), "u1", "member"));
        Assert.False(BotAdminPolicy.IsOfficialGroupAdmin(Array.Empty<string>(), "u1", null));

        // 官方单聊：旧版本在名单为空时就不放行，保持一致。
        Assert.False(BotAdminPolicy.IsOfficialC2CAdmin(Array.Empty<string>(), "u1"));
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
