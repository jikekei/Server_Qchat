namespace Server.Qcat.LocalAdmin;

/// <summary>
/// 「服务器进程」页控制台的**捕获级别** —— 决定哪些输出会进入面板的控制台缓冲。
///
/// 它控制的是**采集**（服务端过滤），不是前端显示：被过滤掉的行不会进入缓冲，
/// 因此也不会随轮询发送给浏览器。这样在长时间运行、输出很吵的服务器上，
/// 有限的缓冲容量能留给真正有用的行。
///
/// 判定依据是**行的类型 + 游戏给出的颜色码**（SCPSL 用红色表示错误、黄色表示警告）：
/// <list type="bullet">
///   <item><c>Stderr</c> → 错误</item>
///   <item><c>Stdout</c> → 调试（这类输出通常是控制台协议通道的重复，最吵）</item>
///   <item>其余按颜色：红(12/9) → 错误；黄(14/6) → 警告；其它 → 信息</item>
/// </list>
/// 面板自身的提示也用同一套颜色（见 <c>Info/Warn/Error</c>），所以规则是统一的、没有特例。
/// </summary>
public enum ConsoleCaptureLevel
{
    /// <summary>全部（含游戏 stdout 与调试信息）</summary>
    All = 0,

    /// <summary>常规：排除游戏 stdout</summary>
    Normal = 1,

    /// <summary>警告与错误（含面板的警告/错误提示）</summary>
    Warn = 2,

    /// <summary>仅错误</summary>
    Error = 3,

    /// <summary>关闭：不采集任何控制台输出</summary>
    Off = 4,
}

/// <summary><see cref="ConsoleCaptureLevel"/> 的解析、展示与判定。</summary>
public static class ConsoleCaptureLevels
{
    /// <summary>持久化用的键（写在服务器定义里，可读性优先，故用字符串而非枚举序号）。</summary>
    public static readonly string[] Keys = { "all", "normal", "warn", "error", "off" };

    /// <summary>默认级别：保持与本功能引入前一致的行为（什么都采集）。</summary>
    public const string DefaultKey = "all";

    /// <summary>把外部输入解析为级别。注意：**不要**把这个方法改名为 TryParse —— 见 LogLevelStore 的注释。</summary>
    public static bool TryParseKey(string? raw, out ConsoleCaptureLevel level)
    {
        level = ConsoleCaptureLevel.All;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string key = raw.Trim().ToLowerInvariant();
        for (int i = 0; i < Keys.Length; i++)
        {
            if (Keys[i] == key)
            {
                level = (ConsoleCaptureLevel)i;
                return true;
            }
        }

        return false;
    }

    /// <summary>规范化为合法的键；非法则回落到默认值。</summary>
    public static string NormalizeKey(string? raw) =>
        TryParseKey(raw, out var level) ? ToKey(level) : DefaultKey;

    public static string ToKey(ConsoleCaptureLevel level) =>
        level is >= ConsoleCaptureLevel.All and <= ConsoleCaptureLevel.Off ? Keys[(int)level] : DefaultKey;

    /// <summary>面板上显示的中文名。</summary>
    public static string Label(ConsoleCaptureLevel level) => level switch
    {
        ConsoleCaptureLevel.All => "全部（含 stdout）",
        ConsoleCaptureLevel.Normal => "常规（不含 stdout）",
        ConsoleCaptureLevel.Warn => "警告与错误",
        ConsoleCaptureLevel.Error => "仅错误",
        ConsoleCaptureLevel.Off => "关闭",
        _ => "全部（含 stdout）",
    };

    /// <summary>面板下拉用：键 + 中文名。</summary>
    public static object[] Describe() =>
        Keys.Select(k =>
        {
            TryParseKey(k, out var level);
            return (object)new { key = k, label = Label(level) };
        }).ToArray();

    /// <summary>行严重度：0=调试 1=信息 2=警告 3=错误。</summary>
    public static int Severity(ConsoleLineKind kind, byte color)
    {
        // 明确来源优先于颜色：stderr 一定是错误，stdout 一定是调试信息
        if (kind == ConsoleLineKind.Stderr)
            return 3;

        if (kind == ConsoleLineKind.Stdout)
            return 0;

        return color switch
        {
            12 or 9 => 3,   // 红 / 深红
            14 or 6 => 2,   // 黄 / 深黄
            _ => 1,         // 其余视为常规信息
        };
    }

    /// <summary>该行是否应当被采集（写入控制台缓冲）。</summary>
    public static bool ShouldCapture(ConsoleCaptureLevel level, ConsoleLineKind kind, byte color)
    {
        if (level == ConsoleCaptureLevel.Off)
            return false;

        int min = level switch
        {
            ConsoleCaptureLevel.All => 0,
            ConsoleCaptureLevel.Normal => 1,
            ConsoleCaptureLevel.Warn => 2,
            ConsoleCaptureLevel.Error => 3,
            _ => 0,
        };

        return Severity(kind, color) >= min;
    }
}
