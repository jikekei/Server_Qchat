using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin.Models;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// LocalAdmin 管理抽象：统一屏蔽 Daemon 独立守护模式与 Embedded 内嵌模式的调用差异。
/// 面板与端点层仅依赖该接口，便于透明切换运行架构。
/// </summary>
public interface ILocalAdminProvider
{
    /// <summary>运行模式：Daemon 或 Embedded。</summary>
    string Mode { get; }

    /// <summary>获取服务器列表与当前生效的配置状态。</summary>
    Task<LocalAdminListResult> ListServersAsync(CancellationToken ct = default);

    /// <summary>获取指定服务器实例的状态快照。</summary>
    Task<LocalServerStatus?> GetStatusAsync(string id, CancellationToken ct = default);

    /// <summary>轮询指定服务器的状态与增量控制台输出。</summary>
    Task<LocalPollResult?> PollAsync(string id, long? after, int? limit, CancellationToken ct = default);

    /// <summary>创建新服务器定义并落盘。</summary>
    Task<LocalSaveResult> CreateServerAsync(LocalServerSaveRequest request, CancellationToken ct = default);

    /// <summary>更新现有服务器定义并落盘。</summary>
    Task<LocalSaveResult> UpdateServerAsync(string id, LocalServerSaveRequest request, CancellationToken ct = default);

    /// <summary>删除服务器定义并落盘。</summary>
    Task<LocalAdminResult> DeleteServerAsync(string id, CancellationToken ct = default);

    /// <summary>启动服务器进程。</summary>
    Task<LocalAdminResult> StartAsync(string id, CancellationToken ct = default);

    /// <summary>停止服务器进程（支持优雅停止与强制终止）。</summary>
    Task<LocalAdminResult> StopAsync(string id, bool force, CancellationToken ct = default);

    /// <summary>重启服务器进程。</summary>
    Task<LocalAdminResult> RestartAsync(string id, bool force, CancellationToken ct = default);

    /// <summary>向服务器控制台发送命令。</summary>
    Task<LocalAdminResult> SendConsoleAsync(string id, string command, CancellationToken ct = default);

    /// <summary>清空控制台环形缓冲区。</summary>
    Task<LocalAdminResult> ClearConsoleAsync(string id, CancellationToken ct = default);

    /// <summary>切换静默崩溃检测（心跳监控）。</summary>
    Task<LocalAdminResult> SetHeartbeatAsync(string id, bool enabled, CancellationToken ct = default);

    /// <summary>设置并持久化控制台捕获级别。</summary>
    Task<LocalConsoleLevelResult> SetConsoleLevelAsync(string id, string? level, CancellationToken ct = default);

    /// <summary>取消重启倒计时。</summary>
    Task<LocalAdminResult> CancelRestartAsync(string id, CancellationToken ct = default);

    /// <summary>获取当前生效的全局与各服配置信息。</summary>
    Task<LocalConfigInfo> GetConfigAsync(CancellationToken ct = default);

    /// <summary>将内存配置规范化并重新写入配置文件。</summary>
    Task<LocalAdminResult> ResaveConfigAsync(CancellationToken ct = default);

    /// <summary>探测本机存在的 SCPSL.exe 路径候选。</summary>
    Task<ScpslDiscoveryDto> FindExecutablesAsync(CancellationToken ct = default);

    /// <summary>健康检查 / 探测 Daemon 状态。</summary>
    Task<DaemonPingResult?> PingAsync(CancellationToken ct = default);

    /// <summary>获取守护进程详细运行指标（内存占用、运行时间、PID等）。</summary>
    Task<DaemonStatusResult> GetDaemonStatusAsync(CancellationToken ct = default);

    /// <summary>启动独立守护进程。</summary>
    Task<LocalAdminResult> StartDaemonAsync(CancellationToken ct = default);

    /// <summary>停止独立守护进程（先优雅停机，若超时则强杀）。</summary>
    Task<LocalAdminResult> StopDaemonAsync(CancellationToken ct = default);

    /// <summary>重启独立守护进程。</summary>
    Task<LocalAdminResult> RestartDaemonAsync(CancellationToken ct = default);

    /// <summary>主动关闭独立守护进程。</summary>
    Task<LocalAdminResult> ShutdownDaemonAsync(CancellationToken ct = default);
}
