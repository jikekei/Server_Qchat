using System.Text.RegularExpressions;
using Server.Qcat.Configuration;
using Server.Qcat.Data;

namespace Server.Qcat.Web;

// ---------------- 请求体 ----------------

public sealed record UpdateDbConfigRequest(string? ConnectionString, bool? TestFirst);
public sealed record TestDbConnectionRequest(string? ConnectionString);

public sealed record SavePlayerRequest(
    string? Id,
    string? PlayerName,
    int? ScpsKilled,
    int? PlayersKilled,
    int? PlayTimeSeconds,
    int? Deaths,
    string? AdminNote,
    bool? IsAdmin,
    long? QqId);

public sealed record SaveBanRequest(
    string? Id,
    string? PlayerIP,
    DateTime? UnbanTime,
    int? DurationSeconds,
    string? AdminId,
    string? AdminName,
    string? Reason);

/// <summary>
/// 数据库功能 API（PlayerData 玩家统计、BanPlayerData 封禁记录与连接维护）。
/// 权限：<see cref="PanelPermission.DatabaseManage"/>（database.manage）。
/// </summary>
public static class DatabaseEndpoints
{
    private static readonly DateTime PermanentUnbanTime = new(9999, 12, 31, 23, 59, 59);

    public static void MapDatabaseApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/db");

        // ---- 连接与概览 ----
        api.MapGet("/status", GetStatusAsync);
        api.MapPost("/config", UpdateConfigAsync);
        api.MapPost("/test-connection", TestConnectionAsync);
        api.MapPost("/init-tables", InitTablesAsync);

        // ---- 玩家数据 (PlayerData) ----
        api.MapGet("/players", GetPlayersAsync);
        api.MapGet("/players/{id}", GetPlayerByIdAsync);
        api.MapPost("/players", SavePlayerAsync);
        api.MapDelete("/players/{id}", DeletePlayerAsync);
        api.MapGet("/players/rankings", GetRankingsAsync);

