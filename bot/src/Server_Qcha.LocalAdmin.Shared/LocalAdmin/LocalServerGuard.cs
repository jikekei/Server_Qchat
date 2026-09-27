using System.Text.RegularExpressions;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// 限制面板能拉起的程序，以及实例 Id 能落到日志目录的形态。
/// server.control 会以本进程的身份启动程序，应只交给可信管理员。
/// </summary>
public static class LocalServerGuard
{
    public const string AllowedWindowsExecutable = "SCPSL.exe";
    public const string AllowedLinuxExecutable = "SCPSL.x86_64";

    private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsSafeId(string? id) => id != null && IdPattern.IsMatch(id);

    public static string? ValidateId(string? id)
    {
        if (IsSafeId(id))
            return null;
        return "标识（Id）只能包含英文字母、数字、下划线和连字符，长度 1 到 64。";
    }

    public static bool IsAllowedExecutableFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        return fileName.Equals(AllowedWindowsExecutable, StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(AllowedLinuxExecutable, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ValidateExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "可执行文件路径不能为空";

        string trimmed = path.Trim();
        if (trimmed.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            return "可执行文件路径含有非法字符";

        string fileName;
        try
        {
            // 配置里可能写着另一套操作系统的路径分隔符，取文件名前先统一。
            string normalized = trimmed.Replace('\\', '/');
            fileName = Path.GetFileName(normalized);
        }
        catch (Exception)
        {
            return "可执行文件路径不合法";
        }

        if (!IsAllowedExecutableFileName(fileName))
        {
            return "只允许启动 SCPSL 服务端，文件名必须是 SCPSL.exe 或 SCPSL.x86_64。"
                + "该权限会以当前进程身份拉起程序，请只授予可信管理员。";
        }

        return null;
    }
}
