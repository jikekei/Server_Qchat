namespace Server.Qcat.LocalAdmin;

internal static class LocalAdminPaths
{
    public static string ApplicationDirectory
    {
        get
        {
            string? executable = Environment.ProcessPath;
            return Path.GetFileName(executable) is "Server_Qcha.Bot.exe" or "Server_Qcha.Daemon.exe"
                or "Server_Qcha.Bot" or "Server_Qcha.Daemon"
                ? Path.GetDirectoryName(executable)!
                : AppContext.BaseDirectory;
        }
    }
}
