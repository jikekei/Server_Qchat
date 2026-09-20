using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// 寻找本机上的 SCPSL 专用服务端可执行文件 —— 让用户在面板「添加服务器」时不必手抄路径。
///
/// 只做**只读探测**：不加载任何程序集、不修改任何文件。
/// 探测顺序（越靠前越可信）：
/// 1. 配置里显式指定的 <c>LocalAdmin:DefaultExecutablePath</c>；
/// 2. **已经在运行的 SCPSL 进程**（最可信 —— 就是本机在用的那一份）；
/// 3. Steam 各库目录下的 <c>steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL.exe</c>。
///
/// Steam 库的发现方式刻意**不读注册表**（本项目 NuGet 源不可达，避免引入
/// <c>Microsoft.Win32.Registry</c> 包）：改为从常见安装位置出发，
/// 再解析每个根目录下的 <c>steamapps\libraryfolders.vdf</c> 补全其它库。
/// </summary>
public static class ScpslLocator
{
    /// <summary>游戏在 Steam 库中的相对路径。</summary>
    public const string RelativeGamePath = @"steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL.exe";

    private const string ProcessName = "SCPSL";

    private static readonly Regex VdfPathRegex =
        new(@"""path""\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>探测结果。Probed 仅用于向用户解释"扫了哪些地方"，便于排查。</summary>
    public sealed record SearchResult(
        IReadOnlyList<string> Candidates,
        IReadOnlyList<string> SteamRoots,
        IReadOnlyList<string> Probed);

    /// <summary>
    /// 执行一次探测。任何单点失败（盘符未就绪、权限不足）都只跳过，不抛出。
    /// </summary>
    /// <param name="preferred">配置里指定的默认路径，存在则排在最前。</param>
    public static SearchResult Search(string? preferred = null)
    {
        var candidates = new List<string>();
        var probed = new List<string>();

        void Consider(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            probed.Add(path);
            if (File.Exists(path) && !candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
                candidates.Add(path);
        }

        // 1) 配置指定的默认路径
        Consider(preferred);

        // 2) 正在运行的 SCPSL 进程 —— 最可信
        foreach (string running in RunningProcessPaths())
            Consider(running);

        // 3) Steam 各库
        var roots = SteamRoots();
        foreach (string root in roots)
            Consider(Path.Combine(root, RelativeGamePath));

        return new SearchResult(candidates, roots, probed);
    }

    /// <summary>取正在运行的 SCPSL 进程的可执行文件路径。</summary>
    private static IEnumerable<string> RunningProcessPaths()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(ProcessName);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var p in processes)
        {
            string? path = null;
            try
            {
                path = p.MainModule?.FileName;
            }
            catch (Exception)
            {
                // 权限不足或进程已退出 —— 忽略这一条
            }
            finally
            {
                p.Dispose();
            }

            if (!string.IsNullOrWhiteSpace(path))
                yield return path;
        }
    }

    /// <summary>
    /// 猜测本机所有 Steam 库根目录（含从 libraryfolders.vdf 解析出来的）。
    /// </summary>
    private static List<string> SteamRoots()
    {
        var roots = new List<string>();

        void AddRoot(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return;
            if (!roots.Contains(dir, StringComparer.OrdinalIgnoreCase))
                roots.Add(dir);
        }

        // 常见安装位置
        AddRoot(Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        AddRoot(Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        foreach (string drive in FixedDriveRoots())
        {
            AddRoot(Combine(drive, "Steam"));
            AddRoot(Combine(drive, "SteamLibrary"));
            AddRoot(Combine(drive, "Program Files (x86)", "Steam"));
            AddRoot(Combine(drive, "Games", "Steam"));
        }

        // 从已有根目录的 libraryfolders.vdf 里补全其它库
        foreach (string root in roots.ToList())
        {
            foreach (string extra in ParseLibraryFolders(root))
                AddRoot(extra);
        }

        return roots;
    }

    /// <summary>解析 <c>steamapps\libraryfolders.vdf</c> 里的 "path" 项。</summary>
    private static IEnumerable<string> ParseLibraryFolders(string steamRoot)
    {
        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        string text;

        try
        {
            if (!File.Exists(vdf))
                yield break;
            text = File.ReadAllText(vdf);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (Match m in VdfPathRegex.Matches(text))
        {
            // VDF 里路径是转义的（D:\\SteamLibrary），需要还原成单反斜杠
            string path = m.Groups[1].Value.Replace("\\\\", "\\").Trim();
            if (path.Length > 0)
                yield return path;
        }
    }

    /// <summary>就绪的固定磁盘根目录（形如 <c>C:\</c>）。未就绪的盘符直接跳过。</summary>
    private static IEnumerable<string> FixedDriveRoots()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var d in drives)
        {
            bool ok;
            try
            {
                ok = d.IsReady && d.DriveType == DriveType.Fixed;
            }
            catch (Exception)
            {
                ok = false;
            }

            if (ok)
                yield return d.RootDirectory.FullName;
        }
    }

    private static string? Combine(params string?[] parts)
    {
        if (parts.Any(string.IsNullOrWhiteSpace))
            return null;

        try
        {
            return Path.Combine(parts!);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
