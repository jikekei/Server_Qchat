using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin;
using Server.Qcat.LocalAdmin.Models;

namespace Server.Qcat.Web;

/// <summary>
/// LocalAdmin 能力的 Web 面板接口。
///
/// 权限：全部要求 <see cref="PanelPermission.ServerControl"/>（server.control）。
/// 审计：所有会产生副作用的操作（启停/重启/下发命令/切换心跳/增删改服务器）都会写入审计日志。
/// 架构：通过 <see cref="ILocalAdminProvider"/> 统一支持 Daemon 独立守护模式与 Embedded 内嵌模式。
/// </summary>
public static class LocalAdminEndpoints
{
    public static void MapLocalAdminApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/local");

        api.MapGet("/servers", ListServersAsync);
        api.MapGet("/executables", FindExecutablesAsync);
        // 对应官方 LocalAdmin 自带指令：lacfg / resave
        api.MapGet("/config", GetConfigAsync);
        api.MapPost("/config/resave", ResaveConfigAsync);
        api.MapPost("/servers", CreateServerAsync);
        api.MapGet("/servers/{id}", GetStatusAsync);
        api.MapPut("/servers/{id}", UpdateServerAsync);
        api.MapDelete("/servers/{id}", DeleteServerAsync);
        api.MapGet("/servers/{id}/poll", PollAsync);

