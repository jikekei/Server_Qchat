using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Server.Qcat.Configuration;

namespace Server.Qcat.LocalAdmin;

/// <summary>实例状态快照（供面板展示）。</summary>
public sealed record LocalServerStatus(
    string Id,
    string Name,
    string ExecutablePath,
    string WorkingDirectory,
    int GamePort,
    bool Running,
    int? ProcessId,
    int ConsolePort,
    bool ConsoleConnected,
    string HeartbeatStatus,
    bool HeartbeatEnabled,
    DateTime? LastHeartbeatAt,
    int HeartbeatRestartStage,
    int RestartCountdownSeconds,
    string ExitAction,
    int RestartsInWindow,
    int RestartLimit,
    bool RestartBudgetExhausted,
    DateTime? StartedAt,
    double UptimeSeconds,
    bool AutoStart,
    string? LastError,
    string ConsoleLevel);

/// <summary>控制类操作的统一返回。</summary>
public sealed record LocalAdminResult(bool Success, string Message, string? Error = null)
{
    public static LocalAdminResult Ok(string message) => new(true, message);

    public static LocalAdminResult Fail(string error) => new(false, error, error);
}

/// <summary>
/// 一台由机器人托管的 SCPSL 服务端实例 —— 等价于官方 LocalAdmin 的单个会话。
///
/// 职责：
/// <list type="number">
/// <item>以 TCP 监听方身份绑定 127.0.0.1 随机端口（控制台通道）；</item>
/// <item>用官方参数拼装命令行并拉起游戏进程（含 <c>-console</c> / <c>-id</c> / <c>-heartbeat</c>）；</item>
/// <item>解析上行帧（输出行 / 控制码），维护控制台环形缓冲与日志；</item>
/// <item>把面板下发的文本以下行帧写入游戏控制台；</item>
/// <item>心跳状态机 + 静默崩溃检测 + 重启限流；</item>
/// <item>退出动作协商，区分崩溃 / 正常关服 / 静默关服 / 重启。</item>
/// </list>
/// </summary>
public sealed class LocalServerInstance : IAsyncDisposable
{
    /// <summary>等待首个心跳的耐心期（与官方一致：80 秒）。</summary>
    private static readonly TimeSpan FirstHeartbeatGrace = TimeSpan.FromSeconds(80);

