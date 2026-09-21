using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin.Models;

namespace Server.Qcat.LocalAdmin;

public static class LocalAdminMappingExtensions
{
    /// <summary>把保存请求体映射为服务器定义；未提供的字段沿用类型默认值。</summary>
    public static LocalServerDefinition ToDefinition(this LocalServerSaveRequest r)
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

    /// <summary>将控制台行转换为前端/API DTO。</summary>
    public static ConsoleLineDto ToDto(this ConsoleLine line) => new(
        line.Seq,
        line.Time,
        line.Kind.ToString().ToLowerInvariant(),
        line.Color,
        ConsoleProtocol.ColorName(line.Color),
        line.ColorHex,
        line.Text);

    /// <summary>将搜索结果转换为前端/API DTO。</summary>
    public static ScpslDiscoveryDto ToDto(this ScpslLocator.SearchResult r) => new(
        r.Candidates.FirstOrDefault() ?? string.Empty,
        r.Candidates,
        r.SteamRoots,
        r.Probed);
}
