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
            "【安全警告】检测到仍在使用默认密钥（或密钥为空）：",
        };
        foreach (string problem in problems)
            lines.Add("  · " + problem);
        lines.Add("使用默认密钥存在安全风险：默认值已写在公开仓库和文档中，任何能连到相关端口的人");
        lines.Add("都可以伪造鉴权，向游戏服下发封禁、广播、重启回合等管理指令，或操作守护进程托管的服务器。");
        lines.Add("请管理员尽快修改：自行生成一段随机字符串（建议至少 24 位），按下面的对应关系写成同一个值后重启：");
        lines.Add("  · 机器人 SocketServer:AuthToken 与游戏插件 auth_token 必须相同");
        lines.Add("  · 机器人与守护进程的 LocalAdmin:DaemonToken 必须相同");
        lines.Add("跨机器通信请走 VPN，或用防火墙限制来源。这条 TCP 链路只做 HMAC 鉴权，不加密内容。");
        lines.Add("程序将继续启动，但在修改之前上述风险一直存在。");
        lines.Add("===================================================================");
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
