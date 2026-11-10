using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Server.Qcat.LocalAdmin;

namespace Server.Qcat.Web;

public sealed class OperationsApiSettings
{
    public bool Enabled { get; set; }
    public bool AllowControl { get; set; }
    public string KeyHash { get; set; } = "";
}

public sealed class OperationsApiStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private OperationsApiSettings _settings;
    public OperationsApiStore(IHostEnvironment env)
    {
        _path = Path.Combine(env.ContentRootPath, "data", "operations-api.json");
        _settings = File.Exists(_path) ? JsonSerializer.Deserialize<OperationsApiSettings>(File.ReadAllText(_path)) ?? new() : new();
    }
    public OperationsApiSettings Snapshot() { lock (_sync) return new() { Enabled = _settings.Enabled, AllowControl = _settings.AllowControl, KeyHash = _settings.KeyHash }; }
    public string? Save(bool enabled, bool allowControl, bool rotate)
    {
        lock (_sync)
        {
            string? key = rotate || _settings.KeyHash.Length == 0 ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) : null;
            var next = new OperationsApiSettings { Enabled = enabled, AllowControl = allowControl, KeyHash = key is null ? _settings.KeyHash : Hash(key) };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(next));
            File.Move(temp, _path, true);
            _settings = next;
            return key;
        }
    }
    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public bool Validate(string key)
    {
        var s = Snapshot();
        return s.Enabled && s.KeyHash.Length == 64 && key.Length <= 256 && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(key)), Encoding.ASCII.GetBytes(s.KeyHash));
    }
}

public sealed class PanelLogBuffer : ILoggerProvider
{
    private readonly Queue<object> _lines = new();
    public object[] Read() { lock (_lines) return _lines.ToArray(); }
    public ILogger CreateLogger(string categoryName) => new BufferLogger(this, categoryName);
    public void Dispose() { }
    private sealed class BufferLogger(PanelLogBuffer owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines)
            {
                owner._lines.Enqueue(new { time = DateTimeOffset.Now, level = level.ToString(), category, text = formatter(state, exception), exception = exception?.ToString() });
                while (owner._lines.Count > 2000) owner._lines.Dequeue();
            }
        }
    }
}

