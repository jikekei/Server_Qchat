namespace Server.Qcat.Configuration;

public sealed class BotOptions
{
    // If empty, listens to all groups.
    public long[] AllowedGroupIds { get; set; } = Array.Empty<long>();

    // For operational notifications (monitoring, etc).
    public long[] NotifyGroupIds { get; set; } = Array.Empty<long>();
    public long[] NotifyPrivateUserIds { get; set; } = Array.Empty<long>();

    // Target group for in-game .ac commands
    public long AcTargetGroupId { get; set; } = 0;
}

