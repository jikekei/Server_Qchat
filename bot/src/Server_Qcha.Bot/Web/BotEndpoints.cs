using Server.Qcat.Bot;
using Server.Qcat.Configuration;

namespace Server.Qcat.Web;

// ---------------- 请求体 ----------------

public sealed record UpdateBotSettingsRequest(
    string? WsBaseUri,
    int? ReconnectDelaySeconds,
    int? MaxReconnectDelaySeconds,
    long[]? AllowedGroupIds,
    long[]? NotifyGroupIds,
    long[]? NotifyPrivateUserIds,
    long? AcTargetGroupId,
    bool? AutoReconnect);

public sealed record SendTestMessageRequest(bool IsGroup, long TargetId, string? Message);

/// <summary>
/// QQ 机器人状态查看、配置管理、重连与测试发送 API。
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
    }

    private static async Task<IResult> GetStatusAsync(
        HttpContext ctx,
        PanelAuthService auth,
        GoCqHttpBotService botService,
        BotSettingsStore store,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        var settings = store.LoadCurrent();

        IReadOnlyList<BotGroupSummary> groups = Array.Empty<BotGroupSummary>();
        IReadOnlyList<BotFriendSummary> friends = Array.Empty<BotFriendSummary>();

        if (botService.IsConnected)
        {
            groups = await botService.GetGroupsAsync(ct);
            friends = await botService.GetFriendsAsync(ct);
        }

        return Results.Json(new
        {
            status = new
            {
                isConnected = botService.IsConnected,
                wsBaseUri = settings.GoCqHttp.WsBaseUri,
                lastConnectedAt = botService.LastConnectedAt,
                lastDisconnectedAt = botService.LastDisconnectedAt,
                lastError = botService.LastError,
                currentAttempt = botService.CurrentAttempt,
                nextReconnectInSeconds = botService.NextReconnectInSeconds,
                connectedUserId = botService.ConnectedUserId,
                connectedNickname = botService.ConnectedNickname,
            },
            settings = new
            {
                wsBaseUri = settings.GoCqHttp.WsBaseUri,
                reconnectDelaySeconds = settings.GoCqHttp.ReconnectDelaySeconds,
                maxReconnectDelaySeconds = settings.GoCqHttp.MaxReconnectDelaySeconds,
                allowedGroupIds = settings.Bot.AllowedGroupIds,
                notifyGroupIds = settings.Bot.NotifyGroupIds,
                notifyPrivateUserIds = settings.Bot.NotifyPrivateUserIds,
                acTargetGroupId = settings.Bot.AcTargetGroupId,
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
        GoCqHttpBotService botService,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        // ---- 校验参数 ----
        if (string.IsNullOrWhiteSpace(request.WsBaseUri) ||
            (!request.WsBaseUri.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
             !request.WsBaseUri.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Json(new { error = "WebSocket 地址不合法，必须以 ws:// 或 wss:// 开头" }, statusCode: StatusCodes.Status400BadRequest);
        }

        int reconnectDelay = request.ReconnectDelaySeconds is > 0 and <= 300
            ? request.ReconnectDelaySeconds.Value
            : 5;

        int maxReconnectDelay = request.MaxReconnectDelaySeconds is > 0 and <= 3600
            ? request.MaxReconnectDelaySeconds.Value
            : 30;

        if (maxReconnectDelay < reconnectDelay)
        {
            return Results.Json(new { error = "最大重连间隔不能小于基础重连间隔" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var current = store.LoadCurrent();
        bool wsChanged = !string.Equals(current.GoCqHttp.WsBaseUri.Trim(), request.WsBaseUri.Trim(), StringComparison.OrdinalIgnoreCase);

        var model = new BotSettingsStore.BotSettingsModel
        {
            GoCqHttp = new GoCqHttpOptions
            {
                WsBaseUri = request.WsBaseUri.Trim(),
                ReconnectDelaySeconds = reconnectDelay,
                MaxReconnectDelaySeconds = maxReconnectDelay,
            },
            Bot = new BotOptions
            {
                AllowedGroupIds = request.AllowedGroupIds?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<long>(),
                NotifyGroupIds = request.NotifyGroupIds?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<long>(),
                NotifyPrivateUserIds = request.NotifyPrivateUserIds?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<long>(),
                AcTargetGroupId = request.AcTargetGroupId is > 0 ? request.AcTargetGroupId.Value : 0,
            }
        };

        store.Save(model);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "bot.config-update",
            Target = "QQ Bot 配置",
            Detail = $"WS: {model.GoCqHttp.WsBaseUri}, 允许群: [{string.Join(",", model.Bot.AllowedGroupIds)}], AC目标群: {model.Bot.AcTargetGroupId}",
            Success = true,
        }, ct);

        // 若 WS 地址变更且开启了自动重连或显式指定了重连
        if (wsChanged || request.AutoReconnect == true)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(300); // 略微等待文件重载生效
                await botService.ReconnectAsync();
            });
        }

        return Results.Json(new
        {
            success = true,
            message = wsChanged ? "配置已保存并已发起重新连接" : "配置已保存，内存中即刻生效",
            wsChanged,
        });
    }

    private static async Task<IResult> ReconnectAsync(
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        GoCqHttpBotService botService,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        await botService.ReconnectAsync();

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "bot.reconnect",
            Target = "QQ Bot 连接",
            Detail = "主动触发重新连接 NapCatQQ / OneBot 11",
            Success = true,
        }, ct);

        return Results.Json(new { success = true, message = "已发起重新连接" });
    }

    private static async Task<IResult> SendTestMessageAsync(
        SendTestMessageRequest request,
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        GoCqHttpBotService botService,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.BotManage);
        if (error is not null)
            return error;

        if (request.TargetId <= 0)
            return Results.Json(new { error = "目标 ID 不合法" }, statusCode: StatusCodes.Status400BadRequest);

        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.Json(new { error = "消息内容不能为空" }, statusCode: StatusCodes.Status400BadRequest);

        string msg = request.Message.Trim();
        if (msg.Length > 1000)
            return Results.Json(new { error = "消息内容过长（上限 1000 字符）" }, statusCode: StatusCodes.Status400BadRequest);

        var (success, messageId, err) = await botService.SendTestMessageAsync(request.IsGroup, request.TargetId, msg, ct);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "bot.test-message",
            Target = request.IsGroup ? $"群 {request.TargetId}" : $"私聊 {request.TargetId}",
            Detail = $"内容: {(msg.Length > 60 ? msg[..60] + "..." : msg)}",
            Success = success,
        }, ct);

        if (!success)
            return Results.Json(new { success = false, error = err }, statusCode: StatusCodes.Status500InternalServerError);

        return Results.Json(new { success = true, messageId, message = "测试消息已成功发送" });
    }
}
