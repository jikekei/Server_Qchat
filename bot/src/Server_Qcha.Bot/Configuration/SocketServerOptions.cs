namespace Server.Qcat.Configuration;

public sealed class SocketServerOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int[] Ports { get; set; } = Array.Empty<int>();

    public int ConnectTimeoutMs { get; set; } = 10_000;
    public int ReadTimeoutMs { get; set; } = 2_000;
    public int Retries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1_000;
}

