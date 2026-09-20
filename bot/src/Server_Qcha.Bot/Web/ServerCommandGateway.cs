using Server.Qcat.Socket;

namespace Server.Qcat.Web;

/// <summary>命令下发结果。</summary>
public sealed record ServerCommandResult(bool Success, string Response, string? Error = null)
{
    public static ServerCommandResult Ok(string response) => new(true, response);

    public static ServerCommandResult Fail(string error) => new(false, error, error);
}

/// <summary>
/// 服务器命令网关抽象 —— 面板与游戏服之间的唯一下发入口。
///
/// 当前实现：<see cref="PluginCommandGateway"/>，通过游戏服 EXILED 插件的 TCP 命令通道下发
/// （与 QQ 群指令走同一条链路，复用 <see cref="SocketCommandClient"/>）。
///
/// 后期扩展点：若需"可替代 LocalAdmin"，可实现本接口新增
/// <c>LocalAdminCommandGateway</c>（通过 LocalAdmin 的 TCP/stdin 控制服务端进程），
/// 在 <c>Program.cs</c> 中替换或按服务器维度并存，Web 层代码无需改动。
/// </summary>
public interface IServerCommandGateway
{
    /// <summary>网关标识，用于诊断与前端展示。</summary>
    string Name { get; }

    Task<ServerCommandResult> SendAsync(ServerInfo server, string command, CancellationToken ct);
}

/// <summary>通过游戏服插件 TCP 通道下发命令（默认实现）。</summary>
public sealed class PluginCommandGateway : IServerCommandGateway
{
    private readonly SocketCommandClient _client;
    private readonly ServerRegistry _registry;
    private readonly ILogger<PluginCommandGateway> _log;

    public PluginCommandGateway(SocketCommandClient client, ServerRegistry registry, ILogger<PluginCommandGateway> log)
    {
        _client = client;
        _registry = registry;
        _log = log;
    }

    public string Name => "plugin-tcp";

    public async Task<ServerCommandResult> SendAsync(ServerInfo server, string command, CancellationToken ct)
    {
        string? response = await _client.SendAsync(server.ConnectHost, server.Port, command, ct);

        if (response is null)
        {
            _registry.MarkOffline(server.ConnectHost, server.Port);
            _log.LogWarning("Web 面板命令 [{Command}] → [{Server}] {Host}:{Port} 失败（超时或断开）",
                command, server.Name, server.ConnectHost, server.Port);
            return ServerCommandResult.Fail("服务器不在线或命令执行超时");
        }

        _log.LogInformation("Web 面板命令 [{Command}] → [{Server}] {Host}:{Port} 执行成功",
            command, server.Name, server.ConnectHost, server.Port);
        return ServerCommandResult.Ok(response);
    }
}
