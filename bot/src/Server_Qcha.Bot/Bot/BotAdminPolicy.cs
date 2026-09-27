namespace Server.Qcat.Bot;

/// <summary>
/// 管理指令的权限判定。
/// 配置了管理名单（Bot:AdminUserIds / OfficialQq:AdminOpenIds）时只认名单，不看群主/管理员角色，
/// 通知名单也不会因此变成管理员。
/// 名单为空时沿用原有逻辑，保持与旧版本兼容：
/// NapCat 群聊看群主/管理员角色，NapCat 私聊对 NotifyPrivateUserIds 内的用户放行；
/// 官方群聊看 member_role 是否为 admin/owner，官方单聊不放行。
/// </summary>
public static class BotAdminPolicy
{
    public static bool HasList(long[]? adminUserIds) => adminUserIds is { Length: > 0 };

    public static bool HasList(string[]? adminOpenIds) => adminOpenIds is { Length: > 0 };

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

    /// <summary>NapCat 群聊：有名单只认名单；名单为空时沿用群主/管理员角色判定。</summary>
    public static bool IsNapCatGroupAdmin(long[]? adminUserIds, long userId, bool isGroupOwnerOrAdmin)
    {
        if (HasList(adminUserIds))
            return IsListed(adminUserIds, userId);
        return isGroupOwnerOrAdmin;
    }

    /// <summary>
    /// NapCat 私聊：有名单只认名单；名单为空时沿用旧逻辑，
    /// 即 NotifyPrivateUserIds 内的用户（私聊入口本身只对他们开放）视为管理员。
    /// </summary>
    public static bool IsNapCatPrivateAdmin(long[]? adminUserIds, long[]? notifyPrivateUserIds, long userId)
    {
        if (HasList(adminUserIds))
            return IsListed(adminUserIds, userId);
        return notifyPrivateUserIds is { Length: > 0 } && userId > 0 && notifyPrivateUserIds.Contains(userId);
    }

    /// <summary>官方群聊：有名单只认名单；名单为空时沿用 member_role 为 admin/owner 的判定。</summary>
    public static bool IsOfficialGroupAdmin(string[]? adminOpenIds, string? senderId, string? memberRole)
    {
        if (HasList(adminOpenIds))
            return IsListed(adminOpenIds, senderId);
        string role = memberRole ?? "";
        return role.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || role.Equals("owner", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>官方单聊没有群角色，只认名单。名单为空时不放行，与旧版本一致。</summary>
    public static bool IsOfficialC2CAdmin(string[]? adminOpenIds, string? senderId) => IsListed(adminOpenIds, senderId);

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
