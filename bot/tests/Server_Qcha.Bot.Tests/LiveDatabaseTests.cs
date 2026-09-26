using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.Data;
using Xunit;

namespace Server.Qcat.Tests;

public class LiveDatabaseTests
{
    [LiveDatabaseFact]
    [Trait("Category", "LiveDatabase")]
    public async Task GameDbRepository_LiveDatabase_ReadPlayers_Works()
    {
        var connectionString = Environment.GetEnvironmentVariable(LiveDatabaseFactAttribute.ConnectionStringVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString), "The live database connection string is required.");

        var options = new FixedOptionsMonitor<MySqlOptions>(new MySqlOptions
        {
            ConnectionString = connectionString!
        });
        var repo = new GameDbRepository(options, NullLogger<GameDbRepository>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (players, total) = await repo.GetPlayersAsync("", 1, 10, "playtime", true, timeout.Token);

        Assert.InRange(players.Count, 0, 10);
        Assert.True(total >= players.Count);
    }

    private sealed class FixedOptionsMonitor<T>(T current) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = current;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

// Discovery reports a real skip before any database code is executed.
internal sealed class LiveDatabaseFactAttribute : FactAttribute
{
    public const string EnableVariable = "QCHA_RUN_LIVE_DB_TESTS";
    public const string ConnectionStringVariable = "QCHA_TEST_MYSQL_CONNECTION_STRING";

    public LiveDatabaseFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
            Skip = $"Set {EnableVariable}=1 to explicitly enable live database tests.";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            Skip = $"Set {ConnectionStringVariable} to a dedicated test database connection string.";
    }
}
