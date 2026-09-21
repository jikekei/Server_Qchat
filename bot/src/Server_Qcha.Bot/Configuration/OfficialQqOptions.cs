namespace Server.Qcat.Configuration;

/// <summary>
/// QQ 开放平台官方 Bot API v2 接入配置。
///
/// 与 NapCat（OneBot 11）的关键差异，配置与代码均以此为前提：
/// <list type="bullet">
/// <item>鉴权用 AppID + AppSecret 换取 AccessToken，请求头形如 <c>Authorization: QQBot {ACCESS_TOKEN}</c>。</item>
/// <item>群与用户一律为 OpenID，拿不到真实 QQ 号；也拿不到群名称、群列表、好友列表。</item>
/// <item>群聊里机器人只能收到 @自己 的消息。</item>
/// <item>回复必须携带触发事件的 msg_id 才能走「被动回复」，否则算主动消息并受额度限制。</item>
/// </list>
/// </summary>
public sealed class OfficialQqOptions
{
    /// <summary>开放平台 API 根地址。生产环境为 https://api.bot.qq.com（旧域名为 https://api.sgroup.qq.com）。</summary>
    public string ApiBase { get; set; } = "https://api.bot.qq.com";

    /// <summary>在开放平台「开发设置」中获取的机器人 AppID。</summary>
    public string AppId { get; set; } = "";

    /// <summary>在开放平台「开发设置」中获取的 AppSecret（ClientSecret）。仅创建时展示一次。</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>是否使用沙箱环境（会在 ApiBase 主机名前加 sandbox. 前缀）。</summary>
    public bool Sandbox { get; set; }

    /// <summary>
    /// 事件订阅位。默认 <c>1 &lt;&lt; 25</c>（GROUP_AND_C2C_EVENT），
    /// 即同时订阅群聊 @机器人 消息与单聊消息。频道场景请自行叠加 PUBLIC_GUILD_MESSAGES(1&lt;&lt;30) 等位。
    /// </summary>
    public int Intents { get; set; } = 1 << 25;

    /// <summary>分片序号（单实例保持 0）。</summary>
    public int ShardIndex { get; set; }

    /// <summary>分片总数（单实例保持 1，即 Identify 时上报 shard=[0,1]）。</summary>
    public int ShardTotal { get; set; } = 1;

    /// <summary>单条文本消息长度上限（字符）。超长会被网关拒绝，代码会自动分段并递增 msg_seq。</summary>
    public int MaxTextLength { get; set; } = 800;

    /// <summary>
    /// 是否允许主动推送（非被动回复）。官方对主动消息有严格额度与时间窗限制，
    /// 默认关闭，此时通知类推送只在「被动回复窗口内」或被显式开启时才发出。
    /// </summary>
    public bool AllowActivePush { get; set; }

    /// <summary>
    /// 允许执行管理指令的用户 OpenID 列表。
    /// 官方群虽然有 member_role，但沙箱/异常情况下可能为空，此列表作为兜底白名单。
    /// </summary>
    public string[] AdminOpenIds { get; set; } = Array.Empty<string>();

    /// <summary>群消息白名单（group_openid）。留空表示不限制。</summary>
    public string[] AllowedGroupOpenIds { get; set; } = Array.Empty<string>();

    /// <summary>运营通知目标群（group_openid）。官方平台无法枚举群列表，需手工登记。</summary>
    public string[] NotifyGroupOpenIds { get; set; } = Array.Empty<string>();

    /// <summary>游戏内 <c>.ac</c> 推送目标群（group_openid）。留空时回退到第一个通知群或最近活跃群。</summary>
    public string AcTargetGroupOpenId { get; set; } = "";

    /// <summary>运营通知目标用户（user_openid，单聊）。</summary>
    public string[] NotifyPrivateOpenIds { get; set; } = Array.Empty<string>();

    // 连接失败或断开后，重连的基础间隔（秒），随连续失败次数线性增长
    public int ReconnectDelaySeconds { get; set; } = 5;

    // 重连间隔上限（秒）
    public int MaxReconnectDelaySeconds { get; set; } = 60;

    /// <summary>单次 HTTP 调用超时（秒）。</summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>解析后的实际 API 根地址。</summary>
    public string ResolveApiBase()
    {
        string baseUrl = string.IsNullOrWhiteSpace(ApiBase) ? "https://api.bot.qq.com" : ApiBase.Trim().TrimEnd('/');
        if (!Sandbox)
            return baseUrl;

        // https://api.bot.qq.com -> https://sandbox.api.bot.qq.com
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && !uri.Host.StartsWith("sandbox.", StringComparison.OrdinalIgnoreCase))
            return $"{uri.Scheme}://sandbox.{uri.Host}{uri.Port switch { 80 or 443 or 0 => "", _ => ":" + uri.Port }}";

        return baseUrl;
    }

    /// <summary>配置是否足以发起连接。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