        api.MapPost("/servers/{id}/start", StartAsync);
        api.MapPost("/servers/{id}/stop", StopAsync);
        api.MapPost("/servers/{id}/restart", RestartAsync);
        api.MapPost("/servers/{id}/console", ConsoleAsync);
        api.MapPost("/servers/{id}/console/clear", ClearConsoleAsync);
        api.MapPost("/servers/{id}/heartbeat", HeartbeatAsync);
        api.MapPost("/servers/{id}/console-level", ConsoleLevelAsync);
        api.MapPost("/servers/{id}/cancel-restart", CancelRestartAsync);
        api.MapGet("/daemon/status", GetDaemonStatusAsync);
        api.MapPost("/daemon/start", StartDaemonAsync);
        api.MapPost("/daemon/stop", StopDaemonAsync);
        api.MapPost("/daemon/restart", RestartDaemonAsync);
    }

    // ==================== 只读 ====================

    /// <summary>列出托管服务器及其定义。</summary>
    private static async Task<IResult> ListServersAsync(HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.ListServersAsync(ct);

        return Results.Json(new
        {
            enabled = result.Enabled,
            total = result.Total,
            source = result.Source,
            configPath = result.ConfigPath,
            consoleLevels = result.ConsoleLevels,
            servers = result.Servers,
            definitions = result.Definitions,
        });
    }

    /// <summary>探测本机可能存在的 SCPSL 服务端可执行文件，供「添加服务器」自动预填。</summary>
    private static async Task<IResult> FindExecutablesAsync(HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.FindExecutablesAsync(ct);

        return Results.Json(new
        {
            recommended = result.Recommended,
            candidates = result.Candidates,
            steamRoots = result.SteamRoots,
            probed = result.Probed,
        });
    }

    private static async Task<IResult> GetStatusAsync(string id, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var status = await provider.GetStatusAsync(id, ct);
        if (status is null)
            return NotFound(id);

        return Results.Json(status);
    }

    /// <summary>面板轮询：一次请求同时取回最新状态与增量控制台输出。</summary>
    private static async Task<IResult> PollAsync(string id, long? after, int? limit, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var poll = await provider.PollAsync(id, after, limit, ct);
        if (poll is null)
            return NotFound(id);

        return Results.Json(new
        {
            status = poll.Status,
            firstSeq = poll.FirstSeq,
            lastSeq = poll.LastSeq,
            truncated = poll.Truncated,
            lines = poll.Lines.Select(ToLineDto).ToList(),
        });
    }

    // ==================== 服务器定义的增删改 ====================

    private static async Task<IResult> CreateServerAsync(LocalServerSaveRequest request, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.CreateServerAsync(request, ct);
        string targetName = !string.IsNullOrWhiteSpace(request.Name) ? request.Name : (request.Id ?? "server");

        await AuditAsync(ctx, session!.Username, "localadmin.server-add",
            $"{targetName} ({request.ExecutablePath}:{request.GamePort})",
            "新增可托管的服务器", result.Success, ct);

        return ToSaveResult(result);
    }

    private static async Task<IResult> UpdateServerAsync(string id, LocalServerSaveRequest request, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.UpdateServerAsync(id, request, ct);
        string targetName = !string.IsNullOrWhiteSpace(request.Name) ? request.Name : id;

        await AuditAsync(ctx, session!.Username, "localadmin.server-update",
            $"{id} → {targetName} ({request.ExecutablePath}:{request.GamePort})",
            "修改服务器配置", result.Success, ct);

        return ToSaveResult(result);
    }

    private static async Task<IResult> DeleteServerAsync(string id, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.DeleteServerAsync(id, ct);

        await AuditAsync(ctx, session!.Username, "localadmin.server-delete", id, "删除服务器配置", result.Success, ct);

        return ToActionResult(result);
    }

    // ==================== 进程控制 ====================

    private static async Task<IResult> StartAsync(string id, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.StartAsync(id, ct);
        await AuditAsync(ctx, session!.Username, "localadmin.start", id, "启动服务器进程", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> StopAsync(string id, LocalStopRequest? request, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        bool force = request?.Force ?? false;
        var result = await provider.StopAsync(id, force, ct);
        await AuditAsync(ctx, session!.Username, "localadmin.stop", id,
            force ? "强制结束服务器进程" : "优雅停止服务器进程", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> RestartAsync(string id, LocalRestartRequest? request, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        bool force = request?.Force ?? false;
        var result = await provider.RestartAsync(id, force, ct);
        await AuditAsync(ctx, session!.Username, "localadmin.restart", id,
            force ? "强制重启服务器进程" : "重启服务器进程", result.Success, ct);
        return ToActionResult(result);
    }

    // ==================== 控制台 ====================

    private static async Task<IResult> ConsoleAsync(string id, LocalConsoleRequest request, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        string command = (request.Command ?? string.Empty).Trim();
        if (command.Length == 0)
            return Results.Json(new { error = "命令不能为空" }, statusCode: StatusCodes.Status400BadRequest);

        if (command.Length > 20000)
            return Results.Json(new { error = "命令过长（上限 20000 字符）" }, statusCode: StatusCodes.Status400BadRequest);

        var result = await provider.SendConsoleAsync(id, command, ct);
        await AuditAsync(ctx, session!.Username, "localadmin.console", id, command, result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> ClearConsoleAsync(string id, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.ClearConsoleAsync(id, ct);
        await AuditAsync(ctx, session!.Username, "localadmin.console-clear", id, "清空控制台缓冲", result.Success, ct);
        return Results.Json(new { success = true });
    }

    // ==================== 心跳 ====================

    private static async Task<IResult> HeartbeatAsync(string id, LocalHeartbeatRequest request, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        bool enabled = request.Enabled ?? true;
        var result = await provider.SetHeartbeatAsync(id, enabled, ct);
        await AuditAsync(ctx, session!.Username, "localadmin.heartbeat", id,
            enabled ? "开启静默崩溃检测" : "关闭静默崩溃检测", result.Success, ct);
        return ToActionResult(result);
    }

    // ==================== 本地功能（对应官方 LocalAdmin 自带指令） ====================

    /// <summary>对应官方指令 <c>lacfg</c>：返回当前生效的配置与配置文件路径。</summary>
    private static async Task<IResult> GetConfigAsync(HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var info = await provider.GetConfigAsync(ct);
        return Results.Json(info);
    }

    /// <summary>对应官方指令 <c>resave</c>：把内存中的配置重写回配置文件（并做一次规范化）。</summary>
    private static async Task<IResult> ResaveConfigAsync(HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.ResaveConfigAsync(ct);

        await AuditAsync(ctx, session!.Username, "localadmin.config-resave", "config",
            "重写配置文件", result.Success, ct);

        return ToActionResult(result);
    }

    // ==================== 控制台显示级别 ====================

    private static async Task<IResult> ConsoleLevelAsync(string id, LocalConsoleLevelRequest request, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.SetConsoleLevelAsync(id, request.Level, ct);

        await AuditAsync(ctx, session!.Username, "localadmin.console-level", id,
            $"控制台显示级别 → {request.Level}", result.Success, ct);

        return Results.Json(new
        {
            success = result.Success,
            response = result.Response,
            error = result.Error,
            warning = result.Warning,
        });
    }

    private static async Task<IResult> CancelRestartAsync(string id, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.CancelRestartAsync(id, ct);
        await AuditAsync(ctx, session!.Username, "localadmin.cancel-restart", id, "中止重启倒计时", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> GetDaemonStatusAsync(HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var status = await provider.GetDaemonStatusAsync(ct);
        return Results.Json(status);
    }

    private static async Task<IResult> StartDaemonAsync(HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = await provider.StartDaemonAsync(ct);
        await AuditAsync(ctx, session!.Username, "localadmin.daemon-start", "daemon", "启动 LocalAdmin 独立守护进程", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> StopDaemonAsync(DaemonActionRequest? request, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        if (request is null || !request.Confirm)
        {
            return Results.Json(new
            {
                success = false,
                error = "操作被拦截：停止守护进程将导致所有受托管的 SCPSL 游戏服务器一并退出，必须确认警告后才能关闭。"
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await provider.StopDaemonAsync(ct);
        await AuditAsync(ctx, session!.Username, "localadmin.daemon-stop", "daemon", "关闭 LocalAdmin 独立守护进程", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> RestartDaemonAsync(DaemonActionRequest? request, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        if (request is null || !request.Confirm)
        {
            return Results.Json(new
            {
                success = false,
                error = "操作被拦截：重启守护进程将导致所有受托管的游戏服务器重新拉起，必须确认警告后才能重启。"
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await provider.RestartDaemonAsync(ct);
        await AuditAsync(ctx, session!.Username, "localadmin.daemon-restart", "daemon", "重启 LocalAdmin 独立守护进程", result.Success, ct);
        return ToActionResult(result);
    }

    // ==================== 辅助 ====================

    private static object ToLineDto(ConsoleLineDto line) => new
    {
        seq = line.Seq,
        time = line.Time,
        kind = line.Kind,
        color = line.Color,
        colorName = line.ColorName,
        colorHex = line.ColorHex,
        text = line.Text,
    };

    private static IResult NotFound(string id) =>
        Results.Json(new { error = $"本地服务器实例不存在：{id}" }, statusCode: StatusCodes.Status404NotFound);

    private static IResult ToActionResult(LocalAdminResult result) =>
        Results.Json(new { success = result.Success, response = result.Message, error = result.Error });

    /// <summary>保存类操作：失败用 400，成功可带一条非致命提醒（如可执行文件不存在）。</summary>
    private static IResult ToSaveResult(LocalSaveResult result)
    {
        if (!result.Success)
            return Results.Json(new { success = false, response = result.Response, error = result.Error },
                statusCode: StatusCodes.Status400BadRequest);

        return Results.Json(new { success = true, response = result.Response, error = (string?)null, warning = result.Warning });
    }

    private static async Task AuditAsync(HttpContext ctx, string username, string action, string target, string detail, bool success, CancellationToken ct)
    {
        var db = ctx.RequestServices.GetRequiredService<PanelDatabase>();
        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = username,
            Action = action,
            Target = target,
            Detail = detail,
            Success = success,
        }, ct);
    }
}
