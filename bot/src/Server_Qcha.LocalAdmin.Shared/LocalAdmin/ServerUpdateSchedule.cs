using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Server.Qcat.LocalAdmin.Models;

namespace Server.Qcat.LocalAdmin;

public sealed partial class ServerUpdateService
{
    private readonly object _scheduleGate = new();
    private readonly HashSet<Guid> _runningSchedules = [];
    private List<LocalUpdateSchedule>? _schedules;
    private Task? _scheduleLoop;
    private string SchedulePath => Path.Combine(Path.GetDirectoryName(_manager.ConfigPath)!, "localadmin-update-schedules.json");

    public LocalUpdateSchedules GetSchedules()
    {
        lock (_scheduleGate)
            return new(TimeZoneInfo.Local.Id, ReadSchedulesLocked().ToArray(), _runningSchedules.ToArray());
    }

    public LocalUpdateScheduleResult AddSchedule(LocalUpdateScheduleRequest request, string? requestedBy)
    {
        if (!_manager.Enabled) return new(false, "LocalAdmin 能力未启用");
        var ids = (request.ServerIds ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0 || ids.Any(id => string.IsNullOrWhiteSpace(id) || _manager.Find(id) is null))
            return new(false, "请选择有效的服务器");

        string? dailyAt = string.IsNullOrWhiteSpace(request.DailyAt) ? null : request.DailyAt.Trim();
        if ((dailyAt is null) == (request.UpdateAt is null))
            return new(false, "请选择每天检查或一次性预约");
        if (dailyAt is not null && !TimeOnly.TryParseExact(dailyAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return new(false, "每天检查时间必须为 HH:mm");

        var now = DateTimeOffset.UtcNow;
        if (request.UpdateAt is { } updateAt && updateAt <= now)
            return new(false, "预约时间必须晚于当前时间");
        string mode = NormalizeMode(request.Mode ?? _options.UpdateDefaultMode, out string? error);
        if (error is not null) return new(false, error);

        var entry = new LocalUpdateSchedule(Guid.NewGuid(), ids, dailyAt, request.UpdateAt, mode,
            request.UpdateAt is not null || request.AutoUpdate, requestedBy, now);
        lock (_scheduleGate)
            SaveSchedulesLocked([.. ReadSchedulesLocked(), entry]);
        return new(true);
    }

    public LocalUpdateScheduleResult RemoveSchedule(Guid id)
    {
        lock (_scheduleGate)
        {
            if (_runningSchedules.Contains(id)) return new(false, "计划正在执行，请等待结束");
            var schedules = ReadSchedulesLocked();
            var next = schedules.Where(schedule => schedule.Id != id).ToList();
            if (next.Count == schedules.Count) return new(false, "该计划已不存在");
            SaveSchedulesLocked(next);
            return new(true);
        }
    }

    private List<LocalUpdateSchedule> ReadSchedulesLocked() => _schedules ??= File.Exists(SchedulePath)
        ? JsonSerializer.Deserialize<List<LocalUpdateSchedule>>(File.ReadAllText(SchedulePath)) ?? []
        : [];

    private void SaveSchedulesLocked(List<LocalUpdateSchedule> schedules)
    {
        string path = SchedulePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(schedules));
        File.Move(temp, path, overwrite: true);
        _schedules = schedules;
    }

    private async Task RunScheduleLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            do
            {
                try
                {
                    Guid[] ids;
                    lock (_scheduleGate) ids = ReadSchedulesLocked().Select(schedule => schedule.Id).ToArray();
                    foreach (Guid id in ids)
                    {
                        ct.ThrowIfCancellationRequested();
                        LocalUpdateSchedule? schedule;
                        lock (_scheduleGate)
                        {
                            schedule = ReadSchedulesLocked().Find(item => item.Id == id);
                            if (schedule is null || !IsScheduleDue(schedule, DateTimeOffset.Now) || !_runningSchedules.Add(id))
                                continue;
                        }

                        await ExecuteScheduleAsync(schedule, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "SteamCMD 更新计划执行失败");
                }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static bool IsScheduleDue(LocalUpdateSchedule schedule, DateTimeOffset now)
    {
        if (schedule.UpdateAt is { } appointment)
            return schedule.LastRunAt is null && now >= appointment;

        if (!TimeOnly.TryParseExact(schedule.DailyAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return false;
        var occurrence = now.Date + time.ToTimeSpan();
        return now.LocalDateTime >= occurrence && schedule.CreatedAt.LocalDateTime <= occurrence &&
            (schedule.LastRunAt is null || schedule.LastRunAt.Value.LocalDateTime < occurrence);
    }

    private async Task ExecuteScheduleAsync(LocalUpdateSchedule schedule, CancellationToken ct)
    {
        bool observingBatch = false;
        try
        {
            var checkedAt = DateTimeOffset.UtcNow;
            var versions = new Dictionary<string, LocalUpdateVersion>(StringComparer.OrdinalIgnoreCase);
            var availableBuilds = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            var results = new List<LocalUpdateBatchItem>();
            foreach (string id in schedule.ServerIds)
            {
                var instance = _manager.Find(id);
                if (instance is null)
                {
                    results.Add(new(id, id, "failed", "服务器已不存在"));
                    continue;
                }

                string install = ResolveInstallDirectory(instance.Definition, out string? error);
                string branch = ResolveBranch(null, instance.Definition, out string? branchError) ?? "public";
                if (error is not null || branchError is not null)
                {
                    results.Add(new(id, instance.Name, "failed", error ?? branchError));
                    continue;
                }

                if (!ulong.TryParse(ReadBuildId(install, _options.SteamAppId), out ulong installed))
                {
                    results.Add(new(id, instance.Name, "failed", "未找到本地构建号，请先手动校验安装目录"));
                    continue;
                }

                if (!versions.TryGetValue(branch, out var version))
                {
                    version = await CheckVersionAsync(branch, ct);
                    versions[branch] = version;
                }
                lock (_versionGate)
                {
                    if (!_versions.TryGetValue(id, out var previous) || !previous.Checking)
                        _versions[id] = version;
                }

                if (!ulong.TryParse(version.LatestBuildId, out ulong remote))
                    results.Add(new(id, instance.Name, "failed", version.Error ?? "无法读取 Steam 构建号"));
                else if (remote > installed)
                {
                    availableBuilds[id] = remote;
                    results.Add(new(id, instance.Name, "available", $"发现新版本：Build {installed} → {remote}"));
                }
                else
                    results.Add(new(id, instance.Name, "current", remote == installed
                        ? $"已是最新版本（Build {installed}）"
                        : $"本地 Build {installed} 高于 Steam 分支的 Build {remote}，保持当前版本"));
            }

            ct.ThrowIfCancellationRequested();
            if (schedule.AutoUpdate && availableBuilds.Count > 0)
            {
                observingBatch = true;
                _ = ApplyScheduledUpdatesAsync(schedule, availableBuilds, results, checkedAt, ct);
                return;
            }

            CompleteSchedule(schedule, results.ToArray(), checkedAt);
        }
        finally
        {
            if (!observingBatch)
                lock (_scheduleGate) _runningSchedules.Remove(schedule.Id);
        }
    }

    private async Task ApplyScheduledUpdatesAsync(LocalUpdateSchedule schedule, Dictionary<string, ulong> availableBuilds,
        List<LocalUpdateBatchItem> checkedItems, DateTimeOffset checkedAt, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Task? previous;
                UpdateBatch? batch = null;
                LocalUpdateBatchResult? started = null;
                lock (_batchGate)
                {
                    previous = _batch is { Active: true } active ? active.Completion : null;
                    if (previous is null)
                    {
                        var pending = new List<string>();
                        for (int index = 0; index < checkedItems.Count; index++)
                        {
                            var item = checkedItems[index];
                            if (item.State != "available") continue;
                            var instance = _manager.Find(item.ServerId);
                            string install = instance is null ? "" : ResolveInstallDirectory(instance.Definition, out _);
                            if (install.Length > 0 && ulong.TryParse(ReadBuildId(install, _options.SteamAppId), out ulong current) &&
                                current >= availableBuilds[item.ServerId])
                                checkedItems[index] = item with { State = "current", Message = $"已更新到 Build {current}" };
                            else
                                pending.Add(item.ServerId);
                        }
                        if (pending.Count > 0)
                            started = StartBatchCore(new(pending.ToArray(), new(Validate: false, Mode: schedule.Mode)), schedule.RequestedBy, out batch);
                    }
                }

                if (previous is not null)
                {
                    await previous.WaitAsync(ct);
                    continue;
                }

                if (batch is not null)
                {
                    await batch.Completion.WaitAsync(ct);
                    var finished = batch.Items.ToDictionary(item => item.ServerId, StringComparer.OrdinalIgnoreCase);
                    checkedItems = checkedItems.Select(item => finished.GetValueOrDefault(item.ServerId) ?? item).ToList();
                }
                else if (started is { Success: false })
                {
                    checkedItems = checkedItems.Select(item => item.State == "available"
                        ? item with { State = "failed", Message = started.Error } : item).ToList();
                }
                ct.ThrowIfCancellationRequested();
                CompleteSchedule(schedule, checkedItems.ToArray(), checkedAt);
                return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "无法保存 SteamCMD 预约结果 {Schedule}", schedule.Id);
        }
        finally
        {
            lock (_scheduleGate) _runningSchedules.Remove(schedule.Id);
        }
    }

    private void CompleteSchedule(LocalUpdateSchedule schedule, LocalUpdateBatchItem[] results, DateTimeOffset checkedAt)
    {
        lock (_scheduleGate)
        {
            var updated = ReadSchedulesLocked().Select(item => item.Id == schedule.Id
                ? item with { LastRunAt = checkedAt, Results = results } : item).ToList();
            SaveSchedulesLocked(updated);
        }
    }
}
