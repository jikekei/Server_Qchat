using System.Text.Json.Serialization;
using Server.Qcat.Configuration;

namespace Server.Qcat.LocalAdmin.Models;

// ==================== 请求体 ====================

public sealed record LocalStopRequest(bool? Force);
public sealed record LocalRestartRequest(bool? Force);
public sealed record LocalConsoleRequest(string? Command);
public sealed record LocalHeartbeatRequest(bool? Enabled);
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

// ==================== 响应体 / DTO ====================

public sealed record ConsoleLineDto(
    long Seq,
    DateTime Time,
    string Kind,
    byte Color,
    string ColorName,
    string ColorHex,
    string Text);

public sealed record LocalPollResult(
    LocalServerStatus Status,
    long FirstSeq,
    long LastSeq,
    bool Truncated,
    IReadOnlyList<ConsoleLineDto> Lines);

public sealed record LocalSaveResult(
    bool Success,
    string? Response,
    string? Error,
    string? Warning);

public sealed record LocalConsoleLevelResult(
    bool Success,
    string? Response,
    string? Error,
    string? Warning);

public sealed record LocalGlobalConfigDto(
    int ConsoleBufferLines,
    string LogDirectory,
    bool WriteLogFiles,
    int LogExpirationDays,
    string DefaultExecutablePath);

public sealed record LocalServerConfigSummaryDto(
    string Id,
    string Name,
    int GamePort,
    string ConsoleLevel,
    bool AutoStart);

public sealed record LocalConfigInfo(
    bool Enabled,
    string ContentRoot,
    string ServersConfigPath,
    bool ServersConfigExists,
    string Source,
    int ServerCount,
    LocalGlobalConfigDto Global,
    IReadOnlyList<LocalServerConfigSummaryDto> Servers);

public sealed record ConsoleLevelDescription(string Key, string Label);

public sealed record LocalAdminListResult(
    bool Enabled,
    int Total,
    string Source,
    string ConfigPath,
    IReadOnlyList<ConsoleLevelDescription> ConsoleLevels,
    IReadOnlyList<LocalServerStatus> Servers,
    IReadOnlyList<LocalServerDefinition> Definitions);

public sealed record DaemonPingResult(
    string Status,
    string Version,
    int Pid,
    int ServerCount,
    int RunningCount);

public sealed record DaemonStatusResult(
    bool Online,
    string Status,
    string Version,
    int? Pid,
    DateTime? StartTime,
    double UptimeSeconds,
    double MemoryWorkingSetMb,
    double MemoryPrivateMb,
    int ThreadCount,
    int ServerCount,
    int RunningServerCount,
    string ListenUri,
    string? Message = null);

public sealed record ScpslDiscoveryDto(
    string Recommended,
    IReadOnlyList<string> Candidates,
    IReadOnlyList<string> SteamRoots,
    IReadOnlyList<string> Probed);

