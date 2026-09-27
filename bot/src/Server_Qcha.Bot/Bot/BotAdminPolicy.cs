namespace Server.Qcat.Bot;

/// <summary>
/// 管理指令只认显式名单，不看群主/管理员角色，也不把通知名单当成管理员。
/// 名单为空时全部拒绝。
/// </summary>
public static class BotAdminPolicy
{
    public static bool IsListed(long[]? adminUserIds, long userId)
    {
        if (adminUserIds is null || adminUserIds.Length == 0 || userId <= 0)
            return false;
        return adminUserIds.Contains(userId);
    }

    public static bool IsListed(string[]? adminOpenIds, string? senderId)
    {
        if (adminOpenIds is null || adminOpenIds.Length == 0 || string.IsNullOrEmpty(senderId))
            return false;
        return adminOpenIds.Contains(senderId);
    }

    /// <summary>未配置群白名单时仍记录，配置了则只记录白名单内的群，避免 .ac 落到名单外。</summary>
    public static bool MayObserveGroup(string[]? allowedGroupOpenIds, string? groupOpenId)
    {
        if (string.IsNullOrEmpty(groupOpenId))
            return false;
        if (allowedGroupOpenIds is null || allowedGroupOpenIds.Length == 0)
            return true;
        return allowedGroupOpenIds.Contains(groupOpenId);
    }
}
