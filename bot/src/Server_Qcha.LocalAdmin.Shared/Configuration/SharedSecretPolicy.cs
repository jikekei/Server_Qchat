using System.Security.Cryptography;

namespace Server.Qcat.Configuration;

/// <summary>
/// 机器人与守护进程共用的口令策略。
/// 检测到仍在使用公开的默认密钥（或密钥为空）时只输出安全警告，不阻止启动；
/// 比较口令时走恒定时间，避免逐字符提前返回。
/// </summary>
public static class SharedSecretPolicy
{
    /// <summary>曾经写进公开仓库和文档的默认 Token。必须与插件侧的同名常量保持一致。</summary>
    public const string LegacyDefaultToken = "QchaSecret_123";

    public static bool IsEmpty(string? token) => string.IsNullOrWhiteSpace(token);

    public static bool IsLegacyDefault(string? token) =>
        token != null && string.Equals(token.Trim(), LegacyDefaultToken, StringComparison.Ordinal);

    /// <summary>为空或仍是公开的默认值。只用于决定是否输出警告。</summary>
    public static bool IsWeak(string? token) => IsEmpty(token) || IsLegacyDefault(token);

    public static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] a = System.Text.Encoding.UTF8.GetBytes(left ?? "");
        byte[] b = System.Text.Encoding.UTF8.GetBytes(right ?? "");
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>列出仍在使用默认密钥或空密钥的字段，每项一行。没有问题时返回空列表。</summary>
    public static List<string> DescribeWeakSecrets(bool checkAuthToken, string? authToken, bool checkDaemonToken, string? daemonToken)
    {
        var problems = new List<string>();
        if (checkAuthToken)
        {
            if (IsLegacyDefault(authToken))
                problems.Add("SocketServer:AuthToken 仍在使用公开的默认密钥 " + LegacyDefaultToken + "。");
            else if (IsEmpty(authToken))
                problems.Add("SocketServer:AuthToken 为空，任何人都能按公开协议算出合法签名，鉴权形同虚设。");
        }

        if (checkDaemonToken)
        {
            if (IsLegacyDefault(daemonToken))
                problems.Add("LocalAdmin:DaemonToken 仍在使用公开的默认密钥 " + LegacyDefaultToken + "。");
            else if (IsEmpty(daemonToken))
                problems.Add("LocalAdmin:DaemonToken 为空，守护进程将不校验 Token，本机任何程序都能调用其管理接口。");
        }

        return problems;
    }

    /// <summary>组装启动时输出的安全警告全文（多行）。</summary>
    public static List<string> BuildStartupWarning(IReadOnlyList<string> problems)
    {
        var lines = new List<string>
        {
            "===================================================================",
            "【安全警告】检测到默认密钥或空密钥。",
            "",
            "当前默认密钥 " + LegacyDefaultToken + " 已公开，继续使用可能导致未授权鉴权和管理操作。",
            "",
            "请尽快修改为至少 24 位随机字符串，并确保：",
            "· SocketServer:AuthToken = 游戏插件 auth_token",
            "· LocalAdmin:DaemonToken = 守护进程对应 Token",
            "",
            "跨机器部署建议配合 VPN 或防火墙限制来源。",
            "程序将继续启动，但修改前仍存在安全风险。",
            "==================================================================="
        };
        return lines;
    }

    /// <summary>在控制台醒目地输出安全警告，然后返回，由调用方继续启动。</summary>
    public static void WriteStartupWarning(IReadOnlyList<string> problems)
    {
        if (problems.Count == 0)
            return;

        Console.ForegroundColor = ConsoleColor.Yellow;
        foreach (string line in BuildStartupWarning(problems))
            Console.WriteLine(line);
        Console.ResetColor();
        Console.WriteLine();
    }
}
