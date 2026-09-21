using Server.Qcat.Bot;

namespace Server.Qcat.Configuration;

public sealed class BotOptions
{
    /// <summary>
    /// 机器人接入模式：NapCat（OneBot 11 正向 WebSocket）或 OfficialQq（QQ 开放平台官方 Bot API）。
    /// 该值可在 Web 面板中在线切换，保存后由 <c>BotHostService</c> 热切换连接，无需重启进程。
    /// </summary>
    public BotPlatform Mode { get; set; } = BotPlatform.NapCat;

    // If empty, listens to all groups.
    public long[] AllowedGroupIds { get; set; } = Array.Empty<long>();

    // For operational notifications (monitoring, etc).
    public long[] NotifyGroupIds { get; set; } = Array.Empty<long>();
    public long[] NotifyPrivateUserIds { get; set; } = Array.Empty<long>();

    // Target group for in-game .ac commands
    public long AcTargetGroupId { get; set; } = 0;
}
