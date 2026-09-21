using System.Buffers.Binary;
using System.Text;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// 游戏 → LocalAdmin 方向的控制码。
/// 0x00–0x0F 被 <see cref="System.ConsoleColor"/> 的 16 个值占用（作为输出行的颜色码），
/// 因此控制码从 0x10 开始。与官方 <c>LocalAdmin.V2.Core.TcpServer.OutputCodes</c> 一一对应。
/// </summary>
public enum ConsoleControlCode : byte
{
    /// <summary>回合重启（LoadAdmin 借此重置日志）</summary>
    RoundRestart = 0x10,

    /// <summary>进入空闲态</summary>
    IdleEnter = 0x11,

    /// <summary>退出空闲态</summary>
    IdleExit = 0x12,

    /// <summary>退出动作重置 → 视为崩溃</summary>
    ExitActionReset = 0x13,

    /// <summary>正常关服</summary>
    ExitActionShutdown = 0x14,

    /// <summary>静默关服（不等按键）</summary>
    ExitActionSilentShutdown = 0x15,

    /// <summary>请求重启</summary>
    ExitActionRestart = 0x16,

    /// <summary>心跳（用于静默崩溃检测）</summary>
    Heartbeat = 0x17,
}

/// <summary>控制台一行输出的来源分类。</summary>
public enum ConsoleLineKind
{
    /// <summary>控制台协议通道的常规输出</summary>
    Output = 0,

    /// <summary>本端下发的命令回显</summary>
    Input = 1,

    /// <summary>面板自身的提示/事件</summary>
    System = 2,

    /// <summary>游戏进程标准输出</summary>
    Stdout = 3,

    /// <summary>游戏进程标准错误</summary>
    Stderr = 4,

    /// <summary>控制协议消息（回合重启、心跳、退出动作等）</summary>
    Control = 5,
}

/// <summary>控制台缓冲中的一行。</summary>
public sealed record ConsoleLine(
    long Seq,
    DateTime Time,
    ConsoleLineKind Kind,
    byte Color,
    string Text,
    string ColorHex);

/// <summary>
/// 官方控制台协议的编解码。
///
/// 方向不对称：
/// <list type="bullet">
/// <item>上行（游戏 → LocalAdmin）：1 字节 code；若 code &lt; 0x10 则再跟 4 字节小端长度 + UTF-8 文本，否则仅有该 1 字节。</item>
/// <item>下行（LocalAdmin → 游戏）：4 字节小端长度 + UTF-8 文本，无类型码。</item>
/// </list>
/// </summary>
public static class ConsoleProtocol
{
    /// <summary>长度前缀字节数。</summary>
    public const int LengthPrefixSize = 4;

    /// <summary>颜色码上界：小于该值的 code 表示一条带颜色的输出行。</summary>
    public const byte ColorCodeLimit = 0x10;

    /// <summary>
    /// 单帧最大字节数。官方实现未做上限校验（直接 ArrayPool.Rent(length)），
    /// 这里补上防御，避免损坏或恶意长度导致大内存分配。
    /// </summary>
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    /// <summary>发送侧编码：与官方一致，遇到非法字节直接抛异常。</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// 接收侧编码：宽松模式（非法字节替换为 U+FFFD）。
    /// 与官方不同 —— 官方用严格模式，一个坏字节会整条连接抛异常。
    /// 面板场景下优先保证链路不中断，坏字节只污染单行并记一条警告。
    /// </summary>
    private static readonly UTF8Encoding LenientUtf8 = new(false, false);

    /// <summary>
    /// 编码一条下行命令帧。<paramref name="txBuffer"/> 为机器人 → 游戏方向的缓冲区字节数；
    /// 超出上限时返回 null（官方行为：拒绝发送并报错）。
    /// </summary>
    public static byte[]? TryEncodeCommand(string text, int txBuffer, out string? error)
    {
        error = null;

        byte[] body;
        try
        {
            body = StrictUtf8.GetBytes(text);
        }
        catch (EncoderFallbackException ex)
        {
            error = $"命令包含无法以 UTF-8 编码的字符：{ex.Message}";
            return null;
        }

        if (body.Length + LengthPrefixSize > txBuffer)
        {
            error = $"命令过长（{body.Length} 字节），超过 LA→SL 缓冲区上限 {txBuffer} 字节，已拒绝发送。";
            return null;
        }

        var frame = new byte[body.Length + LengthPrefixSize];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, LengthPrefixSize);
        return frame;
    }

    /// <summary>解码一帧上行输出行的负载。</summary>
    public static string DecodeOutput(byte[] payload, int length) =>
        LenientUtf8.GetString(payload, 0, length);

    /// <summary>把控制码翻译成中文描述，用于控制台提示。</summary>
    public static string DescribeControl(ConsoleControlCode code) => code switch
    {
        ConsoleControlCode.RoundRestart => "回合重启 (RoundRestart)",
        ConsoleControlCode.IdleEnter => "进入空闲态 (IdleEnter)",
        ConsoleControlCode.IdleExit => "退出空闲态 (IdleExit)",
        ConsoleControlCode.ExitActionReset => "退出动作重置 / 崩溃 (ExitActionReset)",
        ConsoleControlCode.ExitActionShutdown => "正常关服 (ExitActionShutdown)",
        ConsoleControlCode.ExitActionSilentShutdown => "静默关服 (ExitActionSilentShutdown)",
        ConsoleControlCode.ExitActionRestart => "请求重启 (ExitActionRestart)",
        ConsoleControlCode.Heartbeat => "心跳 (Heartbeat)",
        _ => $"未知控制码 0x{(byte)code:X2}",
    };

    /// <summary>
    /// 把 <see cref="System.ConsoleColor"/>（0–15）映射为在深色终端上可读的十六进制颜色。
    ///
    /// 说明：与官方 TUI 的原始色值不完全相同 —— 官方直接写宿主控制台，
    /// 这里是 Web 终端，纯黑的 Black(0) 与纯蓝的 DarkBlue(1) 在深色底上几乎不可见，
    /// 因此对最暗的几个色值做了提亮处理，其余保持语义一致。
    /// </summary>
    public static string ColorToHex(byte color) => (color & 0x0F) switch
    {
        0 => "#7f8c8d",  // Black        → 提亮为中性灰（保证可读）
        1 => "#3498db",  // DarkBlue     → 提亮
        2 => "#27ae60",  // DarkGreen
        3 => "#17a2b8",  // DarkCyan
        4 => "#c0392b",  // DarkRed
        5 => "#9b59b6",  // DarkMagenta
        6 => "#b7950b",  // DarkYellow
        7 => "#bdc3c7",  // Gray
        8 => "#95a5a6",  // DarkGray
        9 => "#4aa3ff",  // Blue
        10 => "#2ecc71", // Green
        11 => "#22d3ee", // Cyan
        12 => "#e74c3c", // Red
        13 => "#e879f9", // Magenta
        14 => "#f1c40f", // Yellow
        _ => "#e5e7eb",  // White (15)
    };

    /// <summary>把颜色码翻译成中文名，便于前端做图例。</summary>
    public static string ColorName(byte color) => (color & 0x0F) switch
    {
        0 => "黑", 1 => "深蓝", 2 => "深绿", 3 => "深青",
        4 => "深红", 5 => "深品红", 6 => "深黄", 7 => "灰",
        8 => "深灰", 9 => "蓝", 10 => "绿", 11 => "青",
        12 => "红", 13 => "品红", 14 => "黄", _ => "白",
    };
}
