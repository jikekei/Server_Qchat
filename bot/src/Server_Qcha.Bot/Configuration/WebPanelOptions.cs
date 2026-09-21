namespace Server.Qcat.Configuration;

/// <summary>
/// Web 管理面板配置。
/// </summary>
public sealed class WebPanelOptions
{
    /// <summary>是否启用 Web 面板。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>监听地址，0.0.0.0 表示所有网卡。</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>监听端口。</summary>
    public int Port { get; set; } = 8080;

    /// <summary>会话有效期（分钟）。</summary>
    public int SessionMinutes { get; set; } = 480;

    /// <summary>本地账号数据库文件路径（默认保存在 data/panel.db，相对 ContentRoot 或绝对路径）。</summary>
    public string DatabasePath { get; set; } = "data/panel.db";

    /// <summary>内置管理员账号的用户名。</summary>
    public string DefaultAdminUsername { get; set; } = "admin";

    /// <summary>
    /// 是否每次启动都重置内置管理员密码并随机生成。
    /// 关闭后仅在账号不存在时创建一次，密码随机后不再变更。
    /// </summary>
    public bool ResetBuiltInPasswordOnStartup { get; set; } = true;
}
