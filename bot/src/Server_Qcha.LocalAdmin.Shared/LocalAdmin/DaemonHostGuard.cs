using System.Net;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// 守护进程的 Host 头校验，避免浏览器把回环地址重绑定到其它域名后仍能访问本机接口。
/// </summary>
public static class DaemonHostGuard
{
    public static string ConfiguredHost(string? listenUri)
    {
        if (string.IsNullOrWhiteSpace(listenUri))
            return "";
        if (!Uri.TryCreate(listenUri.Trim(), UriKind.Absolute, out Uri? uri))
            return "";
        return uri.Host;
    }

    public static bool ListenUriIsLoopback(string? listenUri)
    {
        string host = ConfiguredHost(listenUri);
        return IsLoopbackHost(host);
    }

    public static string ExtractHost(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
            return "";

        string value = hostHeader.Trim();
        if (value.StartsWith('['))
        {
            int end = value.IndexOf(']');
            if (end > 1)
                return value.Substring(1, end - 1);
            return "";
        }

        int colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
        {
            string port = value.Substring(colon + 1);
            if (int.TryParse(port, out _))
                return value.Substring(0, colon);
        }

        return value;
    }

    public static bool IsAllowed(IPAddress? localAddress, string? hostHeader, string? configuredHost)
    {
        string host = ExtractHost(hostHeader);
        if (host.Length == 0)
            return false;

        if (localAddress is null || IsLoopbackAddress(localAddress))
            return IsLoopbackHost(host);

        if (host.Equals(NormalizeAddressText(localAddress), StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(configuredHost)
            && host.Equals(configuredHost.Trim(), StringComparison.OrdinalIgnoreCase)
            && !IsWildcardHost(configuredHost))
        {
            return true;
        }

        return false;
    }

    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;
        string value = host.Trim();
        return value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopbackAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return IPAddress.IsLoopback(address);
    }

    private static string NormalizeAddressText(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return address.ToString();
    }

    private static bool IsWildcardHost(string host)
    {
        string value = host.Trim();
        return value is "0.0.0.0" or "::" or "*" or "+";
    }
}
