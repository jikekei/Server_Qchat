namespace Server.Qcat.Configuration;

public sealed class GoCqHttpOptions
{
    public string WsBaseUri { get; set; } = "ws://127.0.0.1:6700";

    // 连接失败或断开后，重连的基础间隔（秒），随连续失败次数线性增长
    public int ReconnectDelaySeconds { get; set; } = 5;

    // 重连间隔上限（秒）
    public int MaxReconnectDelaySeconds { get; set; } = 30;
}

