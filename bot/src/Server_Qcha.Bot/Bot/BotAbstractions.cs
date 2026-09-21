namespace Server.Qcat.Bot;

/// <summary>
/// 机器人接入平台。
/// <para><see cref="NapCat"/>：OneBot 11 正向 WebSocket（NapCatQQ / go-cqhttp 等），群号与 QQ 号均为真实数字。</para>
/// <para><see cref="OfficialQq"/>：QQ 开放平台官方 Bot API v2，群与用户均为 OpenID，仅能收到 @机器人 的群消息。</para>
/// </summary>
public enum BotPlatform
{
    NapCat = 0,
    OfficialQq = 1,
}

/// <summary>群聊摘要。GroupId 在 NapCat 下是群号，在官方平台下是 group_openid，故统一为字符串。</summary>
public sealed record BotGroupSummary(string GroupId, string GroupName, int MemberCount, int MaxMemberCount);

/// <summary>好友/私聊摘要。UserId 在官方平台下为 user_openid。</summary>
public sealed record BotFriendSummary(string UserId, string Nickname, string? Remark);

/// <summary>
/// 被动回复上下文。官方 API 必须携带触发事件的 msg_id（及可选 event_id）才能走被动回复通道，
/// 否则会被判定为主动消息并受额度限制。NapCat 下为 null。
/// </summary>
public sealed record BotReplyContext(string? MessageId, string? EventId = null)
{
    public static readonly BotReplyContext None = new(null, null);
}

public sealed record BotSendResult(bool Success, string? MessageId, string? Error)
{
    public static BotSendResult Ok(string? messageId = null) => new(true, messageId, null);
    public static BotSendResult Fail(string error) => new(false, null, error);
}

/// <summary>平台无关的入站消息。各适配器负责把自己平台的原始事件翻译成这个结构。</summary>
public sealed record BotIncomingMessage
{
    public BotPlatform Platform { get; init; }

    /// <summary>是否群聊消息。false 表示私聊 / 单聊。</summary>
    public bool IsGroup { get; init; }

    /// <summary>会话目标：群聊为群标识（群号或 group_openid），私聊为用户标识。</summary>
    public string TargetId { get; init; } = "";

    /// <summary>发送者标识：NapCat 下是 QQ 号，官方平台下是 member_openid / user_openid。</summary>
    public string SenderId { get; init; } = "";

    /// <summary>发送者展示名。官方平台拿不到昵称时回退为 OpenID。</summary>
    public string SenderName { get; init; } = "";

    /// <summary>发送者是否具备管理权限（群主/管理员）。</summary>
    public bool IsAdmin { get; init; }

    /// <summary>纯文本内容（已剥离平台前缀，例如官方平台的 @机器人）。</summary>
    public string Text { get; init; } = "";

    /// <summary>本条消息的 ID，用于官方平台被动回复。</summary>
    public string? MessageId { get; init; }

    /// <summary>事件 ID，用于官方平台交互类事件的被动回复。</summary>
    public string? EventId { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>构造该消息对应的被动回复上下文。</summary>
    public BotReplyContext ToReplyContext() => new(MessageId, EventId);
}

/// <summary>
/// 机器人收发能力的统一抽象。NapCat（OneBot 11）与 QQ 官方 Bot API 各提供一个实现，
/// 上层（命令路由、通知推送、Web 面板）只依赖本接口，从而支持运行时切换平台。
/// </summary>
public interface IBotClient
{
    BotPlatform Platform { get; }

    /// <summary>平台展示名，用于日志与面板。</summary>
    string DisplayName { get; }

    bool IsConnected { get; }

    /// <summary>该平台是否支持 OneBot 风格的 CQ 码富文本。</summary>
    bool SupportsCqCode { get; }

    /// <summary>单条文本消息的长度上限（字符）。超过需分段发送。</summary>
    int MaxTextLength { get; }

    /// <summary>该平台是否支持主动（非被动回复）推送消息。</summary>
    bool SupportsActivePush { get; }

    DateTime? LastConnectedAt { get; }
    DateTime? LastDisconnectedAt { get; }
    string? LastError { get; }
    int CurrentAttempt { get; }
    int? NextReconnectInSeconds { get; }

    /// <summary>登录身份（NapCat 为 QQ 号；官方平台为 AppID）。</summary>
    string? ConnectedUserId { get; }

    /// <summary>登录昵称。</summary>
    string? ConnectedNickname { get; }

    /// <summary>当前接入点描述（WS 地址或网关地址），用于面板展示。</summary>
    string? Endpoint { get; }

    Task<IReadOnlyList<BotGroupSummary>> GetGroupsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<BotFriendSummary>> GetFriendsAsync(CancellationToken ct = default);

    Task<BotSendResult> SendGroupMessageAsync(string groupId, string text, BotReplyContext? reply = null, CancellationToken ct = default);

    Task<BotSendResult> SendPrivateMessageAsync(string userId, string text, BotReplyContext? reply = null, CancellationToken ct = default);

    /// <summary>主动请求重连（面板按钮 / 配置变更后触发）。</summary>
    Task ReconnectAsync();
}

/// <summary>
/// 当前生效的机器人客户端。
/// 由 <see cref="BotHostService"/> 在模式切换时更新，其他服务通过它发送消息，
/// 从而与具体平台解耦（替代原先直接持有 <c>CqWsSession</c> 的写法）。
/// </summary>
public sealed class BotClientAccessor
{
    private volatile IBotClient? _current;

    public IBotClient? Current => _current;

    public BotPlatform? CurrentPlatform => _current?.Platform;

    /// <summary>当前客户端是否已连接，可直接用于发送。</summary>
    public bool IsReady => _current is { IsConnected: true };

    internal void Set(IBotClient? client) => _current = client;
}
