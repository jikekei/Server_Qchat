using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;

namespace Server.Qcat.Web;

/// <summary>
/// Web 面板认证服务：内置管理员初始化、密码哈希、会话管理。
/// 会话保存在内存中，程序重启即全部失效（与"启动随机重置密码"的策略一致）。
/// </summary>
public sealed class PanelAuthService
{
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    private readonly PanelDatabase _db;
    private readonly WebPanelOptions _options;
    private readonly ILogger<PanelAuthService> _log;
    private readonly ConcurrentDictionary<string, PanelSession> _sessions = new(StringComparer.Ordinal);

    public PanelAuthService(IOptions<WebPanelOptions> options, PanelDatabase db, ILogger<PanelAuthService> log)
    {
        _options = options.Value;
        _db = db;
        _log = log;
    }

    public TimeSpan SessionLifetime => TimeSpan.FromMinutes(Math.Max(5, _options.SessionMinutes));

    /// <summary>启动时确保内置管理员存在，并按配置随机重置其密码。</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        string username = string.IsNullOrWhiteSpace(_options.DefaultAdminUsername)
            ? "admin"
            : _options.DefaultAdminUsername.Trim();

        var existing = await _db.GetAccountByUsernameAsync(username, ct);

        if (existing is null)
        {
            string createdPassword = GeneratePassword();
            await _db.CreateAccountAsync(new PanelAccount
            {
                Username = username,
                PasswordHash = HashPassword(createdPassword),
                DisplayName = "内置管理员",
                Permissions = PanelPermission.Owner,
                IsEnabled = true,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
            }, ct);

            LogCredentials("已创建内置管理员账号", username, createdPassword);
            return;
        }

        if (!_options.ResetBuiltInPasswordOnStartup)
        {
            _log.LogInformation("内置管理员 [{Username}] 已存在，按配置未重置密码", username);
            return;
        }

        string password = GeneratePassword();
        await _db.UpdatePasswordAsync(existing.Id, HashPassword(password), ct);
        LogCredentials("已随机重置内置管理员密码", username, password);
    }

    private void LogCredentials(string title, string username, string password)
    {
        _log.LogWarning(
            "===== Web 面板 {Title} =====\n" +
            "  登录地址 : http://<本机IP>:{Port}/\n" +
            "  用户名   : {Username}\n" +
            "  密　码   : {Password}\n" +
            "  （密码每次启动随机生成；首次登录后请在「账号管理」中修改）\n" +
            "=========================================",
            title, _options.Port, username, password);
    }

    /// <summary>登录，成功返回会话。</summary>
    public async Task<(PanelSession? Session, string? Error)> LoginAsync(string? username, string? password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return (null, "用户名或密码不能为空");

        var account = await _db.GetAccountByUsernameAsync(username.Trim(), ct);

        // 统一错误信息，避免暴露用户名是否存在
        if (account is null || !VerifyPassword(password, account.PasswordHash))
            return (null, "用户名或密码错误");

        if (!account.IsEnabled)
            return (null, "账号已被禁用，请联系管理员");

        await _db.TouchLoginAsync(account.Id, ct);

        var session = new PanelSession
        {
            Token = GenerateToken(),
            AccountId = account.Id,
            Username = account.Username,
            DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
            Permissions = account.Permissions,
            ExpiresAt = DateTime.UtcNow.Add(SessionLifetime),
        };

        _sessions[session.Token] = session;
        _log.LogInformation("Web 面板登录成功: [{Username}]", session.Username);
        return (session, null);
    }

    /// <summary>校验并续期会话。</summary>
    public PanelSession? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(token, out var session))
            return null;

        if (session.IsExpired)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        return session;
    }

    public void Logout(string? token)
    {
        if (!string.IsNullOrEmpty(token) && _sessions.TryRemove(token, out var removed))
            _log.LogInformation("Web 面板登出: [{Username}]", removed.Username);
    }

    /// <summary>账号权限/启用状态变更后，同步或清除其在线会话。</summary>
    public void SyncSessions(long accountId, PanelPermission permissions, bool isEnabled)
    {
        foreach (var kvp in _sessions.Where(k => k.Value.AccountId == accountId).ToList())
        {
            if (!isEnabled)
                _sessions.TryRemove(kvp.Key, out _);
            else
                kvp.Value.Permissions = permissions;
        }
    }

    /// <summary>账号被删除后清除其全部会话。</summary>
    public void PurgeSessions(long accountId)
    {
        foreach (var kvp in _sessions.Where(k => k.Value.AccountId == accountId).ToList())
            _sessions.TryRemove(kvp.Key, out _);
    }

    /// <summary>修改密码后强制该账号重新登录。</summary>
    public void PurgeSessionsExcept(long accountId, string? keepToken)
    {
        foreach (var kvp in _sessions.Where(k => k.Value.AccountId == accountId && k.Key != keepToken).ToList())
            _sessions.TryRemove(kvp.Key, out _);
    }

    // ---------------- 密码与令牌 ----------------

    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || !string.Equals(parts[0], "pbkdf2", StringComparison.Ordinal))
                return false;

            int iterations = int.Parse(parts[1]);
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>生成易读但高强度的随机密码（去除易混淆字符）。</summary>
    public static string GeneratePassword(int length = 16)
    {
        if (length < 8)
            length = 8;

        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        return new string(chars);
    }

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
