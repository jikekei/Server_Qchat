namespace Server.Qcat.Configuration;

public sealed class SocketServerOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int[] Ports { get; set; } = Array.Empty<int>();

    public string NotificationHost { get; set; } = "127.0.0.1";
    public int NotificationPort { get; set; } = 10088;

    /// <summary>双向鉴权密钥。空值或已公开的旧默认值会被拒绝，须与插件 auth_token 相同。</summary>
    public string AuthToken { get; set; } = "";

    public int ConnectTimeoutMs { get; set; } = 10_000;
    public int ReadTimeoutMs { get; set; } = 2_000;
    public int Retries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1_000;
}

