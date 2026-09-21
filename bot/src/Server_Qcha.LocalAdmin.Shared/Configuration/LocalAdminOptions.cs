namespace Server.Qcat.Configuration;

/// <summary>
/// LocalAdmin 能力配置 —— 让程序（独立守护进程或内嵌在机器人中）扮演官方 LocalAdmin 的角色，
/// 负责拉起/停止 SCPSL 专用服务器进程，并通过官方控制台协议与之双向通信。
///
/// 运行架构：
/// - "Daemon"（推荐默认）：连接独立的 Server_Qcha.Daemon 守护进程，升级/重启面板不会断开游戏服；
/// - "Embedded"：内嵌单进程模式，管理逻辑运行在 Bot 主进程内（关闭 Bot 则游戏服退出）。
/// </summary>
public sealed class LocalAdminOptions
{
    /// <summary>是否启用 LocalAdmin 能力（默认关闭）。</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 运行模式：
    /// "Daemon"（默认）：连接独立守护进程（Server_Qcha.Daemon.exe），升级或关闭面板不影响游戏服；
    /// "Embedded"：单进程内嵌模式，随面板主进程一同退出。
    /// </summary>
    public string Mode { get; set; } = "Daemon";

    /// <summary>独立守护节点的 HTTP 监听地址（仅在 Mode = "Daemon" 时生效）。默认 http://127.0.0.1:10090</summary>
    public string DaemonUri { get; set; } = "http://127.0.0.1:10090";

    /// <summary>与守护节点通信的共享凭证 Token（防止未授权调用）。</summary>
    public string DaemonToken { get; set; } = "QchaSecret_123";

    /// <summary>当守护节点未运行时，是否由 Bot 尝试自动后台拉起 Server_Qcha.Daemon.exe。</summary>
    public bool AutoStartDaemon { get; set; } = true;

    /// <summary>
    /// 「添加服务器」时预填/优先推荐的可执行文件路径（可留空）。
    /// 留空时面板会自动探测：优先取本机正在运行的 SCPSL 进程，其次扫 Steam 各库目录。
    /// </summary>
    public string DefaultExecutablePath { get; set; } = "";

    /// <summary>单个服务器实例的控制台环形缓冲行数上限。</summary>
    public int ConsoleBufferLines { get; set; } = 2000;

    /// <summary>控制台日志落盘目录（相对 ContentRoot 或绝对路径）。</summary>
    public string LogDirectory { get; set; } = "localadmin-logs";

    /// <summary>是否把控制台输出写入日志文件。</summary>
    public bool WriteLogFiles { get; set; } = true;

    /// <summary>
    /// 日志保留天数。0 表示不自动清理（默认，避免误删文件）；
    /// 设为正数后，启动时与每天一次清理超过该天数的日志文件。
    /// </summary>
    public int LogExpirationDays { get; set; }

    /// <summary>由本进程托管的服务器列表。</summary>
    public List<LocalServerDefinition> Servers { get; set; } = new();
}

/// <summary>一台由 LocalAdmin 托管（启动/停止/监控）的 SCPSL 服务端进程定义。</summary>
public sealed class LocalServerDefinition
{
    /// <summary>实例唯一标识，用于 API 路径。留空则由 Name 推导。</summary>
    public string Id { get; set; } = "";

    /// <summary>展示名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>SCPSL.exe 的完整路径。</summary>
    public string ExecutablePath { get; set; } = "";

    /// <summary>工作目录，留空则取可执行文件所在目录。</summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>游戏服务端口（即游戏端 <c>-port</c>）。</summary>
    public int GamePort { get; set; } = 7777;

    /// <summary>透传给游戏的额外参数（对应官方 <c>--</c> 之后的内容）。</summary>
    public string ExtraArguments { get; set; } = "";

    /// <summary>进程管理器启动时是否自动拉起该服务器。</summary>
    public bool AutoStart { get; set; }

    // ---------- 心跳 / 静默崩溃检测 ----------

    /// <summary>是否启用静默崩溃检测（对应官方 <c>enable_heartbeat</c>）。</summary>
    public bool EnableHeartbeat { get; set; } = true;

