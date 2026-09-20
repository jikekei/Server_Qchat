using System.Data;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Server.Qcat.Configuration;

namespace Server.Qcat.Data;

public sealed record BanRecord(
    string Id,
    string PlayerIP,
    DateTime UnbanTime,
    string AdminId,
    string AdminName,
    string Reason,
    bool IsActive);

public sealed record DatabaseSummary(
    bool IsConnected,
    string? Error,
    bool PlayerDataExists,
    bool BanPlayerDataExists,
    int TotalPlayers,
    int TotalBans,
    int ActiveBans,
    long TotalPlayTimeSeconds,
    int TotalKills);

public sealed class GameDbRepository
{
    private readonly IOptionsMonitor<MySqlOptions> _optsMonitor;
    private readonly ILogger<GameDbRepository> _log;

    public GameDbRepository(IOptionsMonitor<MySqlOptions> optsMonitor, ILogger<GameDbRepository> log)
    {
        _optsMonitor = optsMonitor;
        _log = log;
    }

    public string CurrentConnectionString => _optsMonitor.CurrentValue.ConnectionString ?? "";

    private MySqlConnection CreateConnection(string? connectionString = null)
    {
        string cs = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : CurrentConnectionString;

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("未配置 MySQL 数据库连接串（MySql:ConnectionString 为空）");

        return new MySqlConnection(cs);
    }

    // ==================== 连接测试与表维护 ====================

