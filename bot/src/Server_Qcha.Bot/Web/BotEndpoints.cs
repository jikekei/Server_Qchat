using System.Text;
using System.Text.Json;
using Server.Qcat.Bot;
using Server.Qcat.Configuration;

namespace Server.Qcat.Web;

// ---------------- 请求体 ----------------

public sealed record UpdateBotSettingsRequest(
    string? Mode,
    string? WsBaseUri,
    int? ReconnectDelaySeconds,
    int? MaxReconnectDelaySeconds,
    long[]? AllowedGroupIds,
    long[]? NotifyGroupIds,
    long[]? NotifyPrivateUserIds,
    long? AcTargetGroupId,
    bool? AutoReconnect,
    // ---- QQ 官方 Bot API ----
    string? OfficialApiBase,
    string? OfficialAppId,
    string? OfficialClientSecret,
    bool? OfficialSandbox,
    int? OfficialIntents,
    int? OfficialMaxTextLength,
    bool? OfficialAllowActivePush,
    string[]? OfficialAdminOpenIds,
    string[]? OfficialAllowedGroupOpenIds,
    string[]? OfficialNotifyGroupOpenIds,
    string[]? OfficialNotifyPrivateOpenIds,
    string? OfficialAcTargetGroupOpenId);

public sealed record SendTestMessageRequest(bool IsGroup, string? TargetId, string? Message);

/// <summary>
/// 凭证连通性测试请求。字段留空表示沿用已保存的值（前端不回显 AppSecret，
/// 所以「留空 = 用已保存的密钥」是这里唯一说得通的语义）。
/// </summary>
public sealed record TestOfficialAuthRequest(string? ApiBase, string? AppId, string? ClientSecret, bool? Sandbox);

/// <summary>
/// QQ 机器人状态查看、配置管理、模式切换、重连与测试发送 API。
/// 权限：<see cref="PanelPermission.BotManage"/>（bot.manage）。
/// </summary>
public static class BotEndpoints
{
    public static void MapBotApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/bot");

