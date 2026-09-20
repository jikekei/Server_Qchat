using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin;

namespace Server.Qcat.Web;

// ---------------- 请求体 ----------------

public sealed record LocalStopRequest(bool? Force);
public sealed record LocalRestartRequest(bool? Force);
public sealed record LocalConsoleRequest(string? Command);
public sealed record LocalHeartbeatRequest(bool? Enabled);

/// <summary>控制台捕获级别请求体。级别键：all / normal / warn / error / off。</summary>
public sealed record LocalConsoleLevelRequest(string? Level);

/// <summary>
/// 新增/修改服务器的请求体。字段可空：缺省即采用 <see cref="LocalServerDefinition"/> 的默认值。
/// PUT 语义为整体替换，前端提交完整表单。
/// </summary>
public sealed record LocalServerSaveRequest(
    string? Id,
    string? Name,
    string? ExecutablePath,
    string? WorkingDirectory,
    int? GamePort,
    string? ExtraArguments,
    bool? AutoStart,
    bool? EnableHeartbeat,
    int? HeartbeatSpanMaxThreshold,
    int? HeartbeatRestartInSeconds,
    bool? RestartOnCrash,
    int? RestartLimit,
    int? RestartTimeWindowSeconds,
    int? GracefulStopTimeoutSeconds,
    int? LaToSlBufferSize,
    int? SlToLaBufferSize,
    bool? DisableAnsiColors,
    bool? RedirectStandardStreams,
    string? ConsoleLevel);

