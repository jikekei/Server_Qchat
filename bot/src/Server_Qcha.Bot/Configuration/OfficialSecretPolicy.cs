namespace Server.Qcat.Configuration;

/// <summary>
/// AppSecret 只在开放平台地址、AppID 和沙箱开关都没变时才允许留空复用。
/// </summary>
public static class OfficialSecretPolicy
{
    public static bool CanReuse(OfficialQqOptions? saved, string? apiBase, string? appId, bool sandbox)
    {
        if (saved is null || string.IsNullOrEmpty(saved.ClientSecret))
            return false;

        var probe = new OfficialQqOptions
        {
            ApiBase = string.IsNullOrWhiteSpace(apiBase) ? saved.ApiBase : apiBase,
            Sandbox = sandbox,
        };
        var savedProbe = new OfficialQqOptions
        {
            ApiBase = saved.ApiBase,
            Sandbox = saved.Sandbox,
        };

        return string.Equals(probe.ResolveApiBase(), savedProbe.ResolveApiBase(), StringComparison.OrdinalIgnoreCase)
            && string.Equals((appId ?? "").Trim(), (saved.AppId ?? "").Trim(), StringComparison.Ordinal);
    }
}