        api.MapGet("/status", GetStatusAsync);
        api.MapPut("/settings", UpdateSettingsAsync);
        api.MapPost("/reconnect", ReconnectAsync);
        api.MapPost("/send-test", SendTestMessageAsync);
        api.MapGet("/commands", GetCommandManifestAsync);
        api.MapPost("/test-official-auth", TestOfficialAuthAsync);
        api.MapGet("/official/panels", GetOfficialPanelsAsync);
        api.MapPost("/official/panels/sync", SyncOfficialPanelsAsync);
        api.MapDelete("/official/panels/{panelId}", DeleteOfficialPanelAsync);
    }

    private static async Task<IResult> GetStatusAsync(
        HttpContext ctx,
        PanelAuthService auth,
        BotHostService botHost,
        BotSettingsStore store,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        var settings = store.LoadCurrent();
        var client = botHost.ActiveClient;

        IReadOnlyList<BotGroupSummary> groups = Array.Empty<BotGroupSummary>();
        IReadOnlyList<BotFriendSummary> friends = Array.Empty<BotFriendSummary>();

        if (client is { IsConnected: true })
        {
            groups = await client.GetGroupsAsync(ct);
            friends = await client.GetFriendsAsync(ct);
        }

        return Results.Json(new
        {
            status = new
            {
                // 当前实际生效的模式与配置声明的模式可能瞬时不一致（切换中），两者都给出
                platform = (client?.Platform ?? botHost.ActivePlatform).ToString(),
                platformDisplay = client?.DisplayName ?? "未启动",
                configuredMode = settings.Bot.Mode.ToString(),
                isConnected = client?.IsConnected ?? false,
                // 官方模式的接入点是运行时从 /gateway 取得的，未连接时不回退到 NapCat 的 WS 地址，避免误导
                endpoint = client?.Endpoint
                           ?? (settings.Bot.Mode == BotPlatform.NapCat ? settings.GoCqHttp.WsBaseUri : null),
                lastConnectedAt = client?.LastConnectedAt,
                lastDisconnectedAt = client?.LastDisconnectedAt,
                lastError = client?.LastError,
                currentAttempt = client?.CurrentAttempt ?? 0,
                nextReconnectInSeconds = client?.NextReconnectInSeconds,
                connectedUserId = client?.ConnectedUserId,
                connectedNickname = client?.ConnectedNickname,
                // 平台能力，供前端按模式呈现差异
                supportsCqCode = client?.SupportsCqCode ?? true,
                supportsActivePush = client?.SupportsActivePush ?? true,
                maxTextLength = client?.MaxTextLength ?? 0,
                supportsGroupListing = client?.Platform != BotPlatform.OfficialQq,
            },
            settings = new
            {
                mode = settings.Bot.Mode.ToString(),
                wsBaseUri = settings.GoCqHttp.WsBaseUri,
                reconnectDelaySeconds = settings.GoCqHttp.ReconnectDelaySeconds,
                maxReconnectDelaySeconds = settings.GoCqHttp.MaxReconnectDelaySeconds,
                allowedGroupIds = settings.Bot.AllowedGroupIds,
                notifyGroupIds = settings.Bot.NotifyGroupIds,
                notifyPrivateUserIds = settings.Bot.NotifyPrivateUserIds,
                acTargetGroupId = settings.Bot.AcTargetGroupId,
                officialQq = new
                {
                    apiBase = settings.OfficialQq.ApiBase,
                    appId = settings.OfficialQq.AppId,
                    // 密钥不回显，前端留空表示不修改
                    hasClientSecret = !string.IsNullOrEmpty(settings.OfficialQq.ClientSecret),
                    sandbox = settings.OfficialQq.Sandbox,
                    intents = settings.OfficialQq.Intents,
                    maxTextLength = settings.OfficialQq.MaxTextLength,
                    allowActivePush = settings.OfficialQq.AllowActivePush,
                    adminOpenIds = settings.OfficialQq.AdminOpenIds,
                    allowedGroupOpenIds = settings.OfficialQq.AllowedGroupOpenIds,
                    notifyGroupOpenIds = settings.OfficialQq.NotifyGroupOpenIds,
                    notifyPrivateOpenIds = settings.OfficialQq.NotifyPrivateOpenIds,
                    acTargetGroupOpenId = settings.OfficialQq.AcTargetGroupOpenId,
                },
            },
            groups = groups.Select(g => new
            {
                groupId = g.GroupId,
                groupName = g.GroupName,
                memberCount = g.MemberCount,
                maxMemberCount = g.MaxMemberCount,
            }).ToList(),
            friends = friends.Select(f => new
            {
                userId = f.UserId,
                nickname = f.Nickname,
                remark = f.Remark,
            }).ToList(),
            configPath = store.ConfigPath,
            fileExists = store.Exists,
        });
    }

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateBotSettingsRequest request,
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        BotSettingsStore store,
        BotHostService botHost,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        var current = store.LoadCurrent();

        // ---- 解析接入模式 ----
        BotPlatform mode = current.Bot.Mode;
        if (!string.IsNullOrWhiteSpace(request.Mode))
        {
            if (!Enum.TryParse(request.Mode, ignoreCase: true, out BotPlatform parsed))
                return Results.Json(new { error = "接入模式不合法，仅支持 NapCat 或 OfficialQq" }, statusCode: StatusCodes.Status400BadRequest);
            mode = parsed;
        }

        // ---- 校验 NapCat 侧参数 ----
        string wsBaseUri = string.IsNullOrWhiteSpace(request.WsBaseUri)
            ? current.GoCqHttp.WsBaseUri
            : request.WsBaseUri.Trim();

        if (mode == BotPlatform.NapCat &&
            !wsBaseUri.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
            !wsBaseUri.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new { error = "WebSocket 地址不合法，必须以 ws:// 或 wss:// 开头" }, statusCode: StatusCodes.Status400BadRequest);
        }

        int reconnectDelay = request.ReconnectDelaySeconds is > 0 and <= 300
            ? request.ReconnectDelaySeconds.Value
            : current.GoCqHttp.ReconnectDelaySeconds;

        int maxReconnectDelay = request.MaxReconnectDelaySeconds is > 0 and <= 3600
            ? request.MaxReconnectDelaySeconds.Value
            : current.GoCqHttp.MaxReconnectDelaySeconds;

        if (maxReconnectDelay < reconnectDelay)
            return Results.Json(new { error = "最大重连间隔不能小于基础重连间隔" }, statusCode: StatusCodes.Status400BadRequest);

        // ---- 校验官方侧参数 ----
        var official = new OfficialQqOptions
        {
            ApiBase = string.IsNullOrWhiteSpace(request.OfficialApiBase)
                ? current.OfficialQq.ApiBase
                : request.OfficialApiBase.Trim().TrimEnd('/'),
            AppId = request.OfficialAppId is null ? current.OfficialQq.AppId : request.OfficialAppId.Trim(),
            // 前端不回显密钥，留空即视为保持原值
            ClientSecret = string.IsNullOrWhiteSpace(request.OfficialClientSecret)
                ? current.OfficialQq.ClientSecret
                : request.OfficialClientSecret.Trim(),
            Sandbox = request.OfficialSandbox ?? current.OfficialQq.Sandbox,
            Intents = request.OfficialIntents is > 0 ? request.OfficialIntents.Value : current.OfficialQq.Intents,
            MaxTextLength = request.OfficialMaxTextLength is >= 100 and <= 4000
                ? request.OfficialMaxTextLength.Value
                : current.OfficialQq.MaxTextLength,
            AllowActivePush = request.OfficialAllowActivePush ?? current.OfficialQq.AllowActivePush,
            AdminOpenIds = NormalizeStrings(request.OfficialAdminOpenIds) ?? current.OfficialQq.AdminOpenIds,
            AllowedGroupOpenIds = NormalizeStrings(request.OfficialAllowedGroupOpenIds) ?? current.OfficialQq.AllowedGroupOpenIds,
            NotifyGroupOpenIds = NormalizeStrings(request.OfficialNotifyGroupOpenIds) ?? current.OfficialQq.NotifyGroupOpenIds,
            NotifyPrivateOpenIds = NormalizeStrings(request.OfficialNotifyPrivateOpenIds) ?? current.OfficialQq.NotifyPrivateOpenIds,
            AcTargetGroupOpenId = request.OfficialAcTargetGroupOpenId is null
                ? current.OfficialQq.AcTargetGroupOpenId
                : request.OfficialAcTargetGroupOpenId.Trim(),
            ReconnectDelaySeconds = current.OfficialQq.ReconnectDelaySeconds,
            MaxReconnectDelaySeconds = current.OfficialQq.MaxReconnectDelaySeconds,
            RequestTimeoutSeconds = current.OfficialQq.RequestTimeoutSeconds,
            ShardIndex = current.OfficialQq.ShardIndex,
            ShardTotal = current.OfficialQq.ShardTotal,
        };

        if (!official.ApiBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !official.ApiBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new { error = "开放平台 API 地址不合法，必须以 http:// 或 https:// 开头" }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (mode == BotPlatform.OfficialQq && !official.IsConfigured)
        {
            return Results.Json(new { error = "切换为官方模式前，请先填写 AppID 与 AppSecret" }, statusCode: StatusCodes.Status400BadRequest);
        }

        bool modeChanged = current.Bot.Mode != mode;
        bool endpointChanged = mode == BotPlatform.NapCat &&
            !string.Equals(current.GoCqHttp.WsBaseUri.Trim(), wsBaseUri, StringComparison.OrdinalIgnoreCase);

        var model = new BotSettingsStore.BotSettingsModel
        {
            Bot = new BotOptions
            {
                Mode = mode,
                AllowedGroupIds = NormalizeLongs(request.AllowedGroupIds) ?? current.Bot.AllowedGroupIds,
                NotifyGroupIds = NormalizeLongs(request.NotifyGroupIds) ?? current.Bot.NotifyGroupIds,
                NotifyPrivateUserIds = NormalizeLongs(request.NotifyPrivateUserIds) ?? current.Bot.NotifyPrivateUserIds,
                AcTargetGroupId = request.AcTargetGroupId is > 0
                    ? request.AcTargetGroupId.Value
                    : (request.AcTargetGroupId is 0 ? 0 : current.Bot.AcTargetGroupId),
            },
            GoCqHttp = new GoCqHttpOptions
            {
                WsBaseUri = wsBaseUri,
                ReconnectDelaySeconds = reconnectDelay,
                MaxReconnectDelaySeconds = maxReconnectDelay,
            },
            OfficialQq = official,
            MySql = current.MySql,
        };

        store.Save(model);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "bot.config-update",
            Target = "QQ Bot 配置",
            Detail = modeChanged
                ? $"接入模式 {current.Bot.Mode} → {mode}"
                : $"模式 {mode}，WS: {wsBaseUri}，官方 AppID: {official.AppId}",
            Success = true,
        }, ct);

        // 模式变更由 BotHostService 监听配置热重载自动切换连接；
        // 仅当事先就处于同一模式却改了接入点时才需要显式重连。
        if (!modeChanged && (endpointChanged || request.AutoReconnect == true))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(300); // 略微等待文件重载生效
                await botHost.ReconnectActiveAsync();
            });
        }

        string message = modeChanged
            ? $"接入模式已切换为 {DescribeMode(mode)}，正在建立新连接"
            : endpointChanged
                ? "配置已保存并已发起重新连接"
                : "配置已保存，内存中即刻生效";

        return Results.Json(new { success = true, message, modeChanged, mode = mode.ToString() });
    }

    private static async Task<IResult> ReconnectAsync(
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        BotHostService botHost,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        await botHost.ReconnectActiveAsync();

        string platform = botHost.ActiveClient?.DisplayName ?? "机器人";

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "bot.reconnect",
            Target = "QQ Bot 连接",
            Detail = $"主动触发重新连接（{platform}）",
            Success = true,
        }, ct);

        return Results.Json(new { success = true, message = "已发起重新连接" });
    }

    private static async Task<IResult> SendTestMessageAsync(
        SendTestMessageRequest request,
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        BotHostService botHost,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        var client = botHost.ActiveClient;
        if (client is null)
            return Results.Json(new { success = false, error = "机器人接入尚未启动" }, statusCode: StatusCodes.Status500InternalServerError);

        string targetId = request.TargetId?.Trim() ?? "";
        if (targetId.Length == 0)
            return Results.Json(new { error = "目标 ID 不合法" }, statusCode: StatusCodes.Status400BadRequest);

        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.Json(new { error = "消息内容不能为空" }, statusCode: StatusCodes.Status400BadRequest);

        string msg = request.Message.Trim();
        if (msg.Length > 2000)
            return Results.Json(new { error = "消息内容过长（上限 2000 字符）" }, statusCode: StatusCodes.Status400BadRequest);

        var result = request.IsGroup
            ? await client.SendGroupMessageAsync(targetId, msg, BotReplyContext.None, ct)
            : await client.SendPrivateMessageAsync(targetId, msg, BotReplyContext.None, ct);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "bot.test-message",
            Target = request.IsGroup ? $"群 {targetId}" : $"私聊 {targetId}",
            Detail = $"内容: {(msg.Length > 60 ? msg[..60] + "..." : msg)}",
            Success = result.Success,
        }, ct);

        if (!result.Success)
            return Results.Json(new { success = false, error = result.Error }, statusCode: StatusCodes.Status500InternalServerError);

        return Results.Json(new { success = true, messageId = result.MessageId, message = "测试消息已成功发送" });
    }

    /// <summary>
    /// 输出指令目录，含官方开放平台管理端所需的配置清单。
    /// 官方要求指令名 ≤8 个中文字符或 16 个英文字符，介绍 ≤15 个中文字符或 30 个英文字符。
    /// </summary>
    private static async Task<IResult> GetCommandManifestAsync(
        HttpContext ctx,
        PanelAuthService auth,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        return Results.Json(new
        {
            commands = BotCommandCatalog.All.Select(c => new
            {
                name = c.Name,
                aliases = c.Aliases,
                usage = c.Usage,
                summary = c.Summary,
                adminOnly = c.AdminOnly,
            }).ToList(),
            officialConsoleManifest = BotCommandCatalog.BuildOfficialConsoleManifest(),
        });
    }

    /// <summary>
    /// 现场校验 QQ 开放平台凭证：用给定（或已保存）的 AppID / AppSecret 申请一次 AccessToken。
    /// 只做探测，**不修改**当前接入模式与已保存配置，目的是让面板上的「测试凭证」按钮有即时反馈，
    /// 免得用户非要切到官方模式、等连接失败才知道密钥填错了。
    /// </summary>
    private static async Task<IResult> TestOfficialAuthAsync(
        TestOfficialAuthRequest request,
        HttpContext ctx,
        PanelAuthService auth,
        BotSettingsStore store,
        HttpClient http,
        PanelDatabase db,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        var saved = store.LoadCurrent().OfficialQq;

        string apiBase = string.IsNullOrWhiteSpace(request.ApiBase) ? saved.ApiBase : request.ApiBase.Trim();
        string appId = string.IsNullOrWhiteSpace(request.AppId) ? saved.AppId : request.AppId.Trim();
        string clientSecret = string.IsNullOrWhiteSpace(request.ClientSecret) ? saved.ClientSecret : request.ClientSecret.Trim();
        bool sandbox = request.Sandbox ?? saved.Sandbox;

        if (appId.Length == 0)
            return Results.Json(new { success = false, error = "未填写 AppID" }, statusCode: StatusCodes.Status400BadRequest);

        if (clientSecret.Length == 0)
            return Results.Json(new { success = false, error = "未填写 AppSecret" }, statusCode: StatusCodes.Status400BadRequest);

        // 复用配置类自身的地址推导（含沙箱前缀），但不落盘、不影响正在运行的连接
        var probe = new OfficialQqOptions
        {
            ApiBase = apiBase,
            AppId = appId,
            ClientSecret = clientSecret,
            Sandbox = sandbox,
        };

        string url = $"{probe.ResolveApiBase()}/app/getAppAccessToken";
        bool ok = false;
        int expiresIn = 0;
        string detail;

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(new { appId, clientSecret }),
                Encoding.UTF8, "application/json");

            using var resp = await http.PostAsync(url, content, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // 注意：官方在 AppID/密钥有误时**同样返回 HTTP 200**，
                // 真正的判定依据是 body 里的 code（如 10004「机器人不存在」）。
                if (root.TryGetProperty("code", out var codeEl)
                    && codeEl.ValueKind == JsonValueKind.Number
                    && codeEl.GetInt32() != 0)
                {
                    string? apiMessage = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
                    detail = $"开放平台返回错误：{apiMessage ?? "未知错误"}（code={codeEl.GetInt32()}）";
                }
                else if (root.TryGetProperty("access_token", out var tokenEl) && !string.IsNullOrWhiteSpace(tokenEl.GetString()))
                {
                    ok = true;

                    if (root.TryGetProperty("expires_in", out var expEl))
                    {
                        expiresIn = expEl.ValueKind switch
                        {
                            JsonValueKind.Number when expEl.TryGetInt32(out int n) => n,
                            JsonValueKind.String when int.TryParse(expEl.GetString(), out int n2) => n2,
                            _ => 0,
                        };
                    }

                    detail = expiresIn > 0
                        ? $"凭证有效，AccessToken 有效期 {expiresIn} 秒（约 {expiresIn / 60} 分钟）"
                        : "凭证有效，已成功获取 AccessToken";
                }
                else
                {
                    detail = $"响应中缺少 access_token：{Truncate(body)}";
                }
            }
            else
            {
                detail = DescribeHttpError(body, (int)resp.StatusCode);
            }
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            detail = "请求超时：请确认该机器能访问开放平台";
        }
        catch (Exception ex)
        {
            detail = $"请求失败：{ex.Message}";
        }

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "bot.test-official-auth",
            Target = "QQ 开放平台凭证",
            Detail = $"AppID {appId} @ {probe.ResolveApiBase()} → {(ok ? "成功" : "失败：" + detail)}",
            Success = ok,
        }, ct);

        return Results.Json(new { success = ok, message = detail, expiresIn });
    }

    private static string DescribeHttpError(string body, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            string? code = root.TryGetProperty("code", out var c) ? c.ToString() : null;

            if (!string.IsNullOrEmpty(message))
                return $"HTTP {statusCode}：{message}（code={code}）";
        }
        catch (JsonException)
        {
            // 落到通用描述
        }

        return $"HTTP {statusCode}：{Truncate(body)}";
    }

    private static string Truncate(string value)
        => string.IsNullOrEmpty(value) ? "(空)" : (value.Length > 300 ? value[..300] + "..." : value);

    private static string DescribeMode(BotPlatform mode)
        => mode == BotPlatform.OfficialQq ? "QQ 官方 Bot API" : "NapCat / OneBot 11";

    private static long[]? NormalizeLongs(long[]? values)
        => values is null ? null : values.Where(v => v > 0).Distinct().ToArray();

    private static string[]? NormalizeStrings(string[]? values)
        => values is null
            ? null
            : values.Select(v => v?.Trim() ?? "")
                    .Where(v => v.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

    // ---------------- 官方指令面板 API ----------------

    private static async Task<IResult> GetOfficialPanelsAsync(
        HttpContext ctx,
        PanelAuthService auth,
        OfficialQqBotClient officialClient,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        try
        {
            var panels = await officialClient.GetPanelsAsync(null, ct);
            return Results.Json(new { success = true, panels });
        }
        catch (Exception ex)
        {
            return Results.Json(new { success = false, message = ex.Message, panels = Array.Empty<OfficialPanelRecord>() });
        }
    }

    private static async Task<IResult> SyncOfficialPanelsAsync(
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        OfficialQqBotClient officialClient,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        OfficialPanelSyncResult result;
        try
        {
            result = await officialClient.SyncCommandPanelsAsync(ct);
        }
        catch (Exception ex)
        {
            result = new OfficialPanelSyncResult(
                Success: false,
                GroupPanelId: null,
                GroupAction: null,
                GroupItemCount: 0,
                C2cPanelId: null,
                C2cAction: null,
                C2cItemCount: 0,
                Message: ex.Message);
        }

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "bot.sync-official-panels",
            Target = "QQ 官方指令面板",
            Detail = result.Success
                ? $"群聊面板 {result.GroupPanelId}（{result.GroupAction}，{result.GroupItemCount} 项），单聊面板 {result.C2cPanelId}（{result.C2cAction}，{result.C2cItemCount} 项）"
                : $"同步失败：{result.Message}",
            Success = result.Success,
        }, ct);

        return Results.Json(result);
    }

    private static async Task<IResult> DeleteOfficialPanelAsync(
        string panelId,
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        OfficialQqBotClient officialClient,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        if (string.IsNullOrWhiteSpace(panelId))
            return Results.BadRequest(new { success = false, message = "缺少面板 ID" });

        bool ok = false;
        string? message = null;
        try
        {
            ok = await officialClient.DeletePanelAsync(panelId.Trim(), ct);
            message = "指令面板已成功删除";
        }
        catch (Exception ex)
        {
            message = ex.Message;
        }

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "bot.delete-official-panel",
            Target = panelId,
            Detail = ok ? "成功删除官方指令面板" : $"删除失败：{message}",
            Success = ok,
        }, ct);

        return Results.Json(new { success = ok, message });
    }
}
