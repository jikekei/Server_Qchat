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
    private readonly IOptionsMonitor<MySqlOptions> _optsMonitor;
    private readonly ILogger<PlayerRepository> _log;

    public PlayerRepository(IOptionsMonitor<MySqlOptions> optsMonitor, ILogger<PlayerRepository> log)
    {
        _optsMonitor = optsMonitor;
        _log = log;
    }

    private MySqlConnection CreateConnection() => new MySqlConnection(_optsMonitor.CurrentValue.ConnectionString);

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
        cmd.Parameters.AddWithValue("@qq", qqId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        try
        {
            int idOrd = reader.GetOrdinal("Id");
            string id = reader.IsDBNull(idOrd) ? "" : reader.GetString(idOrd);

            int nameOrd = reader.GetOrdinal("PlayerName");
            string name = reader.IsDBNull(nameOrd) ? "" : reader.GetString(nameOrd);

            int scpsOrd = reader.GetOrdinal("ScpsKilled");
            int scpsKilled = reader.IsDBNull(scpsOrd) ? 0 : Convert.ToInt32(reader.GetValue(scpsOrd));

            int playersOrd = reader.GetOrdinal("PlayersKilled");
            int playersKilled = reader.IsDBNull(playersOrd) ? 0 : Convert.ToInt32(reader.GetValue(playersOrd));

            int timeOrd = reader.GetOrdinal("PlayTime");
            int playTime = reader.IsDBNull(timeOrd) ? 0 : Convert.ToInt32(reader.GetValue(timeOrd));

            int deathsOrd = reader.GetOrdinal("Deaths");
            int deaths = reader.IsDBNull(deathsOrd) ? 0 : Convert.ToInt32(reader.GetValue(deathsOrd));

            int adminOrd = reader.GetOrdinal("Admin");
            string? admin = reader.IsDBNull(adminOrd) ? null : reader.GetString(adminOrd);

            int isAdminOrd = reader.GetOrdinal("IsAdmin");
            bool isAdmin = false;
            if (!reader.IsDBNull(isAdminOrd))
            {
                object val = reader.GetValue(isAdminOrd);
                if (val is bool b)
                    isAdmin = b;
                else if (val is sbyte or byte or int or long or short)
                    isAdmin = Convert.ToInt64(val) != 0;
                else if (bool.TryParse(val.ToString(), out bool pb))
                    isAdmin = pb;
                else if (long.TryParse(val.ToString(), out long pl))
                    isAdmin = pl != 0;
            }

            int qqOrd = reader.GetOrdinal("QQ_ID");
            long? qq = null;
            if (!reader.IsDBNull(qqOrd))
            {
                object val = reader.GetValue(qqOrd);
                if (val is long l)
                    qq = l;
                else if (val is int i)
                    qq = i;
                else if (long.TryParse(val.ToString(), out long parsed))
                    qq = parsed;
            }

            return new PlayerStats(id, name, scpsKilled, playersKilled, playTime, deaths, admin, isAdmin, qq);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse playerdata row for QQ={QqId}", qqId);
            return null;
        }
    }
}

