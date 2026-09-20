using Server.Qcat.Logging;

namespace Server.Qcat.Web;

// ---------------- 请求体 ----------------

public sealed record LogLevelCategoryRequest(string? Category, string? Level);

public sealed record LogLevelSaveRequest(string? Default, List<LogLevelCategoryRequest>? Categories);

/// <summary>
/// 机器人日志级别的查看与调整。
///
/// 权限：<see cref="PanelPermission.LoggingManage"/>（logging.manage）。
/// 审计：每次修改写 `logging.level-update`，并记录修改后的完整级别表。
///
/// 生效方式：写入 <c>logging-level.json</c> → 配置热重载 → 框架重新绑定
/// <c>LoggerFilterOptions</c>，**无需重启即时生效**（实现细节见 <see cref="LogLevelStore"/>）。
/// </summary>
public static class LoggingEndpoints
{
    private const int MaxCategories = 50;
    private const int MaxCategoryLength = 200;

    public static void MapLoggingApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/logging");

        // 用空串而不是 "/"：前缀 + "" 得到 /api/logging，
        // 与 /api/logging/ 不会产生尾斜杠歧义
        api.MapGet("", GetAsync);
        api.MapPut("", SaveAsync);
    }

    private static async Task<IResult> GetAsync(HttpContext ctx, PanelAuthService auth, LogLevelStore store, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.LoggingManage);
        if (error is not null)
            return error;

        var settings = store.Load();

        return Results.Json(new
        {
            @default = settings.Default,
            categories = settings.Categories.Select(c => new { category = c.Category, level = c.Level }).ToList(),
            levels = LogLevelStore.LevelNames,
            suggestedCategories = LogLevelStore.SuggestedCategories,
            configPath = store.ConfigPath,
            fileExists = store.Exists,
            note = "修改后立即生效、无需重启。若通过环境变量（Logging__LogLevel__*）锁定了级别，环境变量优先级更高。",
        });
    }

    private static async Task<IResult> SaveAsync(LogLevelSaveRequest request, HttpContext ctx, PanelAuthService auth, LogLevelStore store, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.LoggingManage);
        if (error is not null)
            return error;

        // ---- 校验：默认级别 ----
        string? def = LogLevelStore.Normalize(request.Default);
        if (def is null)
            return BadRequest($"默认级别不合法：{request.Default}（可选 {string.Join(" / ", LogLevelStore.LevelNames)}）");

        // ---- 校验：分类覆盖 ----
        var categories = new List<LogLevelStore.CategoryOverride>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in request.Categories ?? new List<LogLevelCategoryRequest>())
        {
            string name = (item.Category ?? "").Trim();

            // 允许前端留空行，直接忽略，避免误报
            if (name.Length == 0 && string.IsNullOrWhiteSpace(item.Level))
                continue;

            if (name.Length == 0)
                return BadRequest("分类名不能为空");

            if (name.Length > MaxCategoryLength)
                return BadRequest($"分类名过长（上限 {MaxCategoryLength} 字符）：{name}");

            if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Default 不是分类名，请用上方的「默认级别」设置全局级别");

            if (!seen.Add(name))
                return BadRequest($"分类重复：{name}");

            string? level = LogLevelStore.Normalize(item.Level);
            if (level is null)
                return BadRequest($"分类 [{name}] 的级别不合法：{item.Level}");

            categories.Add(new LogLevelStore.CategoryOverride(name, level));
        }

        if (categories.Count > MaxCategories)
            return BadRequest($"分类覆盖过多（上限 {MaxCategories} 条）");

        categories.Sort((a, b) => string.CompareOrdinal(a.Category, b.Category));

        var settings = new LogLevelStore.Settings(def, categories);

        try
        {
            store.Save(settings);
        }
        catch (Exception ex)
        {
            await AuditAsync(ctx, session!.Username, "logging.level-update",
                $"默认 {def}（写入失败）", false, ct);
            return Results.Json(new { success = false, error = $"写入配置失败：{ex.Message}" },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        string detail = "默认 " + def +
            (categories.Count > 0
                ? "；覆盖 " + string.Join(", ", categories.Select(c => $"{c.Category}={c.Level}"))
                : "");

        await AuditAsync(ctx, session!.Username, "logging.level-update", detail, true, ct);

        return Results.Json(new
        {
            success = true,
            response = "日志级别已更新并即时生效",
            @default = settings.Default,
            categories = settings.Categories.Select(c => new { category = c.Category, level = c.Level }).ToList(),
        });
    }

    private static IResult BadRequest(string message) =>
        Results.Json(new { success = false, error = message }, statusCode: StatusCodes.Status400BadRequest);

    private static async Task AuditAsync(HttpContext ctx, string username, string action, string detail, bool success, CancellationToken ct)
    {
        var db = ctx.RequestServices.GetRequiredService<PanelDatabase>();
        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = username,
            Action = action,
            Target = "机器人日志",
            Detail = detail,
            Success = success,
        }, ct);
    }
}
