using Server.Qcat.Bot;

namespace Server.Qcat.Configuration;

public sealed class BotOptions
{
    /// <summary>
    /// 机器人接入模式：NapCat（OneBot 11 正向 WebSocket）或 OfficialQq（QQ 开放平台官方 Bot API）。
    /// 该值可在 Web 面板中在线切换，保存后由 <c>BotHostService</c> 热切换连接，无需重启进程。
    /// </summary>
    public BotPlatform Mode { get; set; } = BotPlatform.NapCat;

    // If empty, listens to all groups. 管理指令不看这个名单。
    public long[] AllowedGroupIds { get; set; } = Array.Empty<long>();

    /// <summary>
    /// 允许执行管理指令的 QQ 号。不信任群主/管理员身份；名单为空则拒绝全部管理指令。
    /// 通知名单不会授予管理权限。
    /// </summary>
    public long[] AdminUserIds { get; set; } = Array.Empty<long>();

    // For operational notifications (monitoring, etc).
    public long[] NotifyGroupIds { get; set; } = Array.Empty<long>();
    public long[] NotifyPrivateUserIds { get; set; } = Array.Empty<long>();

    // Target group for in-game .ac commands
    public long AcTargetGroupId { get; set; } = 0;
}
