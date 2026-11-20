using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Server.Qcat.Services;

internal static class MySqlDumpInstaller
{
    private const string Version = "8.4.11";
    private static readonly HttpClient DownloadClient = new();

    public static async Task<string> ResolveAsync(string configured, string contentRoot, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configured) && configured != "mysqldump")
            return configured;

        bool windows = OperatingSystem.IsWindows();
        string executableName = windows ? "mysqldump.exe" : "mysqldump";
        string toolRoot = Path.Combine(contentRoot, "data", "tools");
        string installed = Path.Combine(toolRoot, $"mysql-client-{Version}");
        string installedExecutable = Path.Combine(installed, "bin", executableName);
        if (File.Exists(installedExecutable)) return installedExecutable;

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .AsEnumerable();
        if (windows)
            directories = directories.Append(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MySQL", "MySQL Server 8.4", "bin"));
        foreach (string directory in directories)
        {
            string candidate = Path.Combine(directory.Trim('"'), executableName);
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate) && await IsCompatibleAsync(candidate, ct))
                return candidate;
        }

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || (!windows && !OperatingSystem.IsLinux()))
            throw new InvalidOperationException("此平台需要配置 MySqlBackup:DumpExecutable");

        Directory.CreateDirectory(toolRoot);
        string staging = Path.Combine(toolRoot, $".mysql-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            string suffix = windows ? "winx64.zip" : "linux-glibc2.28-x86_64-minimal.tar.xz";
            string archiveName = $"mysql-{Version}-{suffix}";
            string archive = Path.Combine(staging, archiveName);
            string client = Path.Combine(staging, "client");
            Directory.CreateDirectory(client);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            try
            {
                string url = $"https://cdn.mysql.com/archives/mysql-8.4/{archiveName}";
                using var response = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                await using var output = File.Create(archive);
                await response.Content.CopyToAsync(output, timeout.Token);
            }
            catch (HttpRequestException)
            {
                throw new InvalidOperationException("无法从 MySQL 官方站点下载备份工具，请检查 Bot 主机的网络连接");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new InvalidOperationException("下载 MySQL 备份工具超时，稍后会重试");
            }

            if (windows)
                ExtractWindowsClient(archive, client, executableName, ct);
            else
                await ExtractLinuxClientAsync(archive, client, ct);

            if (!await IsCompatibleAsync(Path.Combine(client, "bin", executableName), ct))
                throw new InvalidOperationException("下载的 mysqldump 无法运行，请检查 Bot 主机的系统运行库");

            Directory.Move(client, installed);
            return installedExecutable;
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private static void ExtractWindowsClient(string archive, string destination, string executableName, CancellationToken ct)
    {
        string prefix = $"mysql-{Version}-winx64/bin/";
        string bin = Path.Combine(destination, "bin");
        Directory.CreateDirectory(bin);
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.StartsWith(prefix, StringComparison.Ordinal) &&
                entry.FullName[prefix.Length..] == entry.Name &&
                (entry.Name == executableName || entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                entry.ExtractToFile(Path.Combine(bin, entry.Name));
        }
    }

    private static async Task ExtractLinuxClientAsync(string archive, string destination, CancellationToken ct)
    {
        string listing;
        try { listing = await RunAsync("tar", ["-tJf", archive], ct); }
        catch (Win32Exception) { throw new InvalidOperationException("自动安装 MySQL 备份工具需要系统提供 tar 和 xz"); }

        var members = listing.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string? executable = Array.Find(members, member => member.EndsWith("/bin/mysqldump", StringComparison.Ordinal));
        if (executable is null)
            throw new InvalidOperationException("MySQL 官方档案中没有找到 mysqldump");

        string library = executable[..^"bin/mysqldump".Length] + "lib/private/";
        var args = new List<string> { "-xJf", archive, "-C", destination, "--strip-components=1", "--", executable };
        if (members.Contains(library, StringComparer.Ordinal)) args.Add(library);
        await RunAsync("tar", args, ct);
    }

    private static async Task<bool> IsCompatibleAsync(string executable, CancellationToken ct)
    {
        try
        {
            string version = await RunAsync(executable, ["--no-defaults", "--no-login-paths", "--version"], ct);
            return !version.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return false; }
    }

    private static async Task<string> RunAsync(string executable, IEnumerable<string> args, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动备份工具");
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"备份工具安装检查失败（退出码 {process.ExitCode}）");
            return output;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
