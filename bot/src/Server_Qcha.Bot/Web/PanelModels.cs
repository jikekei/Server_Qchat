namespace Server.Qcat.Web;

/// <summary>
/// Web 面板权限位标志。可自由组合，以 long 形式存储于本地数据库。
/// 后续新增能力时只需追加位，不影响既有账号数据。
/// </summary>
[Flags]
public enum PanelPermission : long
{
    None = 0,

    /// <summary>查看服务器列表、在线状态与玩家列表</summary>
    ServersView = 1L << 0,

    /// <summary>发送全服广播</summary>
    BroadcastSend = 1L << 1,

    /// <summary>踢出玩家</summary>
    PlayersKick = 1L << 2,

    /// <summary>封禁玩家</summary>
    PlayersBan = 1L << 3,

    /// <summary>回合控制（开始 / 重启回合）</summary>
    RoundControl = 1L << 4,

    /// <summary>服务器进程控制（LocalAdmin：启动/停止/重启进程、收发控制台命令）</summary>
    ServerControl = 1L << 5,

    /// <summary>Web 账号管理</summary>
    AccountsManage = 1L << 6,

    /// <summary>查看操作审计日志</summary>
    AuditView = 1L << 7,

    /// <summary>查看与调整机器人日志级别（影响日志详细程度）</summary>
    LoggingManage = 1L << 8,

    /// <summary>QQ 机器人管理（查看状态、配置群白名单与通知、NapCat 接口与重连）</summary>
    BotManage = 1L << 9,

    /// <summary>数据库管理（查看与编辑玩家统计、封禁记录、配置连接串与初始化表）</summary>
    DatabaseManage = 1L << 10,

    // ---- 预设组合（前端"快速选择"用）----

    /// <summary>只读</summary>
    Viewer = ServersView,

    /// <summary>运营：只读 + 广播 + 踢人 + 回合控制</summary>
    Operator = ServersView | BroadcastSend | PlayersKick | RoundControl,

    /// <summary>管理员：运营 + 封禁 + 服务器控制 + 审计 + 日志级别 + 机器人管理 + 数据库管理</summary>
    Admin = ServersView | BroadcastSend | PlayersKick | PlayersBan | RoundControl | ServerControl | AuditView | LoggingManage | BotManage | DatabaseManage,

    /// <summary>所有者：全部权限</summary>
    Owner = ServersView | BroadcastSend | PlayersKick | PlayersBan | RoundControl | ServerControl | AccountsManage | AuditView | LoggingManage | BotManage | DatabaseManage,
}

/// <summary>权限元数据，用于前端渲染与权限清单接口。</summary>
public static class PanelPermissions
{
    public sealed record Descriptor(string Key, PanelPermission Value, string Name, string Description);

    public static readonly IReadOnlyList<Descriptor> All = new[]
    {
        new Descriptor("servers.view", PanelPermission.ServersView, "查看服务器", "查看服务器在线状态、玩家列表与详细信息"),
        new Descriptor("broadcast.send", PanelPermission.BroadcastSend, "发送广播", "向指定服务器发送全服广播"),
        new Descriptor("players.kick", PanelPermission.PlayersKick, "踢出玩家", "将玩家踢出服务器"),
        new Descriptor("players.ban", PanelPermission.PlayersBan, "封禁玩家", "封禁玩家（可设时长与原因）"),
        new Descriptor("round.control", PanelPermission.RoundControl, "回合控制", "强制开始回合、重启回合"),
        new Descriptor("server.control", PanelPermission.ServerControl, "服务器控制", "启动/停止/重启服务器进程、下发控制台命令（LocalAdmin 能力）"),
        new Descriptor("accounts.manage", PanelPermission.AccountsManage, "账号管理", "创建、修改、禁用 Web 登录账号及其权限"),
        new Descriptor("audit.view", PanelPermission.AuditView, "审计日志", "查看管理操作审计记录"),
        new Descriptor("logging.manage", PanelPermission.LoggingManage, "日志级别", "查看与调整机器人日志级别（影响日志详细程度）"),
        new Descriptor("bot.manage", PanelPermission.BotManage, "机器人管理", "查看 QQ 机器人状态、配置群白名单、通知目标与 OneBot 连接"),
        new Descriptor("database.manage", PanelPermission.DatabaseManage, "数据库管理", "查看与编辑玩家统计、封禁记录、配置连接串与初始化表"),
    };

    /// <summary>将权限位展开为 Key 列表，便于前端勾选。</summary>
    public static List<string> ToKeys(PanelPermission permissions) =>
        All.Where(d => permissions.HasFlag(d.Value)).Select(d => d.Key).ToList();

    /// <summary>由 Key 列表还原为权限位。</summary>
    public static PanelPermission FromKeys(IEnumerable<string>? keys)
    {
        PanelPermission result = PanelPermission.None;
        if (keys is null)
            return result;

        foreach (var key in keys)
        {
            var match = All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                result |= match.Value;
        }
        return result;
    }
}

/// <summary>Web 面板登录账号。</summary>
public sealed class PanelAccount
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public PanelPermission Permissions { get; set; } = PanelPermission.Viewer;
    public bool IsEnabled { get; set; } = true;

    /// <summary>内置账号：启动时按配置随机重置密码。</summary>
    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public bool Has(PanelPermission permission) =>
        IsEnabled && (permission == PanelPermission.None || (Permissions & permission) == permission);
}

/// <summary>返回给前端的账号视图（不含密码哈希）。</summary>
public sealed record PanelAccountDto(
    long Id,
    string Username,
    string DisplayName,
    PanelPermission Permissions,
    IReadOnlyList<string> PermissionKeys,
    bool IsEnabled,
    bool IsBuiltIn,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

/// <summary>已登录会话。</summary>
public sealed class PanelSession
{
    public string Token { get; set; } = "";
    public long AccountId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public PanelPermission Permissions { get; set; }
    public DateTime ExpiresAt { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool Has(PanelPermission permission) =>
        permission == PanelPermission.None || (Permissions & permission) == permission;
}

/// <summary>操作审计记录。</summary>
public sealed class PanelAuditEntry
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Success { get; set; } = true;
}
