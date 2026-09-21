using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.LocalAdmin.Models;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// LocalAdmin 能力的管理器：把本地服务器实例构建出来，随机器人进程启动/停止，
/// 并支持在**运行时**通过 Web 面板增删改这些服务器定义。
///
/// 未启用时（<see cref="LocalAdminOptions.Enabled"/> = false）不会绑定任何端口、
/// 不会拉起任何进程，对既有行为零影响。
///
/// 实现要点：只对**被改动的那一台**增删实例，绝不整体重建。
/// 因为 <see cref="LocalServerInstance.DisposeAsync"/> 会结束进程树 ——
/// 整体重建会把其它正在运行的服务器一并杀掉。
/// </summary>
public sealed class LocalAdminManager : IHostedService, IAsyncDisposable
{
    private readonly LocalAdminOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LocalAdminManager> _log;
    private readonly string _contentRootPath;
    private readonly LocalServerStore _store;

    private static readonly HashSet<string> DisallowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "bash.exe", "sh.exe", "zsh.exe",
        "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe",
        "certutil.exe", "bitsadmin.exe", "schtasks.exe"
    };

    /// <summary>与 <see cref="_definitions"/> 一一对应、顺序一致。</summary>
    private readonly List<LocalServerInstance> _instances = new();

    private readonly Dictionary<string, LocalServerInstance> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前的服务器定义，顺序即面板上的顺序（第一台为默认选中项）。</summary>
    private readonly List<LocalServerDefinition> _definitions = new();

    /// <summary>增删改期间串行化，避免面板并发点击把配置写坏。</summary>
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _maintenanceTask;

    public LocalAdminManager(
        IOptions<LocalAdminOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<LocalAdminManager> log,
        IHostEnvironment environment)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _log = log;
        _contentRootPath = environment.ContentRootPath;
        _store = new LocalServerStore(_contentRootPath, loggerFactory.CreateLogger<LocalServerStore>());
    }

    /// <summary>能力是否启用。</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>全部实例（按定义顺序）。</summary>
    public IReadOnlyList<LocalServerInstance> Instances => _instances;

    /// <summary>当前的服务器定义（按顺序）。</summary>
    public IReadOnlyList<LocalServerDefinition> Definitions => _definitions;

    /// <summary>是否已落到配置文件（false 表示当前用的是 appsettings 里的种子）。</summary>
    public bool UsingConfigFile => _store.Exists;

    /// <summary>配置文件完整路径，面板上展示给用户。</summary>
    public string ConfigPath => _store.ConfigPath;

    /// <summary>按 Id 查找实例。</summary>
    public LocalServerInstance? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        lock (_gate)
            return _byId.TryGetValue(id.Trim(), out var instance) ? instance : null;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _log.LogInformation("LocalAdmin 能力未启用（LocalAdmin:Enabled = false）");
            return Task.CompletedTask;
        }

        _options.ConsoleBufferLines = Math.Clamp(_options.ConsoleBufferLines, 100, 100000);

        LoadDefinitions();

        if (_definitions.Count == 0)
            _log.LogWarning("LocalAdmin 已启用，但没有任何服务器定义 —— 可在面板「服务器进程」页点「添加服务器」。");

        _cts = new CancellationTokenSource();
        _maintenanceTask = Task.Run(() => MaintenanceLoopAsync(_cts.Token));

        // 自动启动
        foreach (var instance in _instances.Where(i => i.Definition.AutoStart))
        {
            var result = instance.Start();
            if (result.Success)
                _log.LogInformation("已自动启动本地服务器 [{Name}]", instance.Name);
            else
                _log.LogError("自动启动本地服务器 [{Name}] 失败：{Error}", instance.Name, result.Error);
        }

        return Task.CompletedTask;
    }

    /// <summary>启动时载入定义：优先配置文件；没有配置文件则用 appsettings 的 Servers 作种子。</summary>
    private void LoadDefinitions()
    {
        var fromFile = _store.TryLoad(out string? loadError);

        List<LocalServerDefinition> source;

        if (fromFile is not null)
        {
            _log.LogInformation("本地服务器定义来自配置文件：{Path}（共 {Count} 台）", _store.ConfigPath, fromFile.Count);
            source = fromFile;
        }
        else
        {
            if (loadError is not null)
                _log.LogError("{Error} —— 本次回退使用 appsettings 中的 LocalAdmin:Servers", loadError);

            if (_store.Exists)
            {
                _log.LogWarning("配置文件读取失败，本次以 appsettings 种子运行；面板上的修改会覆盖该文件，请先修正它");
            }
            else if (_options.Servers.Count > 0)
            {
                _log.LogInformation(
                    "尚未创建 {File}，本次以 appsettings 的 LocalAdmin:Servers 为种子（{Count} 台）；" +
                    "在面板里保存任意服务器后，该文件将成为唯一事实来源",
                    LocalServerStore.FileName, _options.Servers.Count);
            }

            source = _options.Servers.Select(Clone).ToList();
        }

        lock (_gate)
        {
            _definitions.Clear();
            _instances.Clear();
            _byId.Clear();

            int index = 0;
            foreach (var def in source)
            {
                def.Normalize(index++);

                if (_byId.ContainsKey(def.Id))
                {
                    _log.LogError("本地服务器 Id 重复，已跳过：{Id}", def.Id);
                    continue;
                }

                _definitions.Add(def);
                var instance = new LocalServerInstance(def, _options, _loggerFactory, _contentRootPath);
                _instances.Add(instance);
                _byId[def.Id] = instance;
            }

            _log.LogInformation("LocalAdmin 托管 {Count} 台本地服务器：{Ids}",
                _instances.Count, string.Join(", ", _instances.Select(i => $"{i.Id}({i.Name})")));
        }
    }

    // ==================== 运行时增删改（面板调用） ====================

    /// <summary>
    /// 新增或修改一台服务器。
    /// </summary>
    /// <param name="def">面板提交的定义。</param>
    /// <param name="originalId">修改时的原 Id；新增传 null。</param>
    /// <param name="warning">非致命提示，例如可执行文件当前不存在。</param>
    public LocalAdminResult Save(LocalServerDefinition def, string? originalId, out string? warning)
    {
        warning = null;

        if (!Enabled)
            return LocalAdminResult.Fail("LocalAdmin 能力未启用：请先在 appsettings.json 设置 LocalAdmin:Enabled = true 并重启机器人");

        bool isUpdate = !string.IsNullOrWhiteSpace(originalId);

        // 先规范化，保证 Id 推导与数值钳制规则和启动时一致
        def.Normalize(_definitions.Count);

        if (string.IsNullOrWhiteSpace(def.Name) && string.IsNullOrWhiteSpace(def.Id))
            return LocalAdminResult.Fail("名称不能为空");

        if (string.IsNullOrWhiteSpace(def.ExecutablePath))
            return LocalAdminResult.Fail("可执行文件路径不能为空");

        string exeFileName = Path.GetFileName(def.ExecutablePath).Trim();
        if (DisallowedExecutables.Contains(exeFileName))
            return LocalAdminResult.Fail($"不允许指定系统命令行或系统解释器作为服务端程序：{exeFileName}");

        if (def.GamePort is < 1 or > 65535)
            return LocalAdminResult.Fail($"游戏端口不合法：{def.GamePort}（应在 1-65535 之间）");

        LocalServerInstance? replaced = null;

        lock (_gate)
        {
            var existing = isUpdate && _byId.TryGetValue(originalId!.Trim(), out var found) ? found : null;

            if (isUpdate && existing is null)
                return LocalAdminResult.Fail($"要修改的服务器不存在：{originalId}");

            // Id 不允许通过编辑改名：沿用原 Id，避免 API 路径与前端选中项突然失效
            if (isUpdate)
                def.Id = existing!.Id;
            else if (_byId.ContainsKey(def.Id))
                return LocalAdminResult.Fail($"标识（Id）已存在：{def.Id}，请换一个");

            foreach (var other in _definitions)
            {
                if (isUpdate && string.Equals(other.Id, existing!.Id, StringComparison.OrdinalIgnoreCase))
                    continue;   // 跳过被修改的那条自身

                if (string.Equals(other.Id, def.Id, StringComparison.OrdinalIgnoreCase))
                    return LocalAdminResult.Fail($"标识（Id）已被占用：{def.Id}");

                if (other.GamePort == def.GamePort)
                    return LocalAdminResult.Fail($"游戏端口 {def.GamePort} 已被 [{other.Name}] 使用，两个实例不能共用同一端口");
            }

            if (existing is not null && existing.Running)
                return LocalAdminResult.Fail($"[{existing.Name}] 正在运行，请先停止再修改配置");

            if (!File.Exists(def.ExecutablePath))
                warning = $"已保存，但未找到文件：{def.ExecutablePath} —— 启动会失败，请核对路径";

            var normalized = Clone(def);

            if (isUpdate)
            {
                int pos = IndexOfIdLocked(existing!.Id);
                if (pos < 0)
                    return LocalAdminResult.Fail($"内部状态异常：找不到 [{existing.Id}] 的位置");

                replaced = _instances[pos];
                var newInstance = new LocalServerInstance(normalized, _options, _loggerFactory, _contentRootPath);

                _definitions[pos] = normalized;
                _instances[pos] = newInstance;
                _byId[normalized.Id] = newInstance;
            }
            else
            {
                _definitions.Add(normalized);
                var newInstance = new LocalServerInstance(normalized, _options, _loggerFactory, _contentRootPath);
                _instances.Add(newInstance);
                _byId[normalized.Id] = newInstance;
            }

            _store.Save(_definitions);
        }

        DisposeReplaced(replaced);

        return LocalAdminResult.Ok(isUpdate ? $"已更新服务器 [{def.Name}]" : $"已添加服务器 [{def.Name}]");
    }

    /// <summary>删除一台服务器（正在运行的会被拒绝）。</summary>
    public LocalAdminResult Remove(string id)
    {
        if (!Enabled)
            return LocalAdminResult.Fail("LocalAdmin 能力未启用：请先在 appsettings.json 设置 LocalAdmin:Enabled = true 并重启机器人");

        LocalServerInstance? removed;

        lock (_gate)
        {
            string target = (id ?? "").Trim();

            if (!_byId.TryGetValue(target, out var existing))
                return LocalAdminResult.Fail($"要删除的服务器不存在：{id}");

            if (existing.Running)
                return LocalAdminResult.Fail($"[{existing.Name}] 正在运行，请先停止再删除");

            int pos = IndexOfIdLocked(existing.Id);
            if (pos < 0)
                return LocalAdminResult.Fail($"内部状态异常：找不到 [{existing.Id}] 的位置");

            _definitions.RemoveAt(pos);
            _instances.RemoveAt(pos);
            _byId.Remove(existing.Id);
            removed = existing;

            _store.Save(_definitions);
        }

        DisposeReplaced(removed);

        return LocalAdminResult.Ok($"已删除服务器 [{removed!.Name}]");
    }

    /// <summary>在定义列表里按下标定位（Id 不区分大小写）。</summary>
    private int IndexOfIdLocked(string id) =>
        _definitions.FindIndex(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 释放被替换/删除的实例。调用方已确认它未在运行，所以不会误杀服务器进程。
    /// </summary>
    private void DisposeReplaced(LocalServerInstance? instance)
    {
        if (instance is null)
            return;

        try
        {
            instance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "释放被替换的实例 [{Id}] 时出错", instance.Id);
        }
    }

    /// <summary>拷贝一份定义，避免面板提交的对象与内部状态共享引用。</summary>
    private static LocalServerDefinition Clone(LocalServerDefinition d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        ExecutablePath = d.ExecutablePath,
        WorkingDirectory = d.WorkingDirectory,
        GamePort = d.GamePort,
        ExtraArguments = d.ExtraArguments,
        AutoStart = d.AutoStart,
        EnableHeartbeat = d.EnableHeartbeat,
        HeartbeatSpanMaxThreshold = d.HeartbeatSpanMaxThreshold,
        HeartbeatRestartInSeconds = d.HeartbeatRestartInSeconds,
        RestartOnCrash = d.RestartOnCrash,
        RestartLimit = d.RestartLimit,
        RestartTimeWindowSeconds = d.RestartTimeWindowSeconds,
        GracefulStopTimeoutSeconds = d.GracefulStopTimeoutSeconds,
        LaToSlBufferSize = d.LaToSlBufferSize,
        SlToLaBufferSize = d.SlToLaBufferSize,
        DisableAnsiColors = d.DisableAnsiColors,
        RedirectStandardStreams = d.RedirectStandardStreams,
        ConsoleLevel = d.ConsoleLevel,
    };

    /// <summary>
    /// 持久化某实例的控制台捕获级别。
    ///
    /// 只改**定义与配置文件**，不重建实例 —— 重建会调用 <c>DisposeAsync</c> 从而杀掉运行中的进程。
    /// 级别本身由 <see cref="LocalServerInstance.SetConsoleLevel"/> 在运行时直接切换。
    /// </summary>
    public LocalAdminResult PersistConsoleLevel(string id, string? levelKey)
    {
        if (!Enabled)
            return LocalAdminResult.Fail("LocalAdmin 能力未启用：请先在 appsettings.json 设置 LocalAdmin:Enabled = true 并重启机器人");

        if (!ConsoleCaptureLevels.TryParseKey(levelKey, out _))
            return LocalAdminResult.Fail($"控制台级别不合法：{levelKey}");

        lock (_gate)
        {
            var instance = Find(id);
            if (instance is null)
                return LocalAdminResult.Fail($"本地服务器实例不存在：{id}");

            string normalized = ConsoleCaptureLevels.NormalizeKey(levelKey);

            // 定义对象与实例共享同一引用，改这里即同步了两侧
            instance.Definition.ConsoleLevel = normalized;

            try
            {
                _store.Save(_definitions);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "保存控制台级别失败（内存中的设置已生效）");
                return LocalAdminResult.Fail($"设置已生效，但写入配置文件失败：{ex.Message}");
            }

            return LocalAdminResult.Ok(
                $"控制台级别已保存为 {ConsoleCaptureLevels.Label(ParseLevelSafe(normalized))}");
        }
    }

    private static ConsoleCaptureLevel ParseLevelSafe(string key) =>
        ConsoleCaptureLevels.TryParseKey(key, out var level) ? level : ConsoleCaptureLevel.All;

    // ==================== 本地功能（对应官方 LocalAdmin 自带指令） ====================

    /// <summary>
    /// 对应官方指令 <c>lacfg</c>：打印当前生效的配置与配置文件路径。
    /// </summary>
    public LocalConfigInfo GetConfigInfo()
    {
        lock (_gate)
        {
            return new LocalConfigInfo(
                Enabled,
                _contentRootPath,
                _store.ConfigPath,
                _store.Exists,
                _store.Exists ? "config-file" : "appsettings",
                _definitions.Count,
                new LocalGlobalConfigDto(
                    _options.ConsoleBufferLines,
                    _options.LogDirectory,
                    _options.WriteLogFiles,
                    _options.LogExpirationDays,
                    _options.DefaultExecutablePath),
                _definitions.Select(d => new LocalServerConfigSummaryDto(
                    d.Id,
                    d.Name,
                    d.GamePort,
                    d.ConsoleLevel,
                    d.AutoStart)).ToList());
        }
    }

    /// <summary>
    /// 对应官方指令 <c>resave</c>：把内存中的配置重新写回配置文件。
    /// 顺带做一次规范化，因此也能用来「清洗」被手改乱的配置。
    /// 配置文件原本不存在时会因此被创建 —— 此后它成为唯一事实来源。
    /// </summary>
    public LocalAdminResult ResaveConfig(out string path, out int count)
    {
        path = _store.ConfigPath;
        count = 0;

        if (!Enabled)
            return LocalAdminResult.Fail("LocalAdmin 能力未启用：请先在 appsettings.json 设置 LocalAdmin:Enabled = true 并重启机器人");

        lock (_gate)
        {
            try
            {
                for (int i = 0; i < _definitions.Count; i++)
                    _definitions[i].Normalize(i);

                _store.Save(_definitions);
                count = _definitions.Count;

                _log.LogInformation("已重写本地服务器配置（resave）：{Count} 台 → {Path}", count, path);
                return LocalAdminResult.Ok($"配置已重写：{count} 台服务器 → {path}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "重写本地服务器配置失败");
                return LocalAdminResult.Fail($"重写配置失败：{ex.Message}");
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        foreach (var instance in _instances)
        {
            try
            {
                await instance.DisposeAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "释放本地服务器实例 [{Id}] 时出错", instance.Id);
            }
        }
    }

    /// <summary>每 6 小时清理一次过期日志（仅在 LogExpirationDays &gt; 0 时生效）。</summary>
    private async Task MaintenanceLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            await CleanupLogsAsync(ct);

            while (await timer.WaitForNextTickAsync(ct))
                await CleanupLogsAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LocalAdmin 维护循环异常");
        }
    }

    private Task CleanupLogsAsync(CancellationToken ct)
    {
        // 默认 0 = 不清理，避免任何误删风险；需要时由使用者在配置里显式开启
        if (_options.LogExpirationDays <= 0 || !_options.WriteLogFiles)
            return Task.CompletedTask;

        try
        {
            string root = Path.IsPathRooted(_options.LogDirectory)
                ? _options.LogDirectory
                : Path.Combine(_contentRootPath, _options.LogDirectory);

            if (!Directory.Exists(root))
                return Task.CompletedTask;

            DateTime cutoff = DateTime.Now.Date.AddDays(-_options.LogExpirationDays);
            int removed = 0;

            // 只处理本模块自己创建的目录结构（root/<实例Id>/yyyy-MM-dd.log）
            foreach (string dir in Directory.GetDirectories(root))
            {
                ct.ThrowIfCancellationRequested();

                foreach (string file in Directory.GetFiles(dir, "*.log", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();

                    // 只删形如 yyyy-MM-dd.log 且早于保留期的文件
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (!DateTime.TryParseExact(stem, "yyyy-MM-dd", null,
                            System.Globalization.DateTimeStyles.None, out DateTime date))
                        continue;

                    if (date >= cutoff)
                        continue;

                    File.Delete(file);
                    removed++;
                }
            }

            if (removed > 0)
                _log.LogInformation("已清理 {Count} 个过期控制台日志（保留 {Days} 天）", removed, _options.LogExpirationDays);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "清理过期控制台日志失败");
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }
}
