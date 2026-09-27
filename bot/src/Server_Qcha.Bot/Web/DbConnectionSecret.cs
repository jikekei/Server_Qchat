namespace Server.Qcat.Web;

/// <summary>
/// 面板回显的数据库连接串会把密码打成 ******。
/// 只有主机和端口都没变时才把已保存的密码填回去，避免改掉地址后把密码发到别处。
/// </summary>
public static class DbConnectionSecret
{
    public const string Mask = "******";

    public static bool TryResolve(string? input, string? current, out string resolved, out string? error)
    {
        resolved = "";
        error = null;
        if (string.IsNullOrWhiteSpace(input))
            return true;

        input = input.Trim();
        if (!ContainsMaskedPassword(input))
        {
            resolved = input;
            return true;
        }

        if (!TryReadPassword(current, out string actualPwd))
        {
            error = "没有可复用的已保存密码，请重新输入完整连接串。";
            return false;
        }

        if (!SameEndpoint(input, current))
        {
            error = "数据库主机或端口已变更，不能沿用已保存的密码，请重新输入完整连接串。";
            return false;
        }

        resolved = ReplaceMaskedPassword(input, actualPwd);
        return true;
    }

    private static bool ContainsMaskedPassword(string connectionString)
    {
        foreach (KeyValuePair<string, string> pair in Parse(connectionString))
        {
            if (IsPasswordKey(pair.Key) && pair.Value == Mask)
                return true;
        }

        return false;
    }

    private static bool TryReadPassword(string? connectionString, out string password)
    {
        password = "";
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;
        foreach (KeyValuePair<string, string> pair in Parse(connectionString))
        {
            if (IsPasswordKey(pair.Key) && pair.Value.Length > 0 && pair.Value != Mask)
            {
                password = pair.Value;
                return true;
            }
        }

        return false;
    }

    private static bool SameEndpoint(string? left, string? right)
    {
        Endpoint a = ReadEndpoint(left);
        Endpoint b = ReadEndpoint(right);
        if (a.Host.Length == 0 || b.Host.Length == 0)
            return false;
        return string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;
    }

    private static string ReplaceMaskedPassword(string input, string actualPwd)
    {
        string[] parts = input.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            int eq = parts[i].IndexOf('=');
            if (eq <= 0)
                continue;
            string key = parts[i].Substring(0, eq).Trim();
            string value = parts[i].Substring(eq + 1).Trim();
            if (IsPasswordKey(key) && value == Mask)
                parts[i] = parts[i].Substring(0, eq + 1) + actualPwd;
        }

        return string.Join(";", parts);
    }

    private static Endpoint ReadEndpoint(string? connectionString)
    {
        var map = Parse(connectionString);
        string host = First(map, "Server", "Host", "Data Source", "Address", "Network Address");
        int port = 3306;
        if (map.TryGetValue("Port", out string? portText) && int.TryParse(portText, out int parsed) && parsed > 0)
            port = parsed;
        return new Endpoint(host, port);
    }

    private static string First(Dictionary<string, string> map, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (map.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static Dictionary<string, string> Parse(string? connectionString)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(connectionString))
            return map;
        foreach (string part in connectionString.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = part.Substring(0, eq).Trim();
            if (key.Length == 0 || map.ContainsKey(key))
                continue;
            map[key] = part.Substring(eq + 1).Trim();
        }

        return map;
    }

    private static bool IsPasswordKey(string key) =>
        key.Equals("Password", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Pwd", StringComparison.OrdinalIgnoreCase);

    private readonly record struct Endpoint(string Host, int Port);
}
