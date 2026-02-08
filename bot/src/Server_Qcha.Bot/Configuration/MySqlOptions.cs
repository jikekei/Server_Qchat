namespace Server.Qcat.Configuration;

public sealed class MySqlOptions
{
    // Prefer env var override in production: MySql__ConnectionString
    public string ConnectionString { get; set; } = "";
}

