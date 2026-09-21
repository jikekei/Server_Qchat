using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;

namespace Server.Qcat.Web;

/// <summary>
/// Web 面板本地账号库（SQLite 单文件）。
/// 所有时间以 UTC + ISO-8601 文本存储，避免时区歧义。
/// </summary>
public sealed class PanelDatabase
{
    private readonly string _connectionString;
    private readonly ILogger<PanelDatabase> _log;

    public PanelDatabase(IOptions<WebPanelOptions> options, IHostEnvironment env, ILogger<PanelDatabase> log)
    {
        _log = log;

        string path = options.Value.DatabasePath;
        if (string.IsNullOrWhiteSpace(path))
            path = "data/panel.db";
        if (!Path.IsPathRooted(path))
            path = Path.Combine(env.ContentRootPath, path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _log.LogInformation("Web 面板账号库路径: {Path}", path);
    }

    /// <summary>
    /// 面板 SQLite 库的连接串。供其它需要复用同一文件的功能使用
    /// （例如官方 QQ 模式下用 OpenID 建立的玩家绑定表）。
    /// </summary>
    public string ConnectionString => _connectionString;

    /// <summary>建表（幂等）。</summary>
    public void EnsureCreated()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS panel_accounts (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                username      TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                password_hash TEXT    NOT NULL,
                display_name  TEXT    NOT NULL DEFAULT '',
                permissions   INTEGER NOT NULL DEFAULT 0,
                is_enabled    INTEGER NOT NULL DEFAULT 1,
                is_builtin    INTEGER NOT NULL DEFAULT 0,
                created_at    TEXT    NOT NULL,
                last_login_at TEXT    NULL
            );

            CREATE TABLE IF NOT EXISTS panel_audit (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT    NOT NULL,
                username   TEXT    NOT NULL DEFAULT '',
                action     TEXT    NOT NULL DEFAULT '',
                target     TEXT    NOT NULL DEFAULT '',
                detail     TEXT    NOT NULL DEFAULT '',
                success    INTEGER NOT NULL DEFAULT 1
            );

            CREATE INDEX IF NOT EXISTS idx_panel_audit_created ON panel_audit (created_at DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    // ---------------- 账号 ----------------

    private const string AccountColumns =
        "id, username, password_hash, display_name, permissions, is_enabled, is_builtin, created_at, last_login_at";

    public async Task<PanelAccount?> GetAccountByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {AccountColumns} FROM panel_accounts WHERE username = @u LIMIT 1;";
        cmd.Parameters.AddWithValue("@u", username);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAccount(reader) : null;
    }

    public async Task<PanelAccount?> GetAccountByIdAsync(long id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {AccountColumns} FROM panel_accounts WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAccount(reader) : null;
    }

    public async Task<List<PanelAccount>> ListAccountsAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {AccountColumns} FROM panel_accounts ORDER BY id;";

        var list = new List<PanelAccount>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadAccount(reader));
        return list;
    }

    public async Task<long> CreateAccountAsync(PanelAccount account, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO panel_accounts (username, password_hash, display_name, permissions, is_enabled, is_builtin, created_at)
            VALUES (@u, @p, @d, @perm, @en, @bi, @created);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@u", account.Username);
        cmd.Parameters.AddWithValue("@p", account.PasswordHash);
        cmd.Parameters.AddWithValue("@d", account.DisplayName);
        cmd.Parameters.AddWithValue("@perm", (long)account.Permissions);
        cmd.Parameters.AddWithValue("@en", account.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@bi", account.IsBuiltIn ? 1 : 0);
        cmd.Parameters.AddWithValue("@created", ToDb(account.CreatedAt));

        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task UpdateAccountAsync(long id, string displayName, PanelPermission permissions, bool isEnabled, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE panel_accounts
               SET display_name = @d, permissions = @perm, is_enabled = @en
             WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@d", displayName);
        cmd.Parameters.AddWithValue("@perm", (long)permissions);
        cmd.Parameters.AddWithValue("@en", isEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdatePasswordAsync(long id, string passwordHash, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE panel_accounts SET password_hash = @p WHERE id = @id;";
        cmd.Parameters.AddWithValue("@p", passwordHash);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeleteAccountAsync(long id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM panel_accounts WHERE id = @id AND is_builtin = 0;";
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task TouchLoginAsync(long id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE panel_accounts SET last_login_at = @t WHERE id = @id;";
        cmd.Parameters.AddWithValue("@t", ToDb(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------------- 审计 ----------------

    public async Task AddAuditAsync(PanelAuditEntry entry, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO panel_audit (created_at, username, action, target, detail, success)
            VALUES (@t, @u, @a, @tg, @d, @s);
            """;
        cmd.Parameters.AddWithValue("@t", ToDb(entry.CreatedAt));
        cmd.Parameters.AddWithValue("@u", entry.Username);
        cmd.Parameters.AddWithValue("@a", entry.Action);
        cmd.Parameters.AddWithValue("@tg", entry.Target);
        cmd.Parameters.AddWithValue("@d", entry.Detail);
        cmd.Parameters.AddWithValue("@s", entry.Success ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<PanelAuditEntry>> ListAuditAsync(int limit, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, created_at, username, action, target, detail, success
              FROM panel_audit
             ORDER BY id DESC
             LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 1000));

        var list = new List<PanelAuditEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PanelAuditEntry
            {
                Id = reader.GetInt64(0),
                CreatedAt = FromDb(reader.GetString(1)),
                Username = reader.GetString(2),
                Action = reader.GetString(3),
                Target = reader.GetString(4),
                Detail = reader.GetString(5),
                Success = reader.GetInt64(6) != 0,
            });
        }
        return list;
    }

    // ---------------- 辅助 ----------------

    private static PanelAccount ReadAccount(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Username = reader.GetString(1),
        PasswordHash = reader.GetString(2),
        DisplayName = reader.GetString(3),
        Permissions = (PanelPermission)reader.GetInt64(4),
        IsEnabled = reader.GetInt64(5) != 0,
        IsBuiltIn = reader.GetInt64(6) != 0,
        CreatedAt = FromDb(reader.GetString(7)),
        LastLoginAt = reader.IsDBNull(8) ? null : FromDb(reader.GetString(8)),
    };

    private static string ToDb(DateTime value) => value.ToUniversalTime().ToString("O");

    private static DateTime FromDb(string value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : DateTime.UtcNow;
}
