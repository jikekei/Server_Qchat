using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;

namespace Qchat.GameDatabase
{
    internal sealed class PlayerDelta
    {
        public string Name;
        public int PlayTime;
        public int PlayerKills;
        public int ScpKills;
        public int Deaths;

        public void Add(PlayerDelta older)
        {
            PlayTime += older.PlayTime;
            PlayerKills += older.PlayerKills;
            ScpKills += older.ScpKills;
            Deaths += older.Deaths;
        }
    }

    internal sealed class GameDatabaseStore
    {
        private readonly string connectionString;

        public GameDatabaseStore(string connectionString)
        {
            var settings = new MySqlConnectionStringBuilder(connectionString)
            {
                ConnectionTimeout = 3,
                DefaultCommandTimeout = 3,
                CancellationTimeout = 1,
            };
            this.connectionString = settings.ConnectionString;
        }

        public async Task SavePlayersAsync(IReadOnlyDictionary<string, PlayerDelta> changes, Action committing)
        {
            const string sql = @"
INSERT INTO PlayerData (Id, PlayerName, PlayTime, PlayersKilled, ScpsKilled, Deaths, IsAdmin)
VALUES (@id, @name, @time, @players, @scps, @deaths, FALSE)
ON DUPLICATE KEY UPDATE
    PlayerName = @name,
    PlayTime = COALESCE(PlayTime, 0) + @time,
    PlayersKilled = COALESCE(PlayersKilled, 0) + @players,
    ScpsKilled = COALESCE(ScpsKilled, 0) + @scps,
    Deaths = COALESCE(Deaths, 0) + @deaths;";

            using (var connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                using (var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false))
                using (var command = new MySqlCommand(sql, connection, transaction))
                {
                    command.Parameters.Add("@id", MySqlDbType.VarChar);
                    command.Parameters.Add("@name", MySqlDbType.VarChar);
                    command.Parameters.Add("@time", MySqlDbType.Int32);
                    command.Parameters.Add("@players", MySqlDbType.Int32);
                    command.Parameters.Add("@scps", MySqlDbType.Int32);
                    command.Parameters.Add("@deaths", MySqlDbType.Int32);
                    foreach (var entry in changes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                    {
                        command.Parameters["@id"].Value = entry.Key;
                        command.Parameters["@name"].Value = entry.Value.Name;
                        command.Parameters["@time"].Value = entry.Value.PlayTime;
                        command.Parameters["@players"].Value = entry.Value.PlayerKills;
                        command.Parameters["@scps"].Value = entry.Value.ScpKills;
                        command.Parameters["@deaths"].Value = entry.Value.Deaths;
                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }

                    committing();
                    await transaction.CommitAsync().ConfigureAwait(false);
                }
            }
        }

        public async Task<Dictionary<string, string>> GetActiveBansAsync(string[] playerIds)
        {
            var bans = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (playerIds.Length == 0)
                return bans;

            using (var connection = new MySqlConnection(connectionString))
            using (var command = connection.CreateCommand())
            {
                var names = new string[playerIds.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = "@player" + i;
                    command.Parameters.AddWithValue(names[i], playerIds[i]);
                }

                command.CommandText = "SELECT Id, Reason FROM BanPlayerData WHERE UnbanTime > NOW() AND Id IN (" +
                    string.Join(",", names) + ");";
                await connection.OpenAsync().ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        string id = reader.GetString(0);
                        bans[id] = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    }
                }
            }

            return bans;
        }
    }
}
