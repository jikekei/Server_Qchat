namespace Server.Qcat.Configuration;

public sealed class BotOptions
{
    // If empty, listens to all groups.
    public long[] AllowedGroupIds { get; set; } = Array.Empty<long>();

    // For operational notifications (monitoring, etc).
    public long[] NotifyGroupIds { get; set; } = Array.Empty<long>();
    public long[] NotifyPrivateUserIds { get; set; } = Array.Empty<long>();
}