public sealed record OperationsSettingsRequest(bool Enabled, bool AllowControl, bool RotateKey);
public static class OperationsApi
{
    public static void MapOperationsApi(this WebApplication app)
    {
        app.MapGet("/api/operations/settings", async (HttpContext ctx, PanelAuthService auth, OperationsApiStore store) =>
        {
            var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
            if (error is not null) return error;
            var s = store.Snapshot();
            return Results.Json(new { s.Enabled, s.AllowControl, hasKey = s.KeyHash.Length > 0 });
        });
        app.MapPut("/api/operations/settings", async (OperationsSettingsRequest request, HttpContext ctx, PanelAuthService auth, OperationsApiStore store, PanelDatabase db, CancellationToken ct) =>
        {
            var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
            if (error is not null) return error;
            var key = store.Save(request.Enabled, request.AllowControl, request.RotateKey);
            await db.AddAuditAsync(new PanelAuditEntry { Username = session!.Username, Action = "api.settings", Target = "Operations API", Detail = $"enabled={request.Enabled}, control={request.AllowControl}, rotate={request.RotateKey}", Success = true }, ct);
            return Results.Json(new { key });
        });
        var api = app.MapGroup("/api/ops/v1");
        app.MapGet("/api/operations/archive/{id}", async (string id, string? file, string? date, long? offset, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct) =>
        {
            var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
            if (error is not null) return error;
            return await Archive(id, file, date, offset, provider, ct);
        });
        app.MapGet("/api/operations/log-dates/{id}", async (string id, HttpContext ctx, PanelAuthService auth, ILocalAdminProvider provider, CancellationToken ct) =>
        {
            var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.ServerControl);
            return error ?? await Dates(id, provider, ct);
        });
        api.AddEndpointFilter(async (context, next) =>
        {
            var ctx = context.HttpContext;
            ctx.Response.Headers.CacheControl = "no-store";
            var store = ctx.RequestServices.GetRequiredService<OperationsApiStore>();
            var header = ctx.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !store.Validate(header[7..])) return Results.Unauthorized();
            if (ctx.Request.Method != "GET" && !store.Snapshot().AllowControl) return Results.StatusCode(403);
            var result = await next(context);
            if (ctx.Request.Method != "GET")
                await ctx.RequestServices.GetRequiredService<PanelDatabase>().AddAuditAsync(new PanelAuditEntry { Username = "api-key", Action = "api." + ctx.Request.Path, Target = ctx.Connection.RemoteIpAddress?.ToString() ?? "", Detail = "API control request", Success = result is not IStatusCodeHttpResult status || status.StatusCode is null or < 400 });
            return result;
        });
        api.MapGet("/panel/logs", (PanelLogBuffer logs) => Results.Json(new { lines = logs.Read(), capacity = 2000 }));
        api.MapGet("/servers/{id}/archive", Archive);
        api.MapGet("/log-servers", async (ILocalAdminProvider provider, CancellationToken ct) =>
        {
            var result = await provider.ListServersAsync(ct);
            return Results.Json(new { count = result.Total, servers = result.Servers });
        });
        api.MapGet("/servers/{id}/log-dates", Dates);
        api.MapGet("/servers/{id}/log-files", async (string id, string? date, ILocalAdminProvider provider, CancellationToken ct) =>
            await Archive(id, null, date, null, provider, ct));
        api.MapGet("/servers", async (ILocalAdminProvider provider, CancellationToken ct) => Results.Json((await provider.ListServersAsync(ct)).Servers));
        api.MapGet("/servers/{id}", async (string id, ILocalAdminProvider provider, CancellationToken ct) => (await provider.GetStatusAsync(id, ct)) is { } value ? Results.Json(value) : Results.NotFound());
        api.MapGet("/servers/{id}/logs", async (string id, long? after, int? limit, ILocalAdminProvider provider, CancellationToken ct) => (await provider.PollAsync(id, after, Math.Clamp(limit ?? 500, 1, 2000), ct)) is { } value ? Results.Json(value) : Results.NotFound());
        api.MapPost("/servers/{id}/start", async (string id, ILocalAdminProvider provider, CancellationToken ct) => ControlResult(await provider.StartAsync(id, ct)));
        api.MapPost("/servers/{id}/stop", async (string id, ILocalAdminProvider provider, CancellationToken ct) => ControlResult(await provider.StopAsync(id, false, ct)));
        api.MapPost("/servers/{id}/restart", async (string id, ILocalAdminProvider provider, CancellationToken ct) => ControlResult(await provider.RestartAsync(id, false, ct)));
    }
    private static IResult ControlResult(LocalAdminResult result) => Results.Json(result, statusCode: result.Success ? 200 : 400);
    private static async Task<IResult> Dates(string id, ILocalAdminProvider provider, CancellationToken ct)
    {
        var archive = await provider.ReadArchiveAsync(id, null, 0, ct);
        return archive is null ? Results.NotFound() : Results.Json(new { serverId = id, today = DateTime.Now.ToString("yyyy-MM-dd"), timeZone = TimeZoneInfo.Local.Id, dates = LogArchive.Dates(archive.Files) });
    }
    private static async Task<IResult> Archive(string id, string? file, string? date, long? offset, ILocalAdminProvider provider, CancellationToken ct)
    {
        try
        {
            string? selectedDate = LogArchive.ResolveDate(date);
            if (selectedDate is not null && file is not null && LogArchive.FileDate(file) != selectedDate)
                return Results.BadRequest(new { error = "文件不属于所选日期" });
            var result = await provider.ReadArchiveAsync(id, file, offset ?? 0, ct);
            if (result is null) return Results.NotFound();
            return Results.Json(new { serverId = id, date = selectedDate, files = result.Files.Where(f => selectedDate is null || LogArchive.FileDate(f.Name) == selectedDate), result.Lines, result.Next, result.More });
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }
}