    /// <summary>进程退出后、重启前的间隔（避免端口尚未释放）。</summary>
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);

    /// <summary>游戏在退出前声明的意图。</summary>
    private enum ExitAction
    {
        Crash,
        Shutdown,
        SilentShutdown,
        Restart,
    }

    /// <summary>本端主动触发的退出，其后的处理意图。</summary>
    private enum PendingAction
    {
        /// <summary>不重启</summary>
        None,

        /// <summary>重启，计入限流</summary>
        RestartCounted,

        /// <summary>重启，不计入限流</summary>
        RestartUncounted,
    }

    private enum HeartbeatState
    {
        Disabled,
        AwaitingFirstHeartbeat,
        Active,
    }

    private readonly object _sync = new();
    private readonly LocalServerDefinition _def;
    private readonly LocalAdminOptions _options;

    /// <summary>
    /// 控制台捕获级别（以 int 存放，便于用 <see cref="Volatile"/> 读写；
    /// 面板可在运行中切换，因此不能放进只读的 <c>_def</c>）。
    /// </summary>
    private int _captureLevel = (int)ConsoleCaptureLevel.All;
    private readonly ILogger _log;
    private readonly string _logDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();

    // ---- 控制台缓冲 ----
    private readonly List<ConsoleLine> _lines;
    private long _nextSeq = 1;
    private long _firstSeq = 1;

    // ---- 网络 ----
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private int _consolePort;

    // ---- 进程 ----
    private Process? _process;
    private TaskCompletionSource<bool>? _exitTcs;
    private DateTime? _startedAt;

    // ---- 状态机 ----
    private ExitAction _exitAction = ExitAction.Crash;
    private PendingAction _pending = PendingAction.None;
    private bool _ignoreExitSignals;
    private HeartbeatState _heartbeat = HeartbeatState.Disabled;
    private DateTime _lastHeartbeatAt;
    private DateTime? _connectedAt;
    private bool _firstHeartbeatWarned;
    private int _warningStage;
    private int _restartCount;
    private DateTime _restartWindowStart;
    private bool _restartBudgetExhausted;
    private string? _lastError;
    private bool _consoleConnected;
    private volatile bool _disposed;

    // ---- 日志 ----
    private StreamWriter? _logWriter;
    private DateTime _logDate = DateTime.MinValue;

    public LocalServerInstance(
        LocalServerDefinition definition,
        LocalAdminOptions options,
        ILoggerFactory loggerFactory,
        string contentRootPath)
    {
        _def = definition;
        _options = options;
        _log = loggerFactory.CreateLogger($"Server.Qcat.LocalAdmin.{definition.Id}");
        _lines = new List<ConsoleLine>(Math.Min(options.ConsoleBufferLines, 4096));

        string root = Path.IsPathRooted(options.LogDirectory)
            ? options.LogDirectory
            : Path.Combine(contentRootPath, options.LogDirectory);
        _logDirectory = Path.Combine(root, definition.Id);

        // 定义里的级别键可能被手改坏，这里统一规范化一次
        _def.ConsoleLevel = ConsoleCaptureLevels.NormalizeKey(definition.ConsoleLevel);
        Volatile.Write(ref _captureLevel, (int)ParseLevel(_def.ConsoleLevel));

        _log.LogInformation("本地服务器实例已构建：[{Name}] exe={Exe} gamePort={GamePort} autoStart={AutoStart} consoleLevel={Level}",
            definition.Name, definition.ExecutablePath, definition.GamePort, definition.AutoStart, _def.ConsoleLevel);
    }

    /// <summary>当前控制台捕获级别的键名（供面板展示）。</summary>
    public string ConsoleLevelKey =>
        ConsoleCaptureLevels.ToKey((ConsoleCaptureLevel)Volatile.Read(ref _captureLevel));

    private static ConsoleCaptureLevel ParseLevel(string? key) =>
        ConsoleCaptureLevels.TryParseKey(key, out var level) ? level : ConsoleCaptureLevel.All;

    /// <summary>
    /// 切换控制台捕获级别。只影响**之后**的输出（已被过滤掉的行不会恢复）。
    /// </summary>
    public LocalAdminResult SetConsoleLevel(string? key)
    {
        if (!ConsoleCaptureLevels.TryParseKey(key, out var level))
            return LocalAdminResult.Fail(
                $"控制台级别不合法：{key}（可选 {string.Join(" / ", ConsoleCaptureLevels.Keys)}）");

        int previous = Volatile.Read(ref _captureLevel);
        if (previous == (int)level)
            return LocalAdminResult.Ok($"控制台级别已是 {ConsoleCaptureLevels.Label(level)}");

        Volatile.Write(ref _captureLevel, (int)level);
        _def.ConsoleLevel = ConsoleCaptureLevels.ToKey(level);

        Info($"控制台显示级别已切换为「{ConsoleCaptureLevels.Label(level)}」（仅影响之后的新输出）");

        return LocalAdminResult.Ok($"控制台级别已切换为 {ConsoleCaptureLevels.Label(level)}");
    }

    public string Id => _def.Id;

    public string Name => _def.Name;

    public LocalServerDefinition Definition => _def;

    public bool Running => GetStatus().Running;

    // ==================================================================
    //  状态
    // ==================================================================

    public LocalServerStatus GetStatus()
    {
        lock (_sync)
        {
            Process? process = _process;
            bool running = false;
            int? pid = null;

            if (process is not null)
            {
                try
                {
                    running = !process.HasExited;
                    if (running)
                        pid = process.Id;
                }
                catch (Exception)
                {
                    // 进程对象已被释放或不可访问，按未运行处理
                    running = false;
                }
            }

            DateTime? startedAt = running ? _startedAt : null;
            double uptime = startedAt is null ? 0 : Math.Max(0, (DateTime.Now - startedAt.Value).TotalSeconds);
            int countdown = _warningStage > 0
                ? Math.Max(0, _def.HeartbeatRestartInSeconds - _warningStage)
                : 0;

            return new LocalServerStatus(
                _def.Id,
                _def.Name,
                _def.ExecutablePath,
                _def.WorkingDirectory,
                _def.GamePort,
                running,
                pid,
                _consolePort,
                _consoleConnected,
                _heartbeat switch
                {
                    HeartbeatState.Disabled => "disabled",
                    HeartbeatState.AwaitingFirstHeartbeat => "awaiting",
                    _ => "active",
                },
                _def.EnableHeartbeat,
                _lastHeartbeatAt == default ? null : _lastHeartbeatAt,
                _warningStage,
                countdown,
                _exitAction switch
                {
                    ExitAction.Crash => "crash",
                    ExitAction.Shutdown => "shutdown",
                    ExitAction.SilentShutdown => "silent-shutdown",
                    _ => "restart",
                },
                _restartCount,
                _def.RestartLimit,
                _restartBudgetExhausted,
                startedAt,
                Math.Round(uptime, 1),
                _def.AutoStart,
                _lastError,
                ConsoleCaptureLevels.ToKey((ConsoleCaptureLevel)Volatile.Read(ref _captureLevel)));
        }
    }

    // ==================================================================
    //  控制台缓冲
    // ==================================================================

    /// <summary>
    /// 增量读取控制台行。<paramref name="afterSeq"/> 为客户端已收到的最大序号。
    /// 返回的行按序号升序，最多 <paramref name="limit"/> 条；
    /// 若客户端已落后于缓冲窗口，<paramref name="truncated"/> 为 true。
    /// </summary>
    public IReadOnlyList<ConsoleLine> ReadLines(long afterSeq, int limit, out long firstSeq, out long lastSeq, out bool truncated)
    {
        lock (_sync)
        {
            firstSeq = _firstSeq;
            lastSeq = _nextSeq - 1;
            truncated = afterSeq > 0 && afterSeq < _firstSeq - 1;

            if (_lines.Count == 0 || afterSeq >= lastSeq)
                return Array.Empty<ConsoleLine>();

            int skip = (int)Math.Max(0, afterSeq - _firstSeq + 1);
            if (skip >= _lines.Count)
                return Array.Empty<ConsoleLine>();

            return _lines.Skip(skip).Take(limit).ToList();
        }
    }

    public void ClearBuffer()
    {
        lock (_sync)
        {
            _lines.Clear();
            _firstSeq = _nextSeq;
        }
    }

    private void Append(ConsoleLineKind kind, byte color, string text)
    {
        if (text is null)
            return;

        // 捕获级别过滤：放在最前面，被过滤的行既不进缓冲、也不落盘、也不会发给面板
        var level = (ConsoleCaptureLevel)Volatile.Read(ref _captureLevel);
        if (!ConsoleCaptureLevels.ShouldCapture(level, kind, color))
            return;

        // 控制台输出可能自带换行，逐行拆开更贴近终端观感；保留中间的空行（游戏用它们排版）
        foreach (string piece in SplitLines(text))
        {
            string hex = ConsoleProtocol.ColorToHex(color);
            ConsoleLine line;

            lock (_sync)
            {
                line = new ConsoleLine(_nextSeq++, DateTime.Now, kind, color, piece, hex);
                _lines.Add(line);

                int capacity = _options.ConsoleBufferLines;
                if (_lines.Count > capacity)
                {
                    int drop = _lines.Count - capacity;
                    _lines.RemoveRange(0, drop);
                    _firstSeq += drop;
                }
            }

            WriteLogLine(line);
        }
    }

    /// <summary>按行拆分，仅丢弃末尾因换行产生的空片段（保证每帧恰好对应一行）。</summary>
    private static List<string> SplitLines(string text)
    {
        if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
            return new List<string> { text.TrimEnd() };

        var pieces = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
        if (pieces.Count > 1 && pieces[^1].Length == 0)
            pieces.RemoveAt(pieces.Count - 1);

        return pieces;
    }

    /// <summary>面板提示信息统一用 System 类型 + 灰色。</summary>
    private void Info(string text) => Append(ConsoleLineKind.System, 7, $"[面板] {text}");

    private void Warn(string text) => Append(ConsoleLineKind.System, 14, $"[面板] {text}");

    private void Error(string text) => Append(ConsoleLineKind.System, 12, $"[面板] {text}");

    // ==================================================================
    //  日志落盘
    // ==================================================================

    private void WriteLogLine(ConsoleLine line)
    {
        if (!_options.WriteLogFiles)
            return;

        try
        {
            lock (_sync)
            {
                if (_logWriter is null || _logDate != DateTime.Now.Date)
                {
                    _logWriter?.Dispose();
                    Directory.CreateDirectory(_logDirectory);
                    string path = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
                    _logWriter = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
                    {
                        AutoFlush = true,
                    };
                    _logDate = DateTime.Now.Date;
                }

                _logWriter.WriteLine($"{line.Time:yyyy-MM-dd HH:mm:ss.fff} [{line.Kind}] {line.Text}");
            }
        }
        catch (Exception ex)
        {
            // 日志失败绝不能影响控制台链路
            _log.LogWarning(ex, "写入控制台日志失败（实例 {Id}）", _def.Id);
        }
    }

    // ==================================================================
    //  生命周期：监听器
    // ==================================================================

    /// <summary>
    /// 绑定控制台监听端口（仅回环，端口随机）。幂等；必须在启动游戏进程前调用，
    /// 因为端口要通过命令行传给游戏。
    /// </summary>
    private int EnsureListener()
    {
        lock (_sync)
        {
            if (_listener is not null)
                return _consolePort;

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            _listener = listener;
            _consolePort = ((IPEndPoint)listener.LocalEndpoint).Port;

            _log.LogInformation("控制台通道监听 127.0.0.1:{Port}（实例 {Id}）", _consolePort, _def.Id);
            Info($"控制台通道已监听 127.0.0.1:{_consolePort}");

            _ = Task.Run(() => AcceptLoopAsync(_lifetimeCts.Token));
            _ = Task.Run(() => HeartbeatMonitorLoopAsync(_lifetimeCts.Token));
            return _consolePort;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpListener listener;
            lock (_sync)
                listener = _listener!;

            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                if (ct.IsCancellationRequested)
                    return;
                _log.LogWarning(ex, "接受控制台连接失败（实例 {Id}）", _def.Id);
                continue;
            }

            try
            {
                await HandleConnectionAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "控制台连接处理异常（实例 {Id}）", _def.Id);
                Error($"控制台连接异常：{ex.Message}");
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        client.ReceiveBufferSize = _def.SlToLaBufferSize;
        client.SendBufferSize = _def.LaToSlBufferSize;

        NetworkStream stream = client.GetStream();

        lock (_sync)
        {
            // 新连接顶掉旧连接（游戏重启后重连的场景）
            _stream = stream;
            _client = client;
            _consoleConnected = true;
            _connectedAt = DateTime.Now;
            _firstHeartbeatWarned = false;
            _lastHeartbeatAt = DateTime.Now;
            _heartbeat = _def.EnableHeartbeat ? HeartbeatState.AwaitingFirstHeartbeat : HeartbeatState.Disabled;
            _exitAction = ExitAction.Crash;
        }

        Info("游戏进程已接入控制台通道。");
        _log.LogInformation("控制台通道已连接（实例 {Id}）", _def.Id);

        var codeBuffer = new byte[1];
        var lengthBuffer = new byte[ConsoleProtocol.LengthPrefixSize];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 官方实现此处用 DataAvailable 轮询（10ms 忙等）；
                // 这里改为真正的异步阻塞读，省掉忙等开销，也不会在
                // 「长度头已到、正文未到」的情况下无限空转。
                await stream.ReadExactlyAsync(codeBuffer, 0, 1, ct);

                byte code = codeBuffer[0];

                if (code < ConsoleProtocol.ColorCodeLimit)
                {
                    await stream.ReadExactlyAsync(lengthBuffer, 0, ConsoleProtocol.LengthPrefixSize, ct);
                    int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

                    if (length < 0 || length > ConsoleProtocol.MaxFrameBytes)
                    {
                        Error($"收到非法的数据帧长度 {length}，已断开控制台连接。");
                        _log.LogWarning("非法帧长度 {Length}（实例 {Id}），断开连接", length, _def.Id);
                        break;
                    }

                    if (length == 0)
                    {
                        Append(ConsoleLineKind.Output, code, string.Empty);
                        continue;
                    }

                    var payload = new byte[length];
                    await stream.ReadExactlyAsync(payload, 0, length, ct);
                    Append(ConsoleLineKind.Output, code, ConsoleProtocol.DecodeOutput(payload, length));
                }
                else
                {
                    HandleControl((ConsoleControlCode)code);
                }
            }
        }
        catch (EndOfStreamException)
        {
            Info("控制台连接已被对端关闭。");
        }
        catch (IOException ex)
        {
            _log.LogDebug(ex, "控制台连接 IO 结束（实例 {Id}）", _def.Id);
        }
        finally
        {
            lock (_sync)
            {
                _consoleConnected = false;
                _connectedAt = null;
                _stream = null;
                _client = null;
            }

            try
            {
                stream.Dispose();
                client.Dispose();
            }
            catch (Exception)
            {
                // 忽略释放异常
            }

            _log.LogInformation("控制台通道断开（实例 {Id}）", _def.Id);
        }
    }

    private void HandleControl(ConsoleControlCode code)
    {
        switch (code)
        {
            case ConsoleControlCode.Heartbeat:
                HandleHeartbeat();
                return;

            case ConsoleControlCode.RoundRestart:
                Info("游戏报告：回合重启。");
                return;

            case ConsoleControlCode.IdleEnter:
                Info("游戏进入空闲态。");
                return;

            case ConsoleControlCode.IdleExit:
                Info("游戏退出空闲态。");
                return;

            case ConsoleControlCode.ExitActionReset:
            case ConsoleControlCode.ExitActionShutdown:
            case ConsoleControlCode.ExitActionSilentShutdown:
            case ConsoleControlCode.ExitActionRestart:
                ExitAction action = code switch
                {
                    ConsoleControlCode.ExitActionReset => ExitAction.Crash,
                    ConsoleControlCode.ExitActionShutdown => ExitAction.Shutdown,
                    ConsoleControlCode.ExitActionSilentShutdown => ExitAction.SilentShutdown,
                    _ => ExitAction.Restart,
                };

                lock (_sync)
                {
                    if (_ignoreExitSignals)
                    {
                        // 本端已接管退出语义（用户点了停止/重启），忽略游戏声明
                        return;
                    }

                    if (!_def.EnableHeartbeat && code == ConsoleControlCode.ExitActionReset)
                        Warn("收到崩溃信号，但该信号被忽略（心跳检测已关闭时仍会记录退出动作）。");

                    _exitAction = action;
                }

                Info($"收到退出动作声明：{ConsoleProtocol.DescribeControl(code)}");
                return;

            default:
                Warn($"收到未知控制码 0x{(byte)code:X2}。");
                return;
        }
    }

    // ==================================================================
    //  心跳与静默崩溃检测
    // ==================================================================

    private void HandleHeartbeat()
    {
        lock (_sync)
        {
            if (_heartbeat == HeartbeatState.Disabled)
            {
                if (_def.EnableHeartbeat)
                    Info("收到心跳信号，但心跳监控当前处于禁用状态。");
                return;
            }

            _lastHeartbeatAt = DateTime.Now;

            if (_heartbeat == HeartbeatState.AwaitingFirstHeartbeat)
            {
                _heartbeat = HeartbeatState.Active;
            }
        }

        Info("收到心跳，静默崩溃检测已生效。");
        _log.LogDebug("心跳已刷新（实例 {Id}）", _def.Id);
    }

    private async Task HeartbeatMonitorLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct))
            {
                bool heartbeatRestart = false;
                int restartDelaySeconds = 0;

                lock (_sync)
                {
                    if (_heartbeat == HeartbeatState.AwaitingFirstHeartbeat
                        && _connectedAt is not null
                        && !_firstHeartbeatWarned
                        && DateTime.Now - _connectedAt.Value > FirstHeartbeatGrace)
                    {
                        _firstHeartbeatWarned = true;
                        Warn("至今未收到任何心跳，静默崩溃检测未生效。");
                    }

                    if (_heartbeat == HeartbeatState.Active)
                    {
                        double elapsed = (DateTime.Now - _lastHeartbeatAt).TotalSeconds;

                        if (elapsed <= _def.HeartbeatSpanMaxThreshold)
                        {
                            if (_warningStage != 0)
                            {
                                _warningStage = 0;
                                Warn("心跳已恢复，重启流程已中止。");
                            }
                        }
                        else
                        {
                            _warningStage++;

                            if (_def.RestartLimit == 0)
                            {
                                // 限流为 0 表示禁用重启：只告警，不动作
                                Warn($"已 {elapsed:F0} 秒未收到心跳（重启已按配置禁用）。");
                            }
                            else if (_warningStage >= _def.HeartbeatRestartInSeconds)
                            {
                                _warningStage = 0;
                                heartbeatRestart = true;
                                Error("游戏服务端疑似静默崩溃，正在重启…");
                            }
                            else
                            {
                                restartDelaySeconds = _def.HeartbeatRestartInSeconds - _warningStage;
                                Error($"已 {elapsed:F0} 秒未收到心跳，将在 {restartDelaySeconds} 秒后重启（可在面板中止）。");
                            }
                        }
                    }
                }

                if (heartbeatRestart)
                {
                    lock (_sync)
                    {
                        _ignoreExitSignals = true;
                        _pending = PendingAction.RestartCounted;
                    }

                    TriggerProcessExit(force: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "心跳监控循环异常（实例 {Id}）", _def.Id);
        }
    }

    /// <summary>中止重启倒计时（等价官方 <c>hbc</c>：回到等待首个心跳）。</summary>
    public LocalAdminResult CancelRestartCountdown()
    {
        lock (_sync)
        {
            if (_heartbeat != HeartbeatState.Active)
                return LocalAdminResult.Fail("静默崩溃检测当前未处于监控状态。");

            if (_warningStage == 0)
                return LocalAdminResult.Fail("当前没有正在进行的重启倒计时。");

            _warningStage = 0;
            _heartbeat = HeartbeatState.AwaitingFirstHeartbeat;
        }

        Warn("重启倒计时已中止。下次收到心跳后恢复崩溃检测（如需彻底关闭，请使用 hbctrl disable）。");
        return LocalAdminResult.Ok("重启倒计时已中止");
    }

    /// <summary>运行时开关心跳监控（等价官方 <c>hbctrl</c>）。</summary>
    public LocalAdminResult SetHeartbeatEnabled(bool enabled)
    {
        if (!_def.EnableHeartbeat)
            return LocalAdminResult.Fail("该实例启动时未启用心跳（缺少 -heartbeat 参数），需修改配置并重启进程。");

        lock (_sync)
        {
            if (enabled)
            {
                if (_heartbeat != HeartbeatState.Disabled)
                    return LocalAdminResult.Ok("静默崩溃检测已处于开启状态。");

                _heartbeat = HeartbeatState.AwaitingFirstHeartbeat;
                _warningStage = 0;
            }
            else
            {
                if (_heartbeat == HeartbeatState.Disabled)
                    return LocalAdminResult.Ok("静默崩溃检测已处于关闭状态。");

                _heartbeat = HeartbeatState.Disabled;
                _warningStage = 0;
            }
        }

        Info(enabled ? "静默崩溃检测已开启。" : "静默崩溃检测已关闭。");
        return LocalAdminResult.Ok(enabled ? "静默崩溃检测已开启" : "静默崩溃检测已关闭");
    }

    // ==================================================================
    //  进程控制
    // ==================================================================

    public LocalAdminResult Start()
    {
        lock (_sync)
        {
            if (_process is { HasExited: false })
                return LocalAdminResult.Fail("该服务器已在运行中。");
        }

        if (string.IsNullOrWhiteSpace(_def.ExecutablePath) || !File.Exists(_def.ExecutablePath))
            return LocalAdminResult.Fail($"找不到可执行文件：{_def.ExecutablePath}");

        try
        {
            EnsureListener();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return LocalAdminResult.Fail($"绑定控制台端口失败：{ex.Message}");
        }

        try
        {
            SpawnProcess();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Error($"启动游戏进程失败：{ex.Message}");
            _log.LogError(ex, "启动游戏进程失败（实例 {Id}）", _def.Id);
            return LocalAdminResult.Fail($"启动失败：{ex.Message}");
        }

        _lastError = null;
        return LocalAdminResult.Ok("启动指令已下发");
    }

    private void SpawnProcess()
    {
        int consolePort;
        lock (_sync)
            consolePort = _consolePort;

        var psi = new ProcessStartInfo
        {
            FileName = _def.ExecutablePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(_def.WorkingDirectory)
                ? Path.GetDirectoryName(_def.ExecutablePath) ?? Environment.CurrentDirectory
                : _def.WorkingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = _def.RedirectStandardStreams,
            RedirectStandardError = _def.RedirectStandardStreams,
        };

        if (_def.RedirectStandardStreams)
        {
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
        }

        // 官方参数顺序与分隔方式：
        //   -batchmode -nographics -txbuffer {n} -rxbuffer {n} -port{n} -console{n} -id{pid} [...]
        // 注意 -txbuffer / -rxbuffer 后**有空格**，而 -port / -console / -id 是紧贴式。
        var args = new List<string>
        {
            "-batchmode",
            "-nographics",
            "-txbuffer", _def.SlToLaBufferSize.ToString(),
            "-rxbuffer", _def.LaToSlBufferSize.ToString(),
            $"-port{_def.GamePort}",
            $"-console{consolePort}",
            $"-id{Environment.ProcessId}",
        };

        if (_def.DisableAnsiColors)
            args.Add("-disableAnsiColors");

        if (_def.EnableHeartbeat)
            args.Add("-heartbeat");

        foreach (string extra in SplitArguments(_def.ExtraArguments))
            args.Add(extra);

        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        Info($"启动进程：{psi.FileName}");
        Info($"参数：{string.Join(' ', args)}");

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var exitTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exitTcs.TrySetResult(true);

        if (_def.RedirectStandardStreams)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Append(ConsoleLineKind.Stdout, 7, $"[STDOUT] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Append(ConsoleLineKind.Stderr, 5, $"[STDERR] {e.Data}");
            };
        }

        if (!process.Start())
            throw new InvalidOperationException("Process.Start 返回 false");

        if (_def.RedirectStandardStreams)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        lock (_sync)
        {
            _process = process;
            _exitTcs = exitTcs;
            _startedAt = DateTime.Now;
            _exitAction = ExitAction.Crash;
            _warningStage = 0;
            _firstHeartbeatWarned = false;
            _heartbeat = _def.EnableHeartbeat ? HeartbeatState.AwaitingFirstHeartbeat : HeartbeatState.Disabled;
            _lastHeartbeatAt = DateTime.Now;

            // 新一轮运行，恢复默认的退出语义
            _ignoreExitSignals = false;
            _pending = PendingAction.None;
        }

        Info($"游戏进程已启动，PID {process.Id}，游戏端口 {_def.GamePort}。");
        _log.LogInformation("游戏进程已启动 PID={Pid}（实例 {Id}）", process.Id, _def.Id);

        _ = Task.Run(() => OnProcessExitedAsync(process, exitTcs));
    }

    private async Task OnProcessExitedAsync(Process process, TaskCompletionSource<bool> exitTcs)
    {
        // 等待事件真正落地，确保读流线程已把最后的输出吐出来
        try
        {
            await process.WaitForExitAsync();
            await Task.Delay(120);
        }
        catch (Exception)
        {
            // 忽略
        }

        int exitCode = -1;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (Exception)
        {
            // 忽略
        }

        ExitAction declared;
        PendingAction pending;
        bool ignored;

        lock (_sync)
        {
            declared = _exitAction;
            pending = _pending;
            ignored = _ignoreExitSignals;

            _pending = PendingAction.None;
            _ignoreExitSignals = false;
            _process = null;
            _exitTcs = null;
            _startedAt = null;
            _warningStage = 0;
            _heartbeat = HeartbeatState.Disabled;
            _consoleConnected = false;
        }

        exitTcs.TrySetResult(true);
        process.Dispose();

        Info($"游戏进程已退出（退出码 {exitCode}）。");
        _log.LogWarning("游戏进程已退出 exitCode={Code}（实例 {Id}）", exitCode, _def.Id);

        // ---- 退出动作协商：决定是否拉起新进程 ----
        bool restart;
        bool counted;

        if (pending == PendingAction.RestartCounted)
        {
            restart = true;
            counted = true;
        }
        else if (pending == PendingAction.RestartUncounted)
        {
            restart = true;
            counted = false;
        }
        else if (ignored)
        {
            restart = false;
            counted = false;
            Info("本次退出由面板主动触发，不会自动重启。");
        }
        else
        {
            switch (declared)
            {
                case ExitAction.Crash:
                    restart = _def.RestartOnCrash;
                    counted = true;
                    if (restart)
                        Error("检测到游戏进程异常终止，将自动重启。");
                    else
                        Warn("检测到游戏进程异常终止，已按配置（RestartOnCrash=false）停止。");
                    break;

                case ExitAction.Restart:
                    restart = true;
                    counted = false;
                    Info("游戏请求重启，正在拉起新进程。");
                    break;

                default:
                    restart = false;
                    counted = false;
                    Info(declared == ExitAction.SilentShutdown
                        ? "游戏已静默关服，不再自动重启。"
                        : "游戏已正常关服，不再自动重启。");
                    break;
            }
        }

        if (!restart)
            return;

        if (counted && !TryConsumeRestartBudget(out int usedInWindow))
        {
            _restartBudgetExhausted = true;
            Error($"重启次数已达上限（{_restartCount}/{_def.RestartLimit}，窗口 {_def.RestartTimeWindowSeconds} 秒内），已停止自动重启。请检查服务器状态后手动启动。");
            _log.LogError("重启限流触发，实例 {Id} 停止自动重启（窗口内 {Used} 次）", _def.Id, usedInWindow);
            return;
        }

        _restartBudgetExhausted = false;
        Info($"{RestartDelay.TotalSeconds:F0} 秒后拉起新进程…");
        await Task.Delay(RestartDelay);

        if (_disposed)
            return;

        try
        {
            SpawnProcess();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Error($"自动重启失败：{ex.Message}");
            _log.LogError(ex, "自动重启失败（实例 {Id}）", _def.Id);
        }
    }

    private bool TryConsumeRestartBudget(out int usedInWindow)
    {
        lock (_sync)
        {
            DateTime now = DateTime.Now;
            if (_restartWindowStart == default
                || (now - _restartWindowStart).TotalSeconds > _def.RestartTimeWindowSeconds)
            {
                _restartWindowStart = now;
                _restartCount = 0;
            }

            if (_def.RestartLimit <= 0 || _restartCount >= _def.RestartLimit)
            {
                usedInWindow = _restartCount;
                return false;
            }

            _restartCount++;
            usedInWindow = _restartCount;
            return true;
        }
    }

    /// <summary>让游戏进程退出：优雅（下发 exit 并等待）或强制结束进程树。</summary>
    private void TriggerProcessExit(bool force)
    {
        Process? process;
        lock (_sync)
            process = _process;

        if (process is null)
            return;

        if (force)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "强制结束进程失败（实例 {Id}）", _def.Id);
            }
            return;
        }

        // 优雅：下发 exit，超时后强制结束
        _ = Task.Run(async () =>
        {
            try
            {
                await SendConsoleCoreAsync("exit", CancellationToken.None);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_def.GracefulStopTimeoutSeconds));
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        Warn($"等待 {_def.GracefulStopTimeoutSeconds} 秒后进程仍未退出，强制结束。");
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "优雅停止失败（实例 {Id}）", _def.Id);
            }
        });
    }

    public LocalAdminResult Stop(bool force)
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            if (process is null || process.HasExited)
                return LocalAdminResult.Fail("该服务器当前未在运行。");

            // 用户要求停止：忽略游戏后续发出的退出信号，一律不重启
            _ignoreExitSignals = true;
            _pending = PendingAction.None;
        }

        Info(force ? "正在强制结束游戏进程…" : "正在请求游戏优雅退出…");
        TriggerProcessExit(force);
        return LocalAdminResult.Ok(force ? "已下发强制结束" : "已下发优雅停止");
    }

    public LocalAdminResult Restart(bool force)
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            if (process is null || process.HasExited)
                return LocalAdminResult.Fail("该服务器当前未在运行；如需启动请直接点击启动。");

            _ignoreExitSignals = true;
            _pending = PendingAction.RestartUncounted;
        }

        Info(force ? "正在强制重启（结束进程后重新拉起）…" : "正在请求游戏重启（下发 exit 后重新拉起）…");
        TriggerProcessExit(force);
        return LocalAdminResult.Ok("重启指令已下发");
    }

    // ==================================================================
    //  控制台命令
    // ==================================================================

    /// <summary>
    /// 处理用户在面板控制台输入的一行。
    /// 优先匹配 LocalAdmin 内置命令（与官方等价），未命中则原样透传给游戏控制台。
    /// </summary>
    public async Task<LocalAdminResult> SubmitInputAsync(string input, CancellationToken ct)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return LocalAdminResult.Fail("命令不能为空。");

        Append(ConsoleLineKind.Input, 7, $"> {trimmed}");

        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string head = parts[0].ToLowerInvariant();

        switch (head)
        {
            case "hbctrl":
                return HandleHbCtrl(parts);

            case "hbc":
                return CancelRestartCountdown();

            case "restart":
                return Restart(false);

            case "forcerestart":
                return Restart(true);

            case "clear":
                ClearBuffer();
                Info("控制台缓冲已清空。");
                return LocalAdminResult.Ok("控制台缓冲已清空");

            case "help":
                Info("内置命令：hbctrl <enable|disable|status> · hbc · restart · forcerestart · clear · help");
                Info("其余输入将原样转发给游戏服务端控制台执行。");
                return LocalAdminResult.Ok("已输出帮助");

            case "exit":
            case "quit":
            case "stop":
                // 官方行为：用户主动关服时先关闭退出信号，避免被误判为崩溃
                lock (_sync)
                {
                    _ignoreExitSignals = true;
                    _pending = PendingAction.None;
                    _exitAction = ExitAction.SilentShutdown;
                }
                break;
        }

        return await SendConsoleCoreAsync(trimmed, ct);
    }

    private LocalAdminResult HandleHbCtrl(string[] parts)
    {
        if (!_def.EnableHeartbeat)
            return LocalAdminResult.Fail("该实例启动时未启用心跳，请在配置中开启 enableHeartbeat 后重启进程。");

        if (parts.Length != 2)
            return LocalAdminResult.Fail("用法：hbctrl <enable|disable|status>");

        switch (parts[1].ToLowerInvariant())
        {
            case "enable":
            case "en":
            case "e":
            case "1":
                return SetHeartbeatEnabled(true);

            case "disable":
            case "dis":
            case "d":
            case "0":
                return SetHeartbeatEnabled(false);

            case "status":
            case "st":
            case "s":
            {
                var status = GetStatus();
                Info($"心跳状态：{status.HeartbeatStatus}｜倒计时阶段：{status.HeartbeatRestartStage}"
                     + $"｜最近一次心跳：{(status.LastHeartbeatAt is null ? "无" : $"{(DateTime.Now - status.LastHeartbeatAt.Value).TotalSeconds:F0} 秒前")}");
                return LocalAdminResult.Ok("已输出心跳状态");
            }

            default:
                return LocalAdminResult.Fail("未知子命令，用法：hbctrl <enable|disable|status>");
        }
    }

    /// <summary>把一行文本作为控制台命令写入游戏（下行帧：4 字节长度 + UTF-8）。</summary>
    private async Task<LocalAdminResult> SendConsoleCoreAsync(string text, CancellationToken ct)
    {
        NetworkStream? stream;
        lock (_sync)
            stream = _stream;

        if (stream is null || !_consoleConnected)
            return LocalAdminResult.Fail("游戏控制台未连接（服务器可能未运行或尚未完成启动）。");

        byte[]? frame = ConsoleProtocol.TryEncodeCommand(text, _def.LaToSlBufferSize, out string? encodeError);
        if (frame is null)
            return LocalAdminResult.Fail(encodeError ?? "命令编码失败。");

        await _writeLock.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(frame, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "下发控制台命令失败（实例 {Id}）", _def.Id);
            return LocalAdminResult.Fail($"下发失败：{ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }

        _log.LogInformation("面板下发控制台命令（实例 {Id}）：{Command}", _def.Id, text);
        return LocalAdminResult.Ok("命令已下发");
    }

    /// <summary>面板直接下发命令（会标注调用来源）。</summary>
    public Task<LocalAdminResult> SendConsoleAsync(string command, CancellationToken ct) =>
        SubmitInputAsync(command, ct);

    // ==================================================================
    //  参数解析
    // ==================================================================

    /// <summary>
    /// 按命令行规则拆分额外参数，支持双引号包裹含空格的值。
    /// </summary>
    internal static List<string> SplitArguments(string raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (char c in raw)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    // ==================================================================
    //  释放
    // ==================================================================

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _lifetimeCts.Cancel();

        lock (_sync)
        {
            _ignoreExitSignals = true;
            _pending = PendingAction.None;
        }

        Process? process;
        lock (_sync)
            process = _process;

        if (process is { HasExited: false })
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // 忽略
            }
        }

        try
        {
            _client?.Dispose();
            _listener?.Stop();
        }
        catch (Exception)
        {
            // 忽略
        }

        lock (_sync)
        {
            _logWriter?.Dispose();
            _logWriter = null;
        }

        _writeLock.Dispose();
        _lifetimeCts.Dispose();

        await Task.CompletedTask;
    }
}
