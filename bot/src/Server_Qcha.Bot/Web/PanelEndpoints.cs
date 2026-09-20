using System.Text.RegularExpressions;
using Server.Qcat.Bot;
using Server.Qcat.Configuration;
using Server.Qcat.Data;
using Server.Qcat.Services;
using Server.Qcat.Socket;

namespace Server.Qcat.Web;

// ---------------- 请求体 ----------------

public sealed record LoginRequest(string? Username, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record BroadcastRequest(string? Message);
public sealed record KickRequest(string? PlayerId, string? Reason, int? Duration);
public sealed record BanRequest(string? PlayerId, string? Reason, int? Duration);
public sealed record CreateAccountRequest(string? Username, string? Password, string? DisplayName, string[]? PermissionKeys, bool? IsEnabled);
public sealed record UpdateAccountRequest(string? DisplayName, string[]? PermissionKeys, bool? IsEnabled);
public sealed record ResetPasswordRequest(string? Password);

/// <summary>
/// Web 面板 HTTP API。
/// 所有写操作都会记录审计日志；权限按 <see cref="PanelPermission"/> 校验。
/// </summary>
public static class PanelEndpoints
{
    public static void MapPanelApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ---- 首页总览与玩家统计 ----
        api.MapGet("/overview", GetOverviewAsync);
        api.MapGet("/stats/player-history", GetPlayerHistoryAsync);

        // ---- 认证 ----
        api.MapPost("/auth/login", LoginAsync);
        api.MapPost("/auth/logout", Logout);
        api.MapGet("/auth/me", MeAsync);
        api.MapPost("/auth/password", ChangeOwnPasswordAsync);

        // ---- 元数据 ----
        api.MapGet("/meta", GetMeta);

        // ---- 服务器（只读）----
        api.MapGet("/servers", GetServersAsync);
        api.MapGet("/servers/{index:int}/players", GetPlayersAsync);
        api.MapGet("/servers/{index:int}/info", GetInfoAsync);

        // ---- 服务器操作 ----
        api.MapPost("/servers/{index:int}/broadcast", BroadcastAsync);
        api.MapPost("/servers/{index:int}/kick", KickAsync);
        api.MapPost("/servers/{index:int}/ban", BanAsync);
        api.MapPost("/servers/{index:int}/round/start", StartRoundAsync);
        api.MapPost("/servers/{index:int}/round/restart", RestartRoundAsync);

        // ---- 账号管理 ----
        api.MapGet("/accounts", ListAccountsAsync);
        api.MapPost("/accounts", CreateAccountAsync);
        api.MapPut("/accounts/{id:long}", UpdateAccountAsync);
        api.MapDelete("/accounts/{id:long}", DeleteAccountAsync);
        api.MapPost("/accounts/{id:long}/password", ResetAccountPasswordAsync);

        // ---- 审计 ----
        api.MapGet("/audit", GetAuditAsync);
    }

    // ==================== 首页总览与统计 ====================

    private static async Task<IResult> GetOverviewAsync(
        HttpContext ctx,
        PanelAuthService auth,
        ServerRegistry registry,
        PlayerHistoryTracker historyTracker,
        GoCqHttpBotService botService,
        BotSettingsStore botStore,
        GameDbRepository dbRepo,
        CancellationToken ct)
    {
        var (_, error) = await AuthorizeAsync(ctx, auth, PanelPermission.ServersView);
        if (error is not null)
            return error;

        var sorted = registry.GetSorted();
        int totalServers = sorted.Count;
        int onlineServers = sorted.Count(s => s.IsOnline);

        var serverList = sorted.Select((s, i) =>
        {
            var (online, max) = historyTracker.GetServerOnline(s.ConnectHost, s.Port);
            return new
            {
                index = i + 1,
                name = s.Name,
                connectHost = s.ConnectHost,
                port = s.Port,
                gamePort = s.GamePort,
                sortOrder = s.SortOrder,
                isStatic = s.IsStatic,
                isOnline = s.IsOnline,
                onlinePlayers = online,
                maxPlayers = max,
                lastHeartbeat = s.LastHeartbeat,
            };
        }).ToList();

        int totalOnlinePlayers = serverList.Where(s => s.isOnline).Sum(s => s.onlinePlayers);
        int peakToday = Math.Max(totalOnlinePlayers, historyTracker.PeakToday);

        object? dbSummary = null;
        if (!string.IsNullOrWhiteSpace(dbRepo.CurrentConnectionString))
        {
            try
            {
                dbSummary = await dbRepo.GetSummaryAsync(ct);
            }
            catch { }
        }

        var botSettings = botStore.LoadCurrent();

        var history = historyTracker.GetHistory(60).Select(h => new
        {
            timestamp = h.Timestamp,
            timeLabel = h.TimeLabel,
            totalOnline = h.TotalOnline,
            perServer = h.PerServer
        }).ToList();

        return Results.Json(new
        {
            stats = new
            {
                totalServers,
                onlineServers,
                totalOnlinePlayers,
                peakToday,
                botConnected = botService.IsConnected,
                botUserId = botService.ConnectedUserId,
                botNickname = botService.ConnectedNickname,
                botAllowedGroups = botSettings.Bot.AllowedGroupIds?.Length ?? 0,
                dbConfigured = !string.IsNullOrWhiteSpace(dbRepo.CurrentConnectionString),
                dbSummary
            },
            servers = serverList,
            history
        });
    }

    private static async Task<IResult> GetPlayerHistoryAsync(
        int? points,
        HttpContext ctx,
        PanelAuthService auth,
        PlayerHistoryTracker historyTracker,
        CancellationToken ct)
    {
        var (_, error) = await AuthorizeAsync(ctx, auth, PanelPermission.ServersView);
        if (error is not null)
            return error;

        int count = Math.Clamp(points ?? 60, 10, 1440);
        var history = historyTracker.GetHistory(count).Select(h => new
        {
            timestamp = h.Timestamp,
            timeLabel = h.TimeLabel,
            totalOnline = h.TotalOnline,
            perServer = h.PerServer
        }).ToList();

        return Results.Json(new
        {
            total = history.Count,
            peakToday = historyTracker.PeakToday,
            history
        });
    }

    // ==================== 认证 ====================

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext ctx, PanelAuthService auth, PanelDatabase db, CancellationToken ct)
    {
        string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (auth.IsIpLocked(clientIp, out var remaining))
        {
            return Results.Json(new { error = $"登录失败次数过多，该 IP 已被暂时锁定，请在 {(int)Math.Ceiling(remaining.TotalMinutes)} 分钟后再试" },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var (session, error) = await auth.LoginAsync(request.Username, request.Password, ct);
        if (session is null)
        {
            bool lockedNow = auth.RecordFailedAttempt(clientIp, out var lockRemaining);

            await db.AddAuditAsync(new PanelAuditEntry
            {
                Username = string.IsNullOrWhiteSpace(request.Username) ? "unknown" : request.Username.Trim(),
                Action = "login.failed",
                Target = "web-panel",
                Detail = $"登录失败: {error} (IP: {clientIp})",
                Success = false,
            }, ct);

            if (lockedNow)
            {
                return Results.Json(new { error = $"登录失败次数过多，该 IP 已被暂时锁定，请在 {(int)Math.Ceiling(lockRemaining.TotalMinutes)} 分钟后再试" },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            return Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized);
        }

        auth.ResetFailedAttempts(clientIp);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session.Username,
            Action = "login",
            Target = "web-panel",
            Detail = $"登录成功 (IP: {clientIp})",
            Success = true,
        }, ct);

        return Results.Json(ToSessionDto(session));
    }

    private static IResult Logout(HttpContext ctx, PanelAuthService auth)
    {
        auth.Logout(ExtractToken(ctx));
        return Results.Json(new { success = true });
    }

    private static async Task<IResult> MeAsync(HttpContext ctx, PanelAuthService auth, PanelDatabase db, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.None);
        if (error is not null)
            return error;

        // 从数据库取最新权限，保证管理端改动即时生效
        var account = await db.GetAccountByIdAsync(session!.AccountId, ct);
        if (account is null || !account.IsEnabled)
            return Results.Json(new { error = "账号已被删除或禁用" }, statusCode: StatusCodes.Status401Unauthorized);

        session.Permissions = account.Permissions;
        return Results.Json(ToSessionDto(session));
    }

    private static async Task<IResult> ChangeOwnPasswordAsync(ChangePasswordRequest request, HttpContext ctx, PanelAuthService auth, PanelDatabase db, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.None);
        if (error is not null)
            return error;

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return Results.Json(new { error = "新密码长度至少 8 位" }, statusCode: StatusCodes.Status400BadRequest);

        var account = await db.GetAccountByIdAsync(session!.AccountId, ct);
        if (account is null)
            return Results.Json(new { error = "账号不存在" }, statusCode: StatusCodes.Status404NotFound);

        if (!PanelAuthService.VerifyPassword(request.CurrentPassword ?? "", account.PasswordHash))
            return Results.Json(new { error = "当前密码不正确" }, statusCode: StatusCodes.Status400BadRequest);

        await db.UpdatePasswordAsync(account.Id, PanelAuthService.HashPassword(request.NewPassword), ct);

        // 保留当前会话，踢掉该账号其它会话
        auth.PurgeSessionsExcept(account.Id, session.Token);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session.Username,
            Action = "account.change-own-password",
            Target = session.Username,
            Detail = "修改本人密码",
            Success = true,
        }, ct);

        return Results.Json(new { success = true });
    }

    // ==================== 元数据 ====================

    private static IResult GetMeta(IServerCommandGateway gateway, LocalAdmin.LocalAdminManager localAdmin)
    {
        return Results.Json(new
        {
            gateway = gateway.Name,
            permissions = PanelPermissions.All.Select(d => new
            {
                key = d.Key,
                name = d.Name,
                description = d.Description,
                value = (long)d.Value,
            }),
            presets = new object[]
            {
                new { key = "viewer", name = "只读", value = (long)PanelPermission.Viewer },
                new { key = "operator", name = "运营", value = (long)PanelPermission.Operator },
                new { key = "admin", name = "管理员", value = (long)PanelPermission.Admin },
                new { key = "owner", name = "所有者", value = (long)PanelPermission.Owner },
            },
            capabilities = new
            {
                // LocalAdmin 能力是否可用（配置开启后前端才显示「服务器进程」页）
                localAdminGateway = localAdmin.Enabled,
                serverControl = localAdmin.Enabled,
                localServerCount = localAdmin.Instances.Count,
            },
        });
    }

    // ==================== 服务器只读 ====================

    private static async Task<IResult> GetServersAsync(HttpContext ctx, PanelAuthService auth, ServerRegistry registry, CancellationToken ct)
    {
        var (_, error) = await AuthorizeAsync(ctx, auth, PanelPermission.ServersView);
        if (error is not null)
            return error;

        var servers = registry.GetSorted().Select((s, i) => new
        {
            index = i + 1,
            name = s.Name,
            connectHost = s.ConnectHost,
            port = s.Port,
            gamePort = s.GamePort,
            sortOrder = s.SortOrder,
            isStatic = s.IsStatic,
            lastHeartbeat = s.LastHeartbeat,
        }).ToList();

        return Results.Json(new { total = servers.Count, servers });
    }

    private static async Task<IResult> GetPlayersAsync(int index, HttpContext ctx, PanelAuthService auth, ServerRegistry registry, IServerCommandGateway gateway, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.ServersView);
        if (error is not null)
            return error;

        var server = ResolveServer(registry, index);
        if (server is null)
            return Results.Json(new { error = "服务器不存在或已离线" }, statusCode: StatusCodes.Status404NotFound);

        var result = await gateway.SendAsync(server, "list", ct);
        if (!result.Success)
            return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status502BadGateway);

        var players = ParsePlayerList(result.Response);

        await AuditAsync(ctx, auth, session!.Username, "players.list", server, $"{players.Count} 名玩家", result.Success, ct);

        return Results.Json(new
        {
            server = new { index, server.Name, server.ConnectHost, server.Port },
            count = players.Count,
            players,
            raw = result.Response,
        });
    }

    private static async Task<IResult> GetInfoAsync(int index, HttpContext ctx, PanelAuthService auth, ServerRegistry registry, IServerCommandGateway gateway, CancellationToken ct)
    {
        var (_, error) = await AuthorizeAsync(ctx, auth, PanelPermission.ServersView);
        if (error is not null)
            return error;

        var server = ResolveServer(registry, index);
        if (server is null)
            return Results.Json(new { error = "服务器不存在或已离线" }, statusCode: StatusCodes.Status404NotFound);

        var result = await gateway.SendAsync(server, "info", ct);
        if (!result.Success)
            return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status502BadGateway);

        return Results.Json(new { raw = result.Response });
    }

    // ==================== 服务器操作 ====================

    private static async Task<IResult> BroadcastAsync(int index, BroadcastRequest request, HttpContext ctx, PanelAuthService auth, ServerRegistry registry, IServerCommandGateway gateway, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.BroadcastSend);
        if (error is not null)
            return error;

        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.Json(new { error = "广播内容不能为空" }, statusCode: StatusCodes.Status400BadRequest);

        var server = ResolveServer(registry, index);
        if (server is null)
            return Results.Json(new { error = "服务器不存在或已离线" }, statusCode: StatusCodes.Status404NotFound);

        string message = request.Message.Trim();
        var result = await gateway.SendAsync(server, $"bc&{message}", ct);

        await AuditAsync(ctx, auth, session!.Username, "broadcast", server, message, result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> KickAsync(int index, KickRequest request, HttpContext ctx, PanelAuthService auth, ServerRegistry registry, IServerCommandGateway gateway, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.PlayersKick);
        if (error is not null)
            return error;

        if (string.IsNullOrWhiteSpace(request.PlayerId))
            return Results.Json(new { error = "玩家 ID 不能为空" }, statusCode: StatusCodes.Status400BadRequest);

        var server = ResolveServer(registry, index);
        if (server is null)
            return Results.Json(new { error = "服务器不存在或已离线" }, statusCode: StatusCodes.Status404NotFound);

        // 服务端 kick 命令的时长单位为秒；"踢出"使用较短时长
        int duration = request.Duration is > 0 ? request.Duration.Value : 300;
        string reason = string.IsNullOrWhiteSpace(request.Reason) ? "被管理员踢出" : request.Reason.Trim();

        var result = await gateway.SendAsync(server, $"kick&{request.PlayerId.Trim()}&{reason}&{duration}", ct);

        await AuditAsync(ctx, auth, session!.Username, "players.kick", server,
            $"玩家ID={request.PlayerId} 时长={duration}s 原因={reason}", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> BanAsync(int index, BanRequest request, HttpContext ctx, PanelAuthService auth, ServerRegistry registry, IServerCommandGateway gateway, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.PlayersBan);
        if (error is not null)
            return error;

        if (string.IsNullOrWhiteSpace(request.PlayerId))
            return Results.Json(new { error = "玩家 ID 不能为空" }, statusCode: StatusCodes.Status400BadRequest);

        int duration = request.Duration is > 0 ? request.Duration.Value : 3600;
        if (duration > 315_360_000)
            return Results.Json(new { error = "封禁时长过长（上限 10 年）" }, statusCode: StatusCodes.Status400BadRequest);

        var server = ResolveServer(registry, index);
        if (server is null)
            return Results.Json(new { error = "服务器不存在或已离线" }, statusCode: StatusCodes.Status404NotFound);

        string reason = string.IsNullOrWhiteSpace(request.Reason) ? "违反服务器规则" : request.Reason.Trim();
        var result = await gateway.SendAsync(server, $"kick&{request.PlayerId.Trim()}&{reason}&{duration}", ct);

        await AuditAsync(ctx, auth, session!.Username, "players.ban", server,
            $"玩家ID={request.PlayerId} 时长={duration}s 原因={reason}", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> StartRoundAsync(int index, HttpContext ctx, PanelAuthService auth, ServerRegistry registry, IServerCommandGateway gateway, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.RoundControl);
        if (error is not null)
            return error;

        var server = ResolveServer(registry, index);
        if (server is null)
            return Results.Json(new { error = "服务器不存在或已离线" }, statusCode: StatusCodes.Status404NotFound);

        var result = await gateway.SendAsync(server, "start", ct);
        await AuditAsync(ctx, auth, session!.Username, "round.start", server, "强制开始回合", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> RestartRoundAsync(int index, HttpContext ctx, PanelAuthService auth, ServerRegistry registry, IServerCommandGateway gateway, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.RoundControl);
        if (error is not null)
            return error;

        var server = ResolveServer(registry, index);
        if (server is null)
            return Results.Json(new { error = "服务器不存在或已离线" }, statusCode: StatusCodes.Status404NotFound);

        var result = await gateway.SendAsync(server, "rest", ct);
        await AuditAsync(ctx, auth, session!.Username, "round.restart", server, "重启回合", result.Success, ct);
        return ToActionResult(result);
    }

    // ==================== 账号管理 ====================

    private static async Task<IResult> ListAccountsAsync(HttpContext ctx, PanelAuthService auth, PanelDatabase db, CancellationToken ct)
    {
        var (_, error) = await AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
        if (error is not null)
            return error;

        var accounts = await db.ListAccountsAsync(ct);
        return Results.Json(accounts.Select(ToAccountDto).ToList());
    }

    private static async Task<IResult> CreateAccountAsync(CreateAccountRequest request, HttpContext ctx, PanelAuthService auth, PanelDatabase db, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
        if (error is not null)
            return error;

        string username = (request.Username ?? "").Trim();
        if (username.Length < 3)
            return Results.Json(new { error = "用户名至少 3 个字符" }, statusCode: StatusCodes.Status400BadRequest);
        if (username.Length > 32)
            return Results.Json(new { error = "用户名最多 32 个字符" }, statusCode: StatusCodes.Status400BadRequest);

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return Results.Json(new { error = "密码长度至少 8 位" }, statusCode: StatusCodes.Status400BadRequest);

        if (await db.GetAccountByUsernameAsync(username, ct) is not null)
            return Results.Json(new { error = "用户名已存在" }, statusCode: StatusCodes.Status409Conflict);

        var permissions = PanelPermissions.FromKeys(request.PermissionKeys);
        long id = await db.CreateAccountAsync(new PanelAccount
        {
            Username = username,
            PasswordHash = PanelAuthService.HashPassword(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName!.Trim(),
            Permissions = permissions,
            IsEnabled = request.IsEnabled ?? true,
            IsBuiltIn = false,
            CreatedAt = DateTime.UtcNow,
        }, ct);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "account.create",
            Target = username,
            Detail = $"权限={string.Join(",", PanelPermissions.ToKeys(permissions))}",
            Success = true,
        }, ct);

        var created = await db.GetAccountByIdAsync(id, ct);
        return Results.Json(ToAccountDto(created!));
    }

    private static async Task<IResult> UpdateAccountAsync(long id, UpdateAccountRequest request, HttpContext ctx, PanelAuthService auth, PanelDatabase db, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
        if (error is not null)
            return error;

        var account = await db.GetAccountByIdAsync(id, ct);
        if (account is null)
            return Results.Json(new { error = "账号不存在" }, statusCode: StatusCodes.Status404NotFound);

        bool isEnabled = request.IsEnabled ?? account.IsEnabled;

        // 保护：内置账号不可被禁用，避免把自己锁在门外
        if (account.IsBuiltIn && !isEnabled)
            return Results.Json(new { error = "内置管理员账号不可禁用" }, statusCode: StatusCodes.Status400BadRequest);

        // 保护：不能禁用自己
        if (account.Id == session!.AccountId && !isEnabled)
            return Results.Json(new { error = "不能禁用当前登录的账号" }, statusCode: StatusCodes.Status400BadRequest);

        var permissions = request.PermissionKeys is null
            ? account.Permissions
            : PanelPermissions.FromKeys(request.PermissionKeys);

        string displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? account.DisplayName : request.DisplayName!.Trim();

        await db.UpdateAccountAsync(id, displayName, permissions, isEnabled, ct);
        auth.SyncSessions(id, permissions, isEnabled);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session.Username,
            Action = "account.update",
            Target = account.Username,
            Detail = $"启用={isEnabled} 权限={string.Join(",", PanelPermissions.ToKeys(permissions))}",
            Success = true,
        }, ct);

        var updated = await db.GetAccountByIdAsync(id, ct);
        return Results.Json(ToAccountDto(updated!));
    }

    private static async Task<IResult> DeleteAccountAsync(long id, HttpContext ctx, PanelAuthService auth, PanelDatabase db, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
        if (error is not null)
            return error;

        var account = await db.GetAccountByIdAsync(id, ct);
        if (account is null)
            return Results.Json(new { error = "账号不存在" }, statusCode: StatusCodes.Status404NotFound);

        if (account.IsBuiltIn)
            return Results.Json(new { error = "内置管理员账号不可删除" }, statusCode: StatusCodes.Status400BadRequest);

        if (account.Id == session!.AccountId)
            return Results.Json(new { error = "不能删除当前登录的账号" }, statusCode: StatusCodes.Status400BadRequest);

        bool removed = await db.DeleteAccountAsync(id, ct);
        if (removed)
            auth.PurgeSessions(id);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session.Username,
            Action = "account.delete",
            Target = account.Username,
            Detail = removed ? "删除成功" : "删除失败",
            Success = removed,
        }, ct);

        return Results.Json(new { success = removed });
    }

    private static async Task<IResult> ResetAccountPasswordAsync(long id, ResetPasswordRequest request, HttpContext ctx, PanelAuthService auth, PanelDatabase db, CancellationToken ct)
    {
        var (session, error) = await AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
        if (error is not null)
            return error;

        var account = await db.GetAccountByIdAsync(id, ct);
        if (account is null)
            return Results.Json(new { error = "账号不存在" }, statusCode: StatusCodes.Status404NotFound);

        string password = string.IsNullOrWhiteSpace(request.Password)
            ? PanelAuthService.GeneratePassword(12)
            : request.Password!;

        if (password.Length < 8)
            return Results.Json(new { error = "密码长度至少 8 位" }, statusCode: StatusCodes.Status400BadRequest);

        await db.UpdatePasswordAsync(id, PanelAuthService.HashPassword(password), ct);
        auth.PurgeSessions(id);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "account.reset-password",
            Target = account.Username,
            Detail = "重置密码",
            Success = true,
        }, ct);

        return Results.Json(new { success = true, password });
    }

    // ==================== 审计 ====================

    private static async Task<IResult> GetAuditAsync(int? limit, HttpContext ctx, PanelAuthService auth, PanelDatabase db, CancellationToken ct)
    {
        var (_, error) = await AuthorizeAsync(ctx, auth, PanelPermission.AuditView);
        if (error is not null)
            return error;

        var entries = await db.ListAuditAsync(limit ?? 200, ct);
        return Results.Json(entries.Select(e => new
        {
            e.Id,
            e.CreatedAt,
            e.Username,
            e.Action,
            e.Target,
            e.Detail,
            e.Success,
        }).ToList());
    }

    // ==================== 辅助 ====================

    internal static async Task<(PanelSession? Session, IResult? Error)> AuthorizeAsync(HttpContext ctx, PanelAuthService auth, PanelPermission required)
    {
        var session = auth.Validate(ExtractToken(ctx));
        if (session is null)
            return (null, Results.Json(new { error = "未登录或会话已过期" }, statusCode: StatusCodes.Status401Unauthorized));

        if (!session.Has(required))
            return (null, Results.Json(new { error = "权限不足" }, statusCode: StatusCodes.Status403Forbidden));

        return await Task.FromResult<(PanelSession?, IResult?)>((session, null));
    }

    internal static string? ExtractToken(HttpContext ctx)
    {
        string header = ctx.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return header[prefix.Length..].Trim();
        return null;
    }

    private static ServerInfo? ResolveServer(ServerRegistry registry, int index)
    {
        var list = registry.GetSorted();
        if (index < 1 || index > list.Count)
            return null;
        return list[index - 1];
    }

    private static IResult ToActionResult(ServerCommandResult result) =>
        Results.Json(new { success = result.Success, response = result.Response, error = result.Error });

    private static async Task AuditAsync(HttpContext ctx, PanelAuthService auth, string username, string action, ServerInfo server, string detail, bool success, CancellationToken ct)
    {
        var db = ctx.RequestServices.GetRequiredService<PanelDatabase>();
        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = username,
            Action = action,
            Target = $"{server.Name} ({server.ConnectHost}:{server.Port})",
            Detail = detail,
            Success = success,
        }, ct);
    }

    private static object ToSessionDto(PanelSession session) => new
    {
        token = session.Token,
        username = session.Username,
        displayName = session.DisplayName,
        permissions = (long)session.Permissions,
        permissionKeys = PanelPermissions.ToKeys(session.Permissions),
        expiresAt = session.ExpiresAt,
    };

    private static PanelAccountDto ToAccountDto(PanelAccount a) => new(
        a.Id,
        a.Username,
        a.DisplayName,
        a.Permissions,
        PanelPermissions.ToKeys(a.Permissions),
        a.IsEnabled,
        a.IsBuiltIn,
        a.CreatedAt,
        a.LastLoginAt);

    /// <summary>解析插件 list 命令返回的 "昵称-ID" 列表（昵称可能含 "-"，故从末尾切分）。</summary>
    public static List<object> ParsePlayerList(string raw)
    {
        var players = new List<object>();
        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            int sep = trimmed.LastIndexOf('-');
            string name;
            int id = -1;
            if (sep > 0 && int.TryParse(trimmed[(sep + 1)..].Trim(), out int parsedId))
            {
                name = trimmed[..sep].Trim();
                id = parsedId;
            }
            else
            {
                name = trimmed;
            }

            if (name.Equals("Dedicated Server", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Dedicated Server@", StringComparison.OrdinalIgnoreCase))
                continue;

            players.Add(new { name, id });
        }
        return players;
    }

    /// <summary>从 cx 命令返回中提取 在线/上限 人数。</summary>
    public static (int? Online, int? Max) ParseOnline(string raw)
    {
        var match = Regex.Match(raw, @"在线人数[:：]\s*(\d+)\s*/\s*(\d+)");
        if (!match.Success)
            return (null, null);

        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
    }
}