    /// <summary>心跳间隔超过该秒数即视为异常（对应 <c>heartbeat_span_max_threshold</c>）。</summary>
    public int HeartbeatSpanMaxThreshold { get; set; } = 30;

    /// <summary>判定异常后的重启倒计时秒数（对应 <c>heartbeat_restart_in_seconds</c>）。</summary>
    public int HeartbeatRestartInSeconds { get; set; } = 11;

    // ---------- 崩溃与重启策略 ----------

    /// <summary>崩溃后是否自动重启（对应 <c>restart_on_crash</c>）。</summary>
    public bool RestartOnCrash { get; set; } = true;

    /// <summary>时间窗口内允许的最大重启次数（对应官方 <c>--restartsLimit</c>）。</summary>
    public int RestartLimit { get; set; } = 4;

    /// <summary>重启次数统计窗口秒数（对应官方 <c>--restartsTimeWindow</c>）。</summary>
    public int RestartTimeWindowSeconds { get; set; } = 480;

    /// <summary>优雅停止时等待进程自行退出的秒数，超时后强制结束。</summary>
    public int GracefulStopTimeoutSeconds { get; set; } = 30;

    // ---------- 协议缓冲 ----------

    /// <summary>LocalAdmin → 游戏 方向缓冲区字节数（对应 <c>la_to_sl_buffer_size</c>）。</summary>
    public int LaToSlBufferSize { get; set; } = 25000;

    /// <summary>游戏 → LocalAdmin 方向缓冲区字节数（对应 <c>sl_to_la_buffer_size</c>）。</summary>
    public int SlToLaBufferSize { get; set; } = 200000;

    /// <summary>
    /// 是否禁用游戏端 ANSI 颜色。
    /// 官方在 Windows 上默认禁用（<c>-disableAnsiColors</c>），因为控制台颜色由协议的颜色码承载。
    /// </summary>
    public bool DisableAnsiColors { get; set; } = true;

    /// <summary>
    /// 是否重定向游戏进程的 stdout/stderr 到面板控制台。
    /// 关闭后只能看到控制台协议通道的内容（游戏启动早期日志会丢失）。
    /// </summary>
    public bool RedirectStandardStreams { get; set; } = true;

    /// <summary>
    /// 面板「服务器进程」页控制台的**捕获级别**（决定采集哪些输出到控制台缓冲）。
    /// 取值为 <c>all</c> / <c>normal</c> / <c>warn</c> / <c>error</c> / <c>off</c>；
    /// 非法值按 <c>all</c> 处理。可在面板上实时切换，无需重启进程。
    /// </summary>
    public string ConsoleLevel { get; set; } = "all";

    /// <summary>规范化并回填由 Name 推导的 Id。</summary>
    public void Normalize(int index)
    {
        if (string.IsNullOrWhiteSpace(Name))
            Name = string.IsNullOrWhiteSpace(Id) ? $"本地服-{index + 1}" : Id;

        if (string.IsNullOrWhiteSpace(Id))
        {
            var chars = Name.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            Id = new string(chars).Trim('-');
            if (Id.Length == 0)
                Id = $"local-{index + 1}";
        }

        Id = Id.Trim();

        if (string.IsNullOrWhiteSpace(WorkingDirectory) && !string.IsNullOrWhiteSpace(ExecutablePath))
        {
            try
            {
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(ExecutablePath)) ?? "";
            }
            catch (Exception)
            {
                WorkingDirectory = "";
            }
        }

        HeartbeatSpanMaxThreshold = Math.Clamp(HeartbeatSpanMaxThreshold, 5, 3600);
        HeartbeatRestartInSeconds = Math.Clamp(HeartbeatRestartInSeconds, 1, 600);
        RestartLimit = Math.Clamp(RestartLimit, 0, 100);
        RestartTimeWindowSeconds = Math.Clamp(RestartTimeWindowSeconds, 10, 86400);
        GracefulStopTimeoutSeconds = Math.Clamp(GracefulStopTimeoutSeconds, 1, 600);
        LaToSlBufferSize = Math.Clamp(LaToSlBufferSize, 101, 16 * 1024 * 1024);
        SlToLaBufferSize = Math.Clamp(SlToLaBufferSize, 351, 16 * 1024 * 1024);
    }
}