        // ---- 封禁数据 (BanPlayerData) ----
        api.MapGet("/bans", GetBansAsync);
        api.MapGet("/bans/{id}", GetBanByIdAsync);
        api.MapPost("/bans", SaveBanAsync);
        api.MapDelete("/bans/{id}", DeleteBanAsync);
    }

    // ==================== 连接与概览 ====================

    /// <summary>对连接字符串中的密码字段进行脱敏（替换为 ******）。</summary>
    private static string MaskConnectionString(string? cs)
    {
        if (string.IsNullOrWhiteSpace(cs))
            return "";
        return Regex.Replace(cs, @"(?i)(Password|Pwd)\s*=\s*[^;]+", "$1=******");
    }

    /// <summary>
    /// 若输入连接串包含脱敏掩码 ******，则从当前配置中复用真实密码。
    /// 若用户显式输入了新密码或清空，则沿用输入。
    /// </summary>
    private static string ResolveConnectionString(string? input, string? current)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";
        input = input.Trim();
        if (input.Contains("******") && !string.IsNullOrWhiteSpace(current))
        {
            var match = Regex.Match(current, @"(?i)(Password|Pwd)\s*=\s*([^;]+)");
            if (match.Success)
            {
                string actualPwd = match.Groups[2].Value;
                return Regex.Replace(input, @"(?i)(Password|Pwd)\s*=\s*\*{6}", $"$1={actualPwd}");
            }
        }
        return input;
    }

    private static async Task<IResult> GetStatusAsync(
        HttpContext ctx,
        PanelAuthService auth,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        var summary = await repo.GetSummaryAsync(ct);

        return Results.Json(new
        {
            summary,
            connectionString = MaskConnectionString(repo.CurrentConnectionString),
            hasConfigured = !string.IsNullOrWhiteSpace(repo.CurrentConnectionString),
        });
    }

    private static async Task<IResult> UpdateConfigAsync(
        UpdateDbConfigRequest request,
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        BotSettingsStore store,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        string cs = ResolveConnectionString(request.ConnectionString, repo.CurrentConnectionString);

        if (request.TestFirst == true && !string.IsNullOrWhiteSpace(cs))
        {
            var (success, testErr, _, _, _) = await repo.TestConnectionAsync(cs, ct);
            if (!success)
            {
                return Results.Json(new { error = $"连接测试失败：{testErr}" }, statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var current = store.LoadCurrent();
        current.MySql = new MySqlOptions { ConnectionString = cs };
        store.Save(current);

        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username,
            Action = "db.config-update",
            Target = "MySQL 数据库配置",
            Detail = string.IsNullOrWhiteSpace(cs) ? "清空连接串" : "更新数据库连接串",
            Success = true,
        }, ct);

        return Results.Json(new { success = true, message = "数据库连接配置已保存并生效" });
    }

    private static async Task<IResult> TestConnectionAsync(
        TestDbConnectionRequest request,
        HttpContext ctx,
        PanelAuthService auth,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        string cs = ResolveConnectionString(request.ConnectionString, repo.CurrentConnectionString);
        var res = await repo.TestConnectionAsync(cs, ct);
        return Results.Json(new
        {
            success = res.Success,
            error = res.Error,
            elapsedMs = res.ElapsedMs,
            playerDataExists = res.PlayerDataExists,
            banPlayerDataExists = res.BanPlayerDataExists,
        });
    }

    private static async Task<IResult> InitTablesAsync(
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        try
        {
            await repo.InitializeTablesAsync(ct);

            await db.AddAuditAsync(new PanelAuditEntry
            {
                Username = session!.Username,
                Action = "db.init-tables",
                Target = "MySQL 数据表",
                Detail = "初始化 PlayerData 和 BanPlayerData 数据表结构",
                Success = true,
            }, ct);

            return Results.Json(new { success = true, message = "数据表初始化成功 (PlayerData, BanPlayerData)" });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"初始化表结构失败：{ex.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ==================== 玩家统计 (PlayerData) ====================

    private static async Task<IResult> GetPlayersAsync(
        HttpContext ctx,
        PanelAuthService auth,
        GameDbRepository repo,
        string? search,
        int? page,
        int? pageSize,
        string? sortBy,
        bool? sortDesc,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        try
        {
            var (items, total) = await repo.GetPlayersAsync(search, page ?? 1, pageSize ?? 20, sortBy, sortDesc ?? true, ct);
            return Results.Json(new
            {
                items = items.Select(p => new
                {
                    id = p.Id,
                    playerName = p.PlayerName,
                    scpsKilled = p.ScpsKilled,
                    playersKilled = p.PlayersKilled,
                    totalKills = p.ScpsKilled + p.PlayersKilled,
                    playTimeSeconds = p.PlayTimeSeconds,
                    deaths = p.Deaths,
                    kd = p.Deaths > 0 ? Math.Round((double)(p.ScpsKilled + p.PlayersKilled) / p.Deaths, 2) : (p.ScpsKilled + p.PlayersKilled),
                    adminNote = p.AdminNote,
                    isAdmin = p.IsAdmin,
                    qqId = p.QqId,
                }),
                total,
                page = page ?? 1,
                pageSize = pageSize ?? 20,
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"获取玩家列表失败：{ex.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetPlayerByIdAsync(
        string id,
        HttpContext ctx,
        PanelAuthService auth,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        var player = await repo.GetPlayerByIdAsync(id, ct);
        if (player is null)
            return Results.Json(new { error = "玩家未找到" }, statusCode: StatusCodes.Status404NotFound);

        return Results.Json(player);
    }

    private static async Task<IResult> SavePlayerAsync(
        SavePlayerRequest request,
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        if (string.IsNullOrWhiteSpace(request.Id))
            return Results.Json(new { error = "玩家 ID (SteamID) 不能为空" }, statusCode: StatusCodes.Status400BadRequest);
        if (string.IsNullOrWhiteSpace(request.PlayerName))
            return Results.Json(new { error = "玩家昵称不能为空" }, statusCode: StatusCodes.Status400BadRequest);

        var player = new PlayerStats(
            request.Id.Trim(),
            request.PlayerName.Trim(),
            request.ScpsKilled ?? 0,
            request.PlayersKilled ?? 0,
            request.PlayTimeSeconds ?? 0,
            request.Deaths ?? 0,
            request.AdminNote?.Trim(),
            request.IsAdmin ?? false,
            request.QqId);

        try
        {
            await repo.SavePlayerAsync(player, ct);

            await db.AddAuditAsync(new PanelAuditEntry
            {
                Username = session!.Username,
                Action = "db.player-save",
                Target = $"{player.PlayerName} ({player.Id})",
                Detail = $"击杀: {player.PlayersKilled + player.ScpsKilled}, 游玩: {player.PlayTimeSeconds}s, 管理员: {player.IsAdmin}",
                Success = true,
            }, ct);

            return Results.Json(new { success = true, message = "玩家数据保存成功" });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"保存玩家数据失败：{ex.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DeletePlayerAsync(
        string id,
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        try
        {
            bool deleted = await repo.DeletePlayerAsync(id, ct);
            if (!deleted)
                return Results.Json(new { error = "玩家未找到或已删除" }, statusCode: StatusCodes.Status404NotFound);

            await db.AddAuditAsync(new PanelAuditEntry
            {
                Username = session!.Username,
                Action = "db.player-delete",
                Target = id,
                Detail = "删除玩家统计数据",
                Success = true,
            }, ct);

            return Results.Json(new { success = true, message = "玩家数据已删除" });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"删除玩家失败：{ex.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetRankingsAsync(
        HttpContext ctx,
        PanelAuthService auth,
        GameDbRepository repo,
        string? metric,
        int? limit,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        try
        {
            var rankings = await repo.GetPlayerRankingsAsync(metric ?? "playtime", limit ?? 10, ct);
            return Results.Json(new { items = rankings });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"获取排行榜失败：{ex.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ==================== 封禁管理 (BanPlayerData) ====================

    private static async Task<IResult> GetBansAsync(
        HttpContext ctx,
        PanelAuthService auth,
        GameDbRepository repo,
        string? search,
        bool? activeOnly,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        try
        {
            var (items, total) = await repo.GetBansAsync(search, activeOnly, page ?? 1, pageSize ?? 20, ct);
            return Results.Json(new
            {
                items = items.Select(b => new
                {
                    id = b.Id,
                    playerIP = b.PlayerIP,
                    unbanTime = b.UnbanTime,
                    adminId = b.AdminId,
                    adminName = b.AdminName,
                    reason = b.Reason,
                    isActive = b.IsActive,
                    isPermanent = b.UnbanTime.Year >= 9999,
                }),
                total,
                page = page ?? 1,
                pageSize = pageSize ?? 20,
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"获取封禁列表失败：{ex.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetBanByIdAsync(
        string id,
        HttpContext ctx,
        PanelAuthService auth,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        var ban = await repo.GetBanByIdAsync(id, ct);
        if (ban is null)
            return Results.Json(new { error = "封禁记录未找到" }, statusCode: StatusCodes.Status404NotFound);

        return Results.Json(ban);
    }

    private static async Task<IResult> SaveBanAsync(
        SaveBanRequest request,
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        if (string.IsNullOrWhiteSpace(request.Id))
            return Results.Json(new { error = "封禁玩家 ID 不能为空" }, statusCode: StatusCodes.Status400BadRequest);

        DateTime unbanTime;
        if (request.UnbanTime.HasValue)
        {
            unbanTime = request.UnbanTime.Value;
        }
        else if (request.DurationSeconds.HasValue && request.DurationSeconds.Value > 0)
        {
            unbanTime = DateTime.Now.AddSeconds(request.DurationSeconds.Value);
        }
        else
        {
            unbanTime = PermanentUnbanTime;
        }

        string adminId = string.IsNullOrWhiteSpace(request.AdminId) ? session!.Username : request.AdminId.Trim();
        string adminName = string.IsNullOrWhiteSpace(request.AdminName) ? session!.DisplayName : request.AdminName.Trim();
        string reason = string.IsNullOrWhiteSpace(request.Reason) ? "管理员封禁" : request.Reason.Trim();
        string ip = string.IsNullOrWhiteSpace(request.PlayerIP) ? "0.0.0.0" : request.PlayerIP.Trim();

        var ban = new BanRecord(request.Id.Trim(), ip, unbanTime, adminId, adminName, reason, unbanTime > DateTime.Now);

        try
        {
            await repo.SaveBanAsync(ban, ct);

            await db.AddAuditAsync(new PanelAuditEntry
            {
                Username = session!.Username,
                Action = "db.ban-save",
                Target = ban.Id,
                Detail = $"解封时间: {ban.UnbanTime:yyyy-MM-dd HH:mm:ss}, 原因: {ban.Reason}",
                Success = true,
            }, ct);

            return Results.Json(new { success = true, message = "封禁记录保存成功" });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"保存封禁记录失败：{ex.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DeleteBanAsync(
        string id,
        HttpContext ctx,
        PanelAuthService auth,
        PanelDatabase db,
        GameDbRepository repo,
        CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.DatabaseManage);
        if (error is not null)
            return error;

        try
        {
            bool deleted = await repo.DeleteBanAsync(id, ct);
            if (!deleted)
                return Results.Json(new { error = "封禁记录未找到或已解封" }, statusCode: StatusCodes.Status404NotFound);

            await db.AddAuditAsync(new PanelAuditEntry
            {
                Username = session!.Username,
                Action = "db.ban-delete",
                Target = id,
                Detail = "管理员在面板解封玩家",
                Success = true,
            }, ct);

            return Results.Json(new { success = true, message = "玩家已解封（封禁记录已移除）" });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"解封失败：{ex.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