    public async Task<(bool Success, string? Error, long ElapsedMs, bool PlayerDataExists, bool BanPlayerDataExists)> TestConnectionAsync(
        string? connectionString = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = CreateConnection(connectionString);
            await conn.OpenAsync(ct);

            // 检查表是否存在
            bool playerExists = false;
            bool banExists = false;

            const string checkSql = @"
                SELECT TABLE_NAME 
                FROM information_schema.TABLES 
                WHERE TABLE_SCHEMA = DATABASE() 
                  AND TABLE_NAME IN ('PlayerData', 'BanPlayerData');";

            await using var cmd = new MySqlCommand(checkSql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string tableName = reader.GetString(0);
                if (string.Equals(tableName, "PlayerData", StringComparison.OrdinalIgnoreCase))
                    playerExists = true;
                else if (string.Equals(tableName, "BanPlayerData", StringComparison.OrdinalIgnoreCase))
                    banExists = true;
            }

            sw.Stop();
            return (true, null, sw.ElapsedMilliseconds, playerExists, banExists);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, ex.Message, sw.ElapsedMilliseconds, false, false);
        }
    }

    public async Task InitializeTablesAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // 1. 创建 PlayerData 表
        const string createPlayerSql = @"
        CREATE TABLE IF NOT EXISTS PlayerData (
            Id VARCHAR(255) PRIMARY KEY,
            PlayerName VARCHAR(255) NOT NULL,
            ScpsKilled INT NOT NULL DEFAULT 0,
            PlayersKilled INT NOT NULL DEFAULT 0,
            PlayTime INT NOT NULL DEFAULT 0,
            Deaths INT NOT NULL DEFAULT 0,
            Admin VARCHAR(255) NULL,
            IsAdmin BOOLEAN NOT NULL DEFAULT FALSE,
            QQ_ID BIGINT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
        await using (var cmd = new MySqlCommand(createPlayerSql, conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 兼容旧 PlayerData 表可能缺失 QQ_ID 字段的情况
        try
        {
            const string checkQqColSql = @"
                SELECT COUNT(*) 
                FROM information_schema.COLUMNS 
                WHERE TABLE_SCHEMA = DATABASE() 
                  AND TABLE_NAME = 'PlayerData' 
                  AND COLUMN_NAME = 'QQ_ID';";
            await using var checkCmd = new MySqlCommand(checkQqColSql, conn);
            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct));
            if (count == 0)
            {
                await using var addColCmd = new MySqlCommand("ALTER TABLE PlayerData ADD COLUMN QQ_ID BIGINT NULL;", conn);
                await addColCmd.ExecuteNonQueryAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "检查或添加 PlayerData.QQ_ID 字段时出现异常: {Error}", ex.Message);
        }

        // 2. 创建 BanPlayerData 表
        const string createBanSql = @"
        CREATE TABLE IF NOT EXISTS BanPlayerData (
            Id VARCHAR(255) PRIMARY KEY,
            PlayerIP VARCHAR(255) NOT NULL,
            UnbanTime DATETIME NOT NULL,
            AdminId VARCHAR(255) NOT NULL,
            AdminName VARCHAR(255) NOT NULL,
            Reason TEXT NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
        await using (var cmd = new MySqlCommand(createBanSql, conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        _log.LogInformation("数据库表结构初始化/检查完成 (PlayerData, BanPlayerData)");
    }

    public async Task<DatabaseSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CurrentConnectionString))
        {
            return new DatabaseSummary(false, "未配置数据库连接串", false, false, 0, 0, 0, 0, 0);
        }

        var (success, error, _, playerExists, banExists) = await TestConnectionAsync(null, ct);
        if (!success)
        {
            return new DatabaseSummary(false, error, false, false, 0, 0, 0, 0, 0);
        }

        int totalPlayers = 0;
        long totalPlayTime = 0;
        int totalKills = 0;
        int totalBans = 0;
        int activeBans = 0;

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            if (playerExists)
            {
                const string pStatsSql = @"
                    SELECT COUNT(*), COALESCE(SUM(PlayTime), 0), COALESCE(SUM(PlayersKilled + ScpsKilled), 0) 
                    FROM PlayerData;";
                await using var pCmd = new MySqlCommand(pStatsSql, conn);
                await using var reader = await pCmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    totalPlayers = reader.GetInt32(0);
                    totalPlayTime = reader.GetInt64(1);
                    totalKills = Convert.ToInt32(reader.GetValue(2));
                }
            }

            if (banExists)
            {
                const string bStatsSql = @"
                    SELECT COUNT(*), SUM(CASE WHEN UnbanTime > NOW() THEN 1 ELSE 0 END) 
                    FROM BanPlayerData;";
                await using var bCmd = new MySqlCommand(bStatsSql, conn);
                await using var reader = await bCmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    totalBans = reader.GetInt32(0);
                    activeBans = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                }
            }

            return new DatabaseSummary(true, null, playerExists, banExists, totalPlayers, totalBans, activeBans, totalPlayTime, totalKills);
        }
        catch (Exception ex)
        {
            return new DatabaseSummary(false, ex.Message, playerExists, banExists, 0, 0, 0, 0, 0);
        }
    }

    // ==================== 玩家统计 (PlayerData) ====================

    public async Task<(IReadOnlyList<PlayerStats> Items, int Total)> GetPlayersAsync(
        string? search, int page, int pageSize, string? sortBy, bool sortDesc, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int offset = (page - 1) * pageSize;

        string where = "";
        if (!string.IsNullOrWhiteSpace(search))
        {
            where = "WHERE Id LIKE @q OR PlayerName LIKE @q";
        }

        string orderCol = sortBy?.ToLowerInvariant() switch
        {
            "playername" => "PlayerName",
            "scpskilled" => "ScpsKilled",
            "playerskilled" => "PlayersKilled",
            "playtime" or "playtimeseconds" => "PlayTime",
            "deaths" => "Deaths",
            _ => "PlayTime"
        };
        string orderDir = sortDesc ? "DESC" : "ASC";

        string countSql = $"SELECT COUNT(*) FROM PlayerData {where};";
        await using var countCmd = new MySqlCommand(countSql, conn);
        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@q", $"%{search.Trim()}%");

        int total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        string selectSql = $@"
            SELECT Id, PlayerName, ScpsKilled, PlayersKilled, PlayTime, Deaths, Admin, IsAdmin, QQ_ID
            FROM PlayerData
            {where}
            ORDER BY {orderCol} {orderDir}
            LIMIT {pageSize} OFFSET {offset};";

        await using var selectCmd = new MySqlCommand(selectSql, conn);
        if (!string.IsNullOrWhiteSpace(search))
            selectCmd.Parameters.AddWithValue("@q", $"%{search.Trim()}%");

        var list = new List<PlayerStats>();
        await using var reader = await selectCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadPlayerStats(reader));
        }

        return (list, total);
    }

    public async Task<PlayerStats?> GetPlayerByIdAsync(string id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        const string sql = @"SELECT Id, PlayerName, ScpsKilled, PlayersKilled, PlayTime, Deaths, Admin, IsAdmin, QQ_ID FROM PlayerData WHERE Id = @id LIMIT 1;";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadPlayerStats(reader);
    }

    public async Task SavePlayerAsync(PlayerStats player, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        const string sql = @"
            INSERT INTO PlayerData 
            (Id, PlayerName, ScpsKilled, PlayersKilled, PlayTime, Deaths, Admin, IsAdmin, QQ_ID) 
            VALUES 
            (@Id, @PlayerName, @ScpsKilled, @PlayersKilled, @PlayTime, @Deaths, @Admin, @IsAdmin, @QQ_ID)
            ON DUPLICATE KEY UPDATE
                PlayerName = @PlayerName,
                ScpsKilled = @ScpsKilled,
                PlayersKilled = @PlayersKilled,
                PlayTime = @PlayTime,
                Deaths = @Deaths,
                Admin = @Admin,
                IsAdmin = @IsAdmin,
                QQ_ID = @QQ_ID;";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", player.Id);
        cmd.Parameters.AddWithValue("@PlayerName", player.PlayerName);
        cmd.Parameters.AddWithValue("@ScpsKilled", player.ScpsKilled);
        cmd.Parameters.AddWithValue("@PlayersKilled", player.PlayersKilled);
        cmd.Parameters.AddWithValue("@PlayTime", player.PlayTimeSeconds);
        cmd.Parameters.AddWithValue("@Deaths", player.Deaths);
        cmd.Parameters.AddWithValue("@Admin", (object?)player.AdminNote ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsAdmin", player.IsAdmin);
        cmd.Parameters.AddWithValue("@QQ_ID", (object?)player.QqId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeletePlayerAsync(string id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        const string sql = "DELETE FROM PlayerData WHERE Id = @Id;";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        int rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<IReadOnlyList<PlayerStats>> GetPlayerRankingsAsync(string metric, int limit = 10, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        limit = Math.Clamp(limit, 1, 50);
        string orderCol = metric.ToLowerInvariant() switch
        {
            "scps" => "ScpsKilled",
            "kills" or "players" => "PlayersKilled",
            _ => "PlayTime"
        };

        string sql = $@"
            SELECT Id, PlayerName, ScpsKilled, PlayersKilled, PlayTime, Deaths, Admin, IsAdmin, QQ_ID
            FROM PlayerData
            ORDER BY {orderCol} DESC
            LIMIT {limit};";

        await using var cmd = new MySqlCommand(sql, conn);
        var list = new List<PlayerStats>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadPlayerStats(reader));
        }
        return list;
    }

    // ==================== 封禁管理 (BanPlayerData) ====================

    public async Task<(IReadOnlyList<BanRecord> Items, int Total)> GetBansAsync(
        string? search, bool? activeOnly, int page, int pageSize, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        int offset = (page - 1) * pageSize;

        var whereClauses = new List<string>();
        if (activeOnly == true)
            whereClauses.Add("UnbanTime > NOW()");
        if (!string.IsNullOrWhiteSpace(search))
            whereClauses.Add("(Id LIKE @q OR PlayerIP LIKE @q OR AdminName LIKE @q OR Reason LIKE @q)");

        string where = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        string countSql = $"SELECT COUNT(*) FROM BanPlayerData {where};";
        await using var countCmd = new MySqlCommand(countSql, conn);
        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@q", $"%{search.Trim()}%");

        int total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        string selectSql = $@"
            SELECT Id, PlayerIP, UnbanTime, AdminId, AdminName, Reason
            FROM BanPlayerData
            {where}
            ORDER BY UnbanTime DESC
            LIMIT {pageSize} OFFSET {offset};";

        await using var selectCmd = new MySqlCommand(selectSql, conn);
        if (!string.IsNullOrWhiteSpace(search))
            selectCmd.Parameters.AddWithValue("@q", $"%{search.Trim()}%");

        var list = new List<BanRecord>();
        await using var reader = await selectCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string id = reader.GetString("Id");
            string ip = reader.GetString("PlayerIP");
            DateTime unban = reader.GetDateTime("UnbanTime");
            string adminId = reader.GetString("AdminId");
            string adminName = reader.GetString("AdminName");
            string reason = reader.GetString("Reason");
            bool isActive = unban > DateTime.Now;

            list.Add(new BanRecord(id, ip, unban, adminId, adminName, reason, isActive));
        }

        return (list, total);
    }

    public async Task<BanRecord?> GetBanByIdAsync(string id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        const string sql = "SELECT Id, PlayerIP, UnbanTime, AdminId, AdminName, Reason FROM BanPlayerData WHERE Id = @Id LIMIT 1;";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        string ip = reader.GetString("PlayerIP");
        DateTime unban = reader.GetDateTime("UnbanTime");
        string adminId = reader.GetString("AdminId");
        string adminName = reader.GetString("AdminName");
        string reason = reader.GetString("Reason");

        return new BanRecord(id, ip, unban, adminId, adminName, reason, unban > DateTime.Now);
    }

    public async Task SaveBanAsync(BanRecord ban, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        const string sql = @"
            INSERT INTO BanPlayerData 
            (Id, PlayerIP, UnbanTime, AdminId, AdminName, Reason)
            VALUES 
            (@Id, @PlayerIP, @UnbanTime, @AdminId, @AdminName, @Reason)
            ON DUPLICATE KEY UPDATE
                PlayerIP = @PlayerIP,
                UnbanTime = @UnbanTime,
                AdminId = @AdminId,
                AdminName = @AdminName,
                Reason = @Reason;";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", ban.Id);
        cmd.Parameters.AddWithValue("@PlayerIP", ban.PlayerIP);
        cmd.Parameters.AddWithValue("@UnbanTime", ban.UnbanTime);
        cmd.Parameters.AddWithValue("@AdminId", ban.AdminId);
        cmd.Parameters.AddWithValue("@AdminName", ban.AdminName);
        cmd.Parameters.AddWithValue("@Reason", ban.Reason);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeleteBanAsync(string id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        const string sql = "DELETE FROM BanPlayerData WHERE Id = @Id;";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        int rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    // ==================== 辅助方法 ====================

    private static PlayerStats ReadPlayerStats(MySqlDataReader reader)
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
