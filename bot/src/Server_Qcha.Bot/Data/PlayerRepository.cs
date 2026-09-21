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

    /// <summary>
    /// 把 QQ 号写回玩家库的 QQ_ID 列（NapCat 模式专用，该列为整型）。
    /// </summary>
    public async Task<bool> BindQqAsync(string playerId, long qqId, CancellationToken ct)
    {
        if (!await PlayerExistsAsync(playerId, ct))
            return false;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var updateCmd = new MySqlCommand("UPDATE playerdata SET QQ_ID = @qq WHERE Id = @id;", conn);
        updateCmd.Parameters.AddWithValue("@id", playerId);
        updateCmd.Parameters.AddWithValue("@qq", qqId);
        await updateCmd.ExecuteNonQueryAsync(ct);

        return true;
    }

    /// <summary>判断玩家库中是否存在该 Steam64。</summary>
    public async Task<bool> PlayerExistsAsync(string playerId, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM playerdata WHERE Id = @id;", conn);
        cmd.Parameters.AddWithValue("@id", playerId);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>按 QQ 号查询玩家数据（NapCat 模式）。</summary>
    public Task<PlayerStats?> GetByQqAsync(long qqId, CancellationToken ct)
        => QuerySingleAsync("WHERE QQ_ID = @key", qqId.ToString(), qqId.ToString(), ct);

    /// <summary>按 Steam64 查询玩家数据（官方 QQ 模式通过绑定表解析出 Steam64 后使用）。</summary>
    public Task<PlayerStats?> GetByPlayerIdAsync(string playerId, CancellationToken ct)
        => QuerySingleAsync("WHERE Id = @key", playerId, playerId, ct);

    private async Task<PlayerStats?> QuerySingleAsync(string whereClause, string keyValue, string logKey, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // whereClause 仅来自本类内部的常量片段，不存在注入面
        string sql = $@"SELECT Id, PlayerName, ScpsKilled, PlayersKilled, PlayTime, Deaths, Admin, IsAdmin, QQ_ID
FROM playerdata
{whereClause}
LIMIT 1;";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@key", keyValue);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        try
        {
            return ReadStats(reader);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "解析 playerdata 行失败 (key={Key})", logKey);
            return null;
        }
    }

    private static PlayerStats ReadStats(MySqlDataReader reader)
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
}