/// <summary>
/// LocalAdmin 能力的 Web 面板接口。
///
/// 权限：全部要求 <see cref="PanelPermission.ServerControl"/>（server.control）。
/// 审计：所有会产生副作用的操作（启停/重启/下发命令/切换心跳/增删改服务器）都会写入审计日志。
///
/// 安全边界：该能力等价于把服务器控制台搬到浏览器 —— 能执行任意服务端控制台命令，
/// 也能新增一个「可执行文件路径」从而启动任意程序。因此必须同时满足
/// 「面板已登录且具备 server.control」与「实例仅监听回环」。
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
    }

    // ==================== 只读 ====================

    /// <summary>
    /// 列出托管服务器。
    ///
    /// <c>servers</c> 是运行时状态（旧字段，形状未变）；<c>definitions</c> 是可供编辑的完整定义，
    /// 与 <c>servers</c> 同序同长，前端按 id 关联。另外给出配置来源与配置文件路径，
    /// 便于用户理解「改了 appsettings 为什么没生效」。
    /// </summary>
    private static async Task<IResult> ListServersAsync(HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var servers = manager.Instances.Select(i => i.GetStatus()).ToList();
        var definitions = manager.Definitions.ToList();

        return Results.Json(new
        {
            enabled = manager.Enabled,
            total = servers.Count,
            source = manager.UsingConfigFile ? "config-file" : "appsettings",
            configPath = manager.ConfigPath,
            // 供前端渲染「控制台显示级别」下拉
            consoleLevels = ConsoleCaptureLevels.Describe(),
            servers,
            definitions,
        });
    }

    /// <summary>
    /// 探测本机可能存在的 SCPSL 服务端可执行文件，供「添加服务器」自动预填。
    /// 只读探测，不修改任何文件；结果为空时前端提示用户手动填写。
    /// </summary>
    private static async Task<IResult> FindExecutablesAsync(HttpContext ctx, PanelAuthService auth, IOptions<LocalAdminOptions> options, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = ScpslLocator.Search(options.Value.DefaultExecutablePath);

        return Results.Json(new
        {
            // 预填优先级：配置里的默认值 → 第一个探测到的
            recommended = result.Candidates.FirstOrDefault() ?? "",
            candidates = result.Candidates,
            steamRoots = result.SteamRoots,
            probed = result.Probed,
        });
    }

    private static async Task<IResult> GetStatusAsync(string id, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        return Results.Json(instance.GetStatus());
    }

    /// <summary>面板轮询：一次请求同时取回最新状态与增量控制台输出。</summary>
    private static async Task<IResult> PollAsync(string id, long? after, int? limit, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        long afterSeq = after.GetValueOrDefault();
        int take = Math.Clamp(limit ?? 400, 1, 2000);

        var lines = instance.ReadLines(afterSeq, take, out long firstSeq, out long lastSeq, out bool truncated);

        return Results.Json(new
        {
            status = instance.GetStatus(),
            firstSeq,
            lastSeq,
            truncated,
            lines = lines.Select(ToLineDto).ToList(),
        });
    }

    // ==================== 服务器定义的增删改 ====================

    private static async Task<IResult> CreateServerAsync(LocalServerSaveRequest request, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var def = ToDefinition(request);
        var result = manager.Save(def, originalId: null, out string? warning);

        await AuditAsync(ctx, session!.Username, "localadmin.server-add",
            $"{(string.IsNullOrWhiteSpace(def.Name) ? def.Id : def.Name)} ({def.ExecutablePath}:{def.GamePort})",
            "新增可托管的服务器", result.Success, ct);

        return ToSaveResult(result, warning);
    }

    private static async Task<IResult> UpdateServerAsync(string id, LocalServerSaveRequest request, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var def = ToDefinition(request);
        var result = manager.Save(def, originalId: id, out string? warning);

        await AuditAsync(ctx, session!.Username, "localadmin.server-update",
            $"{id} → {(string.IsNullOrWhiteSpace(def.Name) ? def.Id : def.Name)} ({def.ExecutablePath}:{def.GamePort})",
            "修改服务器配置", result.Success, ct);

        return ToSaveResult(result, warning);
    }

    private static async Task<IResult> DeleteServerAsync(string id, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = manager.Remove(id);

        await AuditAsync(ctx, session!.Username, "localadmin.server-delete", id, "删除服务器配置", result.Success, ct);

        // 与新增/修改一致：失败返回 4xx，前端靠异常分支提示，避免把失败当成功
        return ToSaveResult(result, null);
    }

    // ==================== 进程控制 ====================

    private static async Task<IResult> StartAsync(string id, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        var result = instance.Start();
        await AuditAsync(ctx, session!.Username, "localadmin.start", instance, "启动服务器进程", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> StopAsync(string id, LocalStopRequest? request, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        bool force = request?.Force ?? false;
        var result = instance.Stop(force);
        await AuditAsync(ctx, session!.Username, "localadmin.stop", instance,
            force ? "强制结束服务器进程" : "优雅停止服务器进程", result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> RestartAsync(string id, LocalRestartRequest? request, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        bool force = request?.Force ?? false;
        var result = instance.Restart(force);
        await AuditAsync(ctx, session!.Username, "localadmin.restart", instance,
            force ? "强制重启服务器进程" : "重启服务器进程", result.Success, ct);
        return ToActionResult(result);
    }

    // ==================== 控制台 ====================

    private static async Task<IResult> ConsoleAsync(string id, LocalConsoleRequest request, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        string command = (request.Command ?? string.Empty).Trim();
        if (command.Length == 0)
            return Results.Json(new { error = "命令不能为空" }, statusCode: StatusCodes.Status400BadRequest);

        if (command.Length > 20000)
            return Results.Json(new { error = "命令过长（上限 20000 字符）" }, statusCode: StatusCodes.Status400BadRequest);

        var result = await instance.SendConsoleAsync(command, ct);
        await AuditAsync(ctx, session!.Username, "localadmin.console", instance, command, result.Success, ct);
        return ToActionResult(result);
    }

    private static async Task<IResult> ClearConsoleAsync(string id, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        instance.ClearBuffer();
        await AuditAsync(ctx, session!.Username, "localadmin.console-clear", instance, "清空控制台缓冲", true, ct);
        return Results.Json(new { success = true });
    }

    // ==================== 心跳 ====================

    private static async Task<IResult> HeartbeatAsync(string id, LocalHeartbeatRequest request, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        bool enabled = request.Enabled ?? true;
        var result = instance.SetHeartbeatEnabled(enabled);
        await AuditAsync(ctx, session!.Username, "localadmin.heartbeat", instance,
            enabled ? "开启静默崩溃检测" : "关闭静默崩溃检测", result.Success, ct);
        return ToActionResult(result);
    }

    // ==================== 本地功能（对应官方 LocalAdmin 自带指令） ====================

    /// <summary>对应官方指令 <c>lacfg</c>：返回当前生效的配置与配置文件路径。</summary>
    private static async Task<IResult> GetConfigAsync(HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        return Results.Json(manager.GetConfigInfo());
    }

    /// <summary>对应官方指令 <c>resave</c>：把内存中的配置重写回配置文件（并做一次规范化）。</summary>
    private static async Task<IResult> ResaveConfigAsync(HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var result = manager.ResaveConfig(out string path, out int count);

        await AuditAsync(ctx, session!.Username, "localadmin.config-resave", path,
            $"重写配置文件（{count} 台服务器）", result.Success, ct);

        return ToActionResult(result);
    }

    // ==================== 控制台显示级别 ====================

    /// <summary>
    /// 切换「服务器进程」页控制台的捕获级别。运行中也能改，仅影响之后的新输出。
    /// 同时把级别写回服务器定义，重启后仍生效。
    /// </summary>
    private static async Task<IResult> ConsoleLevelAsync(string id, LocalConsoleLevelRequest request, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        var result = instance.SetConsoleLevel(request.Level);

        // 运行中切换成功后，把级别落到定义里；失败不影响已经生效的运行时设置
        string? persistWarning = null;
        if (result.Success)
        {
            var persist = manager.PersistConsoleLevel(id, instance.ConsoleLevelKey);
            if (!persist.Success)
                persistWarning = persist.Error;
        }

        await AuditAsync(ctx, session!.Username, "localadmin.console-level", instance,
            $"控制台显示级别 → {instance.ConsoleLevelKey}", result.Success, ct);

        return Results.Json(new
        {
            success = result.Success,
            response = result.Message,
            error = result.Error,
            warning = persistWarning,
        });
    }

    private static async Task<IResult> CancelRestartAsync(string id, HttpContext ctx, PanelAuthService auth, LocalAdminManager manager, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
        if (error is not null)
            return error;

        var instance = manager.Find(id);
        if (instance is null)
            return NotFound(id);

        var result = instance.CancelRestartCountdown();
        await AuditAsync(ctx, session!.Username, "localadmin.cancel-restart", instance, "中止重启倒计时", result.Success, ct);
        return ToActionResult(result);
    }

    // ==================== 辅助 ====================

    /// <summary>把请求体映射为定义；未提供的字段沿用类型默认值。</summary>
    private static LocalServerDefinition ToDefinition(LocalServerSaveRequest r)
    {
        var d = new LocalServerDefinition();

        if (r.Id is not null) d.Id = r.Id.Trim();
        if (r.Name is not null) d.Name = r.Name.Trim();
        if (r.ExecutablePath is not null) d.ExecutablePath = r.ExecutablePath.Trim();
        if (r.WorkingDirectory is not null) d.WorkingDirectory = r.WorkingDirectory.Trim();
        if (r.GamePort is not null) d.GamePort = r.GamePort.Value;
        if (r.ExtraArguments is not null) d.ExtraArguments = r.ExtraArguments.Trim();

        if (r.AutoStart is not null) d.AutoStart = r.AutoStart.Value;
        if (r.EnableHeartbeat is not null) d.EnableHeartbeat = r.EnableHeartbeat.Value;
        if (r.HeartbeatSpanMaxThreshold is not null) d.HeartbeatSpanMaxThreshold = r.HeartbeatSpanMaxThreshold.Value;
        if (r.HeartbeatRestartInSeconds is not null) d.HeartbeatRestartInSeconds = r.HeartbeatRestartInSeconds.Value;

        if (r.RestartOnCrash is not null) d.RestartOnCrash = r.RestartOnCrash.Value;
        if (r.RestartLimit is not null) d.RestartLimit = r.RestartLimit.Value;
        if (r.RestartTimeWindowSeconds is not null) d.RestartTimeWindowSeconds = r.RestartTimeWindowSeconds.Value;
        if (r.GracefulStopTimeoutSeconds is not null) d.GracefulStopTimeoutSeconds = r.GracefulStopTimeoutSeconds.Value;

        if (r.LaToSlBufferSize is not null) d.LaToSlBufferSize = r.LaToSlBufferSize.Value;
        if (r.SlToLaBufferSize is not null) d.SlToLaBufferSize = r.SlToLaBufferSize.Value;
        if (r.DisableAnsiColors is not null) d.DisableAnsiColors = r.DisableAnsiColors.Value;
        if (r.RedirectStandardStreams is not null) d.RedirectStandardStreams = r.RedirectStandardStreams.Value;
        if (r.ConsoleLevel is not null) d.ConsoleLevel = ConsoleCaptureLevels.NormalizeKey(r.ConsoleLevel);

        return d;
    }

    private static object ToLineDto(ConsoleLine line) => new
    {
        seq = line.Seq,
        time = line.Time,
        kind = line.Kind.ToString().ToLowerInvariant(),
        color = line.Color,
        colorName = ConsoleProtocol.ColorName(line.Color),
        colorHex = line.ColorHex,
        text = line.Text,
    };

    private static IResult NotFound(string id) =>
        Results.Json(new { error = $"本地服务器实例不存在：{id}" }, statusCode: StatusCodes.Status404NotFound);

    private static IResult ToActionResult(LocalAdminResult result) =>
        Results.Json(new { success = result.Success, response = result.Message, error = result.Error });

    /// <summary>保存类操作：失败用 400，成功可带一条非致命提醒（如可执行文件不存在）。</summary>
    private static IResult ToSaveResult(LocalAdminResult result, string? warning)
    {
        if (!result.Success)
            return Results.Json(new { success = false, response = result.Message, error = result.Error },
                statusCode: StatusCodes.Status400BadRequest);

        return Results.Json(new { success = true, response = result.Message, error = (string?)null, warning });
    }

    private static Task AuditAsync(HttpContext ctx, string username, string action, LocalServerInstance instance, string detail, bool success, CancellationToken ct) =>
        AuditAsync(ctx, username, action, $"{instance.Name} ({instance.Definition.ExecutablePath})", detail, success, ct);

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
