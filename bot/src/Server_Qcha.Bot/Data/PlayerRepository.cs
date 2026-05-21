using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Server.Qcat.Configuration;

namespace Server.Qcat.Data;

public sealed record PlayerStats(
    string Id,
    string PlayerName,
    int ScpsKilled,
    int PlayersKilled,
    int PlayTimeSeconds,
    int Deaths,
    string? AdminNote,
    bool IsAdmin,
    long? QqId
);

public sealed class PlayerRepository
{
    private readonly MySqlOptions _opts;
    private readonly ILogger<PlayerRepository> _log;

    public PlayerRepository(IOptions<MySqlOptions> opts, ILogger<PlayerRepository> log)
    {
        _opts = opts.Value;
        _log = log;
    }

    private MySqlConnection CreateConnection() => new MySqlConnection(_opts.ConnectionString);

    public async Task<bool> BindQqAsync(string playerId, long qqId, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Ensure player exists.
        await using (var checkCmd = new MySqlCommand("SELECT COUNT(*) FROM playerdata WHERE Id = @id;", conn))
        {
            checkCmd.Parameters.AddWithValue("@id", playerId);
            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct));
            if (count <= 0)
                return false;
        }

        await using (var updateCmd = new MySqlCommand("UPDATE playerdata SET QQ_ID = @qq WHERE Id = @id;", conn))
        {
            updateCmd.Parameters.AddWithValue("@id", playerId);
            updateCmd.Parameters.AddWithValue("@qq", qqId);
            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        return true;
    }

    public async Task<PlayerStats?> GetByQqAsync(long qqId, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        const string sql = @"SELECT Id, PlayerName, ScpsKilled, PlayersKilled, PlayTime, Deaths, Admin, IsAdmin, QQ_ID
FROM playerdata
WHERE QQ_ID = @qq
LIMIT 1;";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@qq", qqId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        try
        {
            string id = reader.GetString("Id");
            string name = reader.GetString("PlayerName");
            int scpsKilled = reader.GetInt32("ScpsKilled");
            int playersKilled = reader.GetInt32("PlayersKilled");
            int playTime = reader.GetInt32("PlayTime");
            int deaths = reader.GetInt32("Deaths");
            string? admin = reader.IsDBNull(reader.GetOrdinal("Admin")) ? null : reader.GetString("Admin");
            bool isAdmin = reader.GetBoolean("IsAdmin");
            long? qq = reader.IsDBNull(reader.GetOrdinal("QQ_ID")) ? null : reader.GetInt64("QQ_ID");

            return new PlayerStats(id, name, scpsKilled, playersKilled, playTime, deaths, admin, isAdmin, qq);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse playerdata row for QQ={QqId}", qqId);
            return null;
        }
    }
}

