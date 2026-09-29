using Qchat.GameAdmin;
using Server.Qcat.Socket;

namespace Server.Qcat.Web;

public static class GameAdminEndpoints
{
    public static void MapGameAdminApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/servers/{index:int}/game-admin");
        // Dedicated server picker also works for an account with only game-admin.manage.
        app.MapGet("/api/game-admin/servers", async (HttpContext ctx, PanelAuthService auth, ServerRegistry registry) =>
        {
            var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.GameAdminManage);
            return error ?? Results.Json(registry.GetSorted(includeOffline: true).Select((s, i) => new { index = i + 1, s.Name, s.IsOnline, identity = Identity(s) }));
        });
        api.MapGet("", Read);
        api.MapGet("/capabilities", Read);
        api.MapPost("/changes", Change);
    }
    private static string Identity(ServerInfo s) => $"{s.ConnectHost}:{s.Port}";
    private static async Task<IResult> Read(int index, HttpContext ctx, PanelAuthService auth, ServerRegistry registry, SocketCommandClient client, CancellationToken ct)
    {
        var (_, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.GameAdminManage);
        if (error is not null) return error;
        var server = registry.GetSorted(includeOffline: true).ElementAtOrDefault(index - 1);
        if (server is null) return Results.NotFound(new { error = "服务器不存在" });
        if (ctx.Request.Headers["X-Qcha-Server"].ToString() != Identity(server)) return Results.Conflict(new { error = "服务器列表已改变，请刷新列表" });
        if (!server.IsOnline) return Results.Json(new { error = "服务器离线，不能读取配置" }, statusCode: 503);
        try { return Result(await client.SendAdminAsync(server, new AdminRequest(), ct)); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Net.Sockets.SocketException)
        { return Results.Json(new { error = "读取权限配置失败：" + ex.Message }, statusCode: 503); }
    }
    private static async Task<IResult> Change(int index, AdminRequest request, HttpContext ctx, PanelAuthService auth, ServerRegistry registry, SocketCommandClient client, PanelDatabase db, CancellationToken ct)
    {
        var (session, error) = await PanelEndpoints.AuthorizeAsync(ctx, auth, PanelPermission.GameAdminManage);
        if (error is not null) return error;
        var server = registry.GetSorted(includeOffline: true).ElementAtOrDefault(index - 1);
        if (server is null) return Results.NotFound(new { error = "服务器不存在" });
        if (ctx.Request.Headers["X-Qcha-Server"].ToString() != Identity(server)) return Results.Conflict(new { error = "服务器列表已改变，请刷新列表" });
        if (request.Operation is not ("create-group" or "update-group" or "delete-group" or "set-member" or "delete-member"))
            return Results.BadRequest(new { error = "不支持的操作" });
        if (string.IsNullOrWhiteSpace(request.Revision) || string.IsNullOrWhiteSpace(request.RequestId))
            return Results.BadRequest(new { error = "缺少配置版本或请求标识" });
        if (AdminJson.Write(request).Length > 256 * 1024) return Results.BadRequest(new { error = "单次变更过大" });
        AdminReply reply;
        try
        {
            reply = server.IsOnline ? await client.SendAdminAsync(server, request, ct) : new() { Code = "offline", Error = "服务器离线，未保存" };
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            // Do not retry a mutation automatically. The page reloads actual state for verification.
            reply = new() { Code = "uncertain", Error = "未收到保存结果，请重新读取核实后再操作：" + ex.Message };
        }
        await db.AddAuditAsync(new PanelAuditEntry
        {
            Username = session!.Username, Action = "game-admin." + request.Operation,
            Target = $"{server.Name} ({Identity(server)})",
            Detail = $"IP={ctx.Connection.RemoteIpAddress}; request={AdminJson.Write(request)}; result={reply.Code ?? "ok"}; error={reply.Error}",
            Success = reply.Success && reply.Persisted && reply.Applied,
        }, CancellationToken.None);
        return Result(reply);
    }
    private static IResult Result(AdminReply reply)
    {
        if (reply.Success) return Results.Json(reply);
        int status = reply.Code switch { "conflict" => 409, "offline" or "unavailable" or "timeout" or "uncertain" => 503, "upgrade" => 501, _ => 400 };
        return Results.Json(new { error = reply.Error ?? "游戏服拒绝了请求", reply.Code, reply.Persisted, reply.Applied, reply.Snapshot }, statusCode: status);
    }
}
