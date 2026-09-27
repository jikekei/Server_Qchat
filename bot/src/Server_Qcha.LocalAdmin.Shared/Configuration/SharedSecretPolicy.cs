using System.Security.Cryptography;

namespace Server.Qcat.Configuration;

/// <summary>
/// 机器人与守护进程共用的口令策略。
/// 已公开的旧默认值不再接受；比较口令时走恒定时间，避免逐字符提前返回。
/// </summary>
public static class SharedSecretPolicy
{
    /// <summary>曾经写进公开仓库和文档的默认 Token。必须与插件侧的同名常量保持一致。</summary>
    public const string RetiredDefaultToken = "QchaSecret_123";

    public static bool IsRejected(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return true;
        return string.Equals(token.Trim(), RetiredDefaultToken, StringComparison.Ordinal);
    }

    public static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] a = System.Text.Encoding.UTF8.GetBytes(left ?? "");
        byte[] b = System.Text.Encoding.UTF8.GetBytes(right ?? "");
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static List<string> DescribeStartupRejection(bool checkAuthToken, string? authToken, bool checkDaemonToken, string? daemonToken)
    {
        var problems = new List<string>();
        if (checkAuthToken && IsRejected(authToken))
        {
            problems.Add("【安全】SocketServer:AuthToken 为空，或仍是已公开的旧默认值，已拒绝启动。");
        }

        if (checkDaemonToken && IsRejected(daemonToken))
        {
            problems.Add("【安全】LocalAdmin:DaemonToken 为空，或仍是已公开的旧默认值，已拒绝启动。");
        }

        return problems;
    }

    public static void WriteStartupRejection(IReadOnlyList<string> problems)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("===================================================================");
        foreach (string line in problems)
            Console.WriteLine(line);
        Console.WriteLine("请自行生成一段随机字符串（建议至少 24 位），并在以下位置写成同一个值：");
        Console.WriteLine("  · 机器人 SocketServer:AuthToken");
        Console.WriteLine("  · 机器人与守护进程的 LocalAdmin:DaemonToken（两边必须相同）");
        Console.WriteLine("  · 游戏插件 auth_token（与 SocketServer:AuthToken 相同）");
        Console.WriteLine("不要继续使用已公开的旧默认值 " + RetiredDefaultToken + "。");
        Console.WriteLine("跨机器通信请走 VPN，或用防火墙限制来源。这条 TCP 链路只做 HMAC 鉴权，不加密内容。");
        Console.WriteLine("===================================================================");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("按任意键退出...");
        if (!Console.IsInputRedirected)
        {
            try { Console.ReadKey(); } catch { }
        }
    }
}
