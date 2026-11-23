using MySqlConnector;

namespace Server.Qcat.Web;

public static class DbConnectionSecret
{
    public const string Mask = "******";

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !TryParse(value, out var connection))
            return "";

        if (connection.Password.Length > 0)
            connection.Password = Mask;
        if (connection.CertificatePassword?.Length > 0)
            connection.CertificatePassword = Mask;
        return connection.ConnectionString;
    }

    public static bool TryResolve(string? input, string? current, out string resolved, out string? error)
    {
        resolved = "";
        error = null;
        if (string.IsNullOrWhiteSpace(input))
            return true;
        if (!TryParse(input, out var requested))
        {
            error = "MySQL 连接字符串无效。";
            return false;
        }

        bool reusePassword = requested.Password == Mask;
        bool reuseCertificatePassword = requested.CertificatePassword == Mask;
        if (reusePassword || reuseCertificatePassword)
        {
            if (!TryParse(current, out var saved) ||
                (reusePassword && !CanReuse(saved.Password)) ||
                (reuseCertificatePassword && !CanReuse(saved.CertificatePassword)))
            {
                error = "没有可复用的已保存密码，请重新输入完整连接串。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(saved.Server) ||
                !string.Equals(saved.Server, requested.Server,
                    saved.Server.Contains('/') ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) ||
                saved.Port != requested.Port ||
                saved.ConnectionProtocol != requested.ConnectionProtocol ||
                ((saved.ConnectionProtocol == MySqlConnectionProtocol.NamedPipe) &&
                    !string.Equals(saved.PipeName, requested.PipeName, StringComparison.OrdinalIgnoreCase)) ||
                (reuseCertificatePassword &&
                    !string.Equals(saved.CertificateFile, requested.CertificateFile, StringComparison.Ordinal)))
            {
                error = "数据库主机、端口、连接方式或证书已变更，请重新输入完整连接串。";
                return false;
            }

            if (reusePassword)
                requested.Password = saved.Password;
            if (reuseCertificatePassword)
                requested.CertificatePassword = saved.CertificatePassword;
        }

        resolved = requested.ConnectionString;
        return true;
    }

    private static bool CanReuse(string? password) => !string.IsNullOrEmpty(password) && password != Mask;

    private static bool TryParse(string? value, out MySqlConnectionStringBuilder connection)
    {
        try
        {
            connection = new MySqlConnectionStringBuilder(value ?? "");
            return true;
        }
        catch (ArgumentException)
        {
            connection = null!;
            return false;
        }
    }
}
