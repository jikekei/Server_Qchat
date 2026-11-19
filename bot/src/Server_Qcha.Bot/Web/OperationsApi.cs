using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Server.Qcat.Bot;
using Server.Qcat.LocalAdmin;

namespace Server.Qcat.Web;

public sealed record OperationsApiToken(string Id, string Name, string KeyHash);

public sealed record OperationsApiSettings
{
    public bool Enabled { get; init; }
    public bool AllowControl { get; init; }
    public OperationsApiToken[] Tokens { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeyHash { get; init; }
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
        if (_settings.KeyHash is { Length: 64 } legacyHash)
            _settings = _settings with
            {
                KeyHash = null,
                Tokens = [.. _settings.Tokens, new("legacy", "原有令牌", legacyHash)],
            };
    }
    public OperationsApiSettings Snapshot()
    {
        lock (_sync) return _settings with { Tokens = [.. _settings.Tokens] };
    }

    public void Save(bool enabled, bool allowControl)
    {
        lock (_sync) Persist(_settings with { Enabled = enabled, AllowControl = allowControl });
    }

    public (OperationsApiToken Token, string Key) CreateToken(string name)
    {
        lock (_sync)
        {
            string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var token = new OperationsApiToken(Guid.NewGuid().ToString("N"), name, Hash(key));
            Persist(_settings with { Tokens = [.. _settings.Tokens, token] });
            return (token, key);
        }
    }

    public OperationsApiToken? RevokeToken(string id)
    {
        lock (_sync)
        {
            var token = Array.Find(_settings.Tokens, token => token.Id == id);
            if (token is null) return null;
            Persist(_settings with { Tokens = Array.FindAll(_settings.Tokens, token => token.Id != id) });
            return token;
        }
    }

    private void Persist(OperationsApiSettings next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(next));
        File.Move(temp, _path, true);
        _settings = next;
    }

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public bool Validate(string key, out bool allowControl)
    {
        lock (_sync)
        {
            allowControl = _settings.AllowControl;
            if (!_settings.Enabled || key.Length > 256 || _settings.Tokens.Length == 0) return false;
            byte[] hash = Encoding.ASCII.GetBytes(Hash(key));
            return _settings.Tokens.Any(token =>
                CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(token.KeyHash)));
        }
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

public sealed record OperationsSettingsRequest(bool Enabled, bool AllowControl);
public sealed record CreateOperationsTokenRequest(string? Name);
public sealed record BotCommandApiRequest(string? Command, string? SenderId);

public static class OperationsApi
{
    public static void MapOperationsApi(this WebApplication app)
    {
        app.MapGet("/api/operations/settings", async (HttpContext ctx, PanelAuthService auth, OperationsApiStore store) =>
        {
            var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
            if (error is not null) return error;
            var s = store.Snapshot();
            return Results.Json(new { s.Enabled, s.AllowControl, tokens = s.Tokens.Select(token => new { token.Id, token.Name }) });
        });
        app.MapPut("/api/operations/settings", async (OperationsSettingsRequest request, HttpContext ctx, PanelAuthService auth, OperationsApiStore store, PanelDatabase db, CancellationToken ct) =>
        {
            var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
            if (error is not null) return error;
            store.Save(request.Enabled, request.AllowControl);
            await db.AddAuditAsync(new PanelAuditEntry { Username = session!.Username, Action = "api.settings", Target = "Operations API", Detail = $"enabled={request.Enabled}, control={request.AllowControl}", Success = true }, ct);
            return Results.NoContent();
        });
        app.MapPost("/api/operations/tokens", async (CreateOperationsTokenRequest request, HttpContext ctx, PanelAuthService auth, OperationsApiStore store, PanelDatabase db, CancellationToken ct) =>
        {
            var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
            if (error is not null) return error;

            string name = request.Name?.Trim() ?? "";
            if (name.Length is 0 or > 80)
                return Results.BadRequest(new { error = "请输入 1 到 80 个字符的令牌名称" });

            ctx.Response.Headers.CacheControl = "no-store";
            var (token, key) = store.CreateToken(name);
            await db.AddAuditAsync(new PanelAuditEntry { Username = session!.Username, Action = "api.token.create", Target = token.Id, Detail = token.Name, Success = true }, ct);
            return Results.Json(new { token.Id, token.Name, key }, statusCode: 201);
        });
        app.MapDelete("/api/operations/tokens/{id}", async (string id, HttpContext ctx, PanelAuthService auth, OperationsApiStore store, PanelDatabase db, CancellationToken ct) =>
        {
            var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.AccountsManage);
            if (error is not null) return error;

            var token = store.RevokeToken(id);
            if (token is null) return Results.NotFound(new { error = "该令牌已不存在" });
            await db.AddAuditAsync(new PanelAuditEntry { Username = session!.Username, Action = "api.token.revoke", Target = token.Id, Detail = token.Name, Success = true }, ct);
            return Results.NoContent();
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
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !store.Validate(header[7..], out bool allowControl)) return Results.Unauthorized();
            if (ctx.Request.Method != "GET" && !allowControl) return Results.StatusCode(403);
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
        api.MapPost("/bot/commands", ExecuteBotCommandAsync);
    }

    private static async Task<IResult> ExecuteBotCommandAsync(
        BotCommandApiRequest body, BotClientAccessor accessor, CommandRouter router, CancellationToken ct)
    {
        var request = BotCommandParser.Parse(body.Command ?? "");
        if (request.IsEmpty)
            return Results.BadRequest(new { error = "请输入 QQ 机器人指令" });

        var definition = BotCommandCatalog.Resolve(request.Name);
        if (definition is null)
            return Results.BadRequest(new { error = $"未知指令：{request.Name}" });

        string senderId = body.SenderId?.Trim() ?? "";
        if (definition.Name is "bind" or "me" && senderId.Length == 0)
            return Results.BadRequest(new { error = "该指令需要 senderId（QQ 号或 OpenID）" });

        var client = accessor.Current;
        if (client is null)
            return Results.Json(new { error = "机器人接入模式尚未就绪" }, statusCode: 503);

        string content = await router.ExecuteForApiAsync(client, request, senderId, ct);
        return Results.Json(new { command = definition.Name, content });
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
