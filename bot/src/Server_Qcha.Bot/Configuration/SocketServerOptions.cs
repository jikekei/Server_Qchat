namespace Server.Qcat.Configuration;

public sealed class SocketServerOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int[] Ports { get; set; } = Array.Empty<int>();

    public string NotificationHost { get; set; } = "127.0.0.1";
    public int NotificationPort { get; set; } = 10088;

    /// <summary>双向 HMAC 鉴权密钥，须与插件 auth_token 相同。为空或仍是旧默认值时启动会输出安全警告，但不阻止运行。</summary>
    public string AuthToken { get; set; } = "";

    public int ConnectTimeoutMs { get; set; } = 10_000;
    public int ReadTimeoutMs { get; set; } = 2_000;
    public int Retries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1_000;
}

