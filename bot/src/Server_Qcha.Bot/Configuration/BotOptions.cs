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
    /// 允许执行管理指令的 QQ 号。配置后只认名单，不看群主/管理员身份，通知名单也不会授予管理权限。
    /// 名单为空时沿用原有判定：群聊中群主和群管理员可用，私聊中 NotifyPrivateUserIds 内的用户可用，启动时会输出提示。
    /// </summary>
    public long[] AdminUserIds { get; set; } = Array.Empty<long>();

    // For operational notifications (monitoring, etc).
    public long[] NotifyGroupIds { get; set; } = Array.Empty<long>();
    public long[] NotifyPrivateUserIds { get; set; } = Array.Empty<long>();

    // Target group for in-game .ac commands
    public long AcTargetGroupId { get; set; } = 0;
}
