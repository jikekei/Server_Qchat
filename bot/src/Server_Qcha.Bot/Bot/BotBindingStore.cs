using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Server.Qcat.Web;

namespace Server.Qcat.Bot;

/// <summary>
/// 跨平台的玩家身份绑定表（存放在面板 SQLite 库中）。
///
/// 为什么需要它：SCPSL 玩家库的 <c>QQ_ID</c> 列是整型，只能存真实 QQ 号。
/// 而 QQ 官方平台出于隐私保护只下发 OpenID（形如 <c>1A2B3C…</c> 的字符串），
/// 无法写回该列，因此官方模式下改用这张独立表完成「OpenID → Steam64」的绑定，
/// 既不改动既有玩家库表结构，也让两种模式可以各自独立地完成绑定。
/// </summary>
public sealed class BotBindingStore
{
    private readonly string _connectionString;
    private readonly ILogger<BotBindingStore> _log;
    private readonly object _initLock = new();
    private bool _initialized;

    public BotBindingStore(PanelDatabase panel, ILogger<BotBindingStore> log)
    {
        _connectionString = panel.ConnectionString;
        _log = log;
    }

    private SqliteConnection Open()
    {
        EnsureTable();

        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void EnsureTable()
    {
        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS bot_bindings (
                    platform    TEXT NOT NULL,
                    external_id TEXT NOT NULL,
                    player_id   TEXT NOT NULL,
                    created_at  TEXT NOT NULL,
                    PRIMARY KEY (platform, external_id)
                );

                CREATE INDEX IF NOT EXISTS idx_bot_bindings_player ON bot_bindings (player_id);
                """;
            cmd.ExecuteNonQuery();

            _initialized = true;
            _log.LogInformation("玩家绑定表 bot_bindings 已就绪");
        }
    }

    /// <summary>写入或覆盖一条绑定。</summary>
    public async Task<bool> BindAsync(BotPlatform platform, string externalId, string playerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(playerId))
            return false;

        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO bot_bindings (platform, external_id, player_id, created_at)
            VALUES (@p, @e, @pid, @t)
            ON CONFLICT (platform, external_id) DO UPDATE SET player_id = @pid, created_at = @t;
            """;
        cmd.Parameters.AddWithValue("@p", platform.ToString());
        cmd.Parameters.AddWithValue("@e", externalId);
        cmd.Parameters.AddWithValue("@pid", playerId);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>查询该平台身份绑定的玩家 ID；未绑定时返回 null。</summary>
    public async Task<string?> ResolveAsync(BotPlatform platform, string externalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            return null;

        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT player_id FROM bot_bindings WHERE platform = @p AND external_id = @e LIMIT 1;";
        cmd.Parameters.AddWithValue("@p", platform.ToString());
        cmd.Parameters.AddWithValue("@e", externalId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>解除绑定。</summary>
    public async Task<bool> UnbindAsync(BotPlatform platform, string externalId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM bot_bindings WHERE platform = @p AND external_id = @e;";
        cmd.Parameters.AddWithValue("@p", platform.ToString());
        cmd.Parameters.AddWithValue("@e", externalId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
