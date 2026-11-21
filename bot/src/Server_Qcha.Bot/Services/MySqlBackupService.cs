using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Server.Qcat.Configuration;

namespace Server.Qcat.Services;

public sealed record MySqlBackupFile(string Tier, string Name, long Size, DateTime LastWriteUtc);
public sealed record MySqlBackupStatus(bool Enabled, bool Configured, bool Running, string? Directory,
    string? Error, IReadOnlyList<MySqlBackupFile> Files);
public sealed record MySqlBackupResult(bool Success, string Message);

public sealed class MySqlBackupService(
    IOptionsMonitor<MySqlOptions> mysql,
    IOptionsMonitor<MySqlBackupOptions> options,
    IHostEnvironment environment,
    ILogger<MySqlBackupService> log) : BackgroundService
{
    private const UnixFileMode PrivateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private string BackupRoot => Path.Combine(environment.ContentRootPath, "data", "mysql-backups");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Failure? _failure;

    private sealed record Failure(string Directory, string Message);
    private sealed record Target(MySqlConnectionStringBuilder Connection, string Directory);

    public MySqlBackupStatus GetStatus()
    {
        var target = GetTarget(out string? error);
        bool enabled = options.CurrentValue.Enabled;
        bool running = _gate.CurrentCount == 0;
        if (target is null)
            return new(enabled, false, running, null, error, []);

        var failure = _failure;
        return new(enabled, true, running, Path.GetRelativePath(environment.ContentRootPath, target.Directory),
            failure?.Directory == target.Directory ? failure.Message : null,
            Enumerate(target.Directory, "daily", "yyyy-MM-dd")
                .Concat(Enumerate(target.Directory, "monthly", "yyyy-MM"))
                .Concat(Enumerate(target.Directory, "yearly", "yyyy"))
                .Select(file => new MySqlBackupFile(file.Directory!.Name, file.Name, file.Length, file.LastWriteTimeUtc))
                .ToArray());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        if (OperatingSystem.IsLinux())
        {
            await _gate.WaitAsync(stoppingToken);
            try { ProtectExistingBackups(stoppingToken); }
            finally { _gate.Release(); }
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Enabled && DateTime.Now.Hour >= 3)
            {
                var target = GetTarget(out _);
                if (target is not null)
                    await RunAsync(target, stoppingToken);
            }

            var now = DateTime.Now;
            var quarterHour = TimeSpan.FromMinutes(15);
            await Task.Delay(quarterHour - TimeSpan.FromTicks(now.Ticks % quarterHour.Ticks), stoppingToken);
        }
    }

    public Task<MySqlBackupResult> BackupNowAsync(CancellationToken ct)
    {
        var target = GetTarget(out string? error);
        return target is null
            ? Task.FromResult(new MySqlBackupResult(false, error ?? "请先配置 MySQL 连接"))
            : RunAsync(target, ct);
    }

    private async Task<MySqlBackupResult> RunAsync(Target target, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) return new(false, "已有备份正在运行");
        try
        {
            var date = DateOnly.FromDateTime(DateTime.Now);
            string daily = BackupPath(target.Directory, "daily", date, "yyyy-MM-dd");
            bool created = !File.Exists(daily);
            if (created)
                await DumpAsync(target.Connection, daily, ct);

            await CopyIfMissingAsync(daily, BackupPath(target.Directory, "monthly", date, "yyyy-MM"), ct);
            await CopyIfMissingAsync(daily, BackupPath(target.Directory, "yearly", date, "yyyy"), ct);
            Trim(target.Directory, "daily", "yyyy-MM-dd");
            Trim(target.Directory, "monthly", "yyyy-MM");
            _failure = null;
            if (created)
                log.LogInformation("MySQL 备份完成：{File}", daily);
            return new(true, created ? "MySQL 备份已完成" : "今天已有备份");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string message = ex switch
            {
                System.ComponentModel.Win32Exception => "无法启动 mysqldump，请安装 MySQL 客户端或配置 MySqlBackup:DumpExecutable",
                InvalidOperationException => ex.Message,
                _ => "写入备份失败，请检查备份目录权限和剩余空间",
            };
            _failure = new(target.Directory, message);
            log.LogWarning("MySQL 备份失败：{Reason}", message);
            return new(false, message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Target? GetTarget(out string? error)
    {
        error = null;
        string? raw = mysql.CurrentValue.ConnectionString;
        if (string.IsNullOrWhiteSpace(raw)) return null;

        MySqlConnectionStringBuilder connection;
        try { connection = new(raw); }
        catch (ArgumentException) { error = "MySQL 连接字符串无效"; return null; }
        if (string.IsNullOrWhiteSpace(connection.Database) || string.IsNullOrWhiteSpace(connection.Server))
        {
            error = "备份需要 MySQL 连接指定服务器和数据库名";
            return null;
        }
        if (connection.ConnectionProtocol != MySqlConnectionProtocol.Sockets || connection.Server.Contains(',') ||
            !string.IsNullOrEmpty(connection.CertificateFile) || !string.IsNullOrEmpty(connection.CertificateThumbprint))
        {
            error = "备份支持单服务器 TCP 连接及 PEM 客户端证书";
            return null;
        }

        string folder = $"db-{Uri.EscapeDataString(connection.Server)}-{connection.Port}-{Uri.EscapeDataString(connection.Database)}";
        return new(connection, Path.Combine(BackupRoot, folder));
    }

    private async Task DumpAsync(MySqlConnectionStringBuilder connection, string destination, CancellationToken ct)
    {
        string executable = await MySqlDumpInstaller.ResolveAsync(
            options.CurrentValue.DumpExecutable, environment.ContentRootPath, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string credentials = Path.Combine(Path.GetTempPath(), $"qcha-mysql-{Guid.NewGuid():N}.cnf");
        string partial = destination + ".partial";
        try
        {
            File.Delete(partial);
            await WriteCredentialsAsync(credentials, connection, ct);
            var start = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            foreach (string arg in new[] { "--defaults-file=" + credentials, "--no-login-paths", "--single-transaction",
                "--quick", "--no-tablespaces", "--set-gtid-purged=OFF", "--databases", "--", connection.Database })
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 mysqldump");
            process.StandardInput.Close();
            Task stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, ct);
            try
            {
                await using (var file = CreatePrivateFile(partial))
                await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                    await process.StandardOutput.BaseStream.CopyToAsync(gzip, ct);

                await process.WaitForExitAsync(ct);
                await stderr;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"mysqldump 退出码 {process.ExitCode}，请检查数据库连接和备份权限");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                try { await stderr; }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            }

            File.Move(partial, destination);
        }
        finally
        {
            File.Delete(credentials);
            File.Delete(partial);
        }
    }

    private static async Task WriteCredentialsAsync(string path, MySqlConnectionStringBuilder connection, CancellationToken ct)
    {
        await using var file = CreatePrivateFile(path);
        await using var writer = new StreamWriter(file);
        string sslMode = connection.SslMode switch
        {
            MySqlSslMode.None => "DISABLED",
            MySqlSslMode.VerifyCA => "VERIFY_CA",
            MySqlSslMode.VerifyFull => "VERIFY_IDENTITY",
            _ => connection.SslMode.ToString().ToUpperInvariant(),
        };
        var values = new Dictionary<string, string>
        {
            ["host"] = connection.Server, ["port"] = connection.Port.ToString(CultureInfo.InvariantCulture),
            ["user"] = connection.UserID, ["password"] = connection.Password, ["protocol"] = "TCP",
            ["ssl-mode"] = sslMode,
        };
        if (!string.IsNullOrEmpty(connection.SslCa)) values["ssl-ca"] = connection.SslCa;
        if (!string.IsNullOrEmpty(connection.SslCert)) values["ssl-cert"] = connection.SslCert;
        if (!string.IsNullOrEmpty(connection.SslKey)) values["ssl-key"] = connection.SslKey;
        if (connection.AllowPublicKeyRetrieval) values["get-server-public-key"] = "ON";
        await writer.WriteLineAsync("[client]");
        foreach (var (key, value) in values)
        {
            string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
            await writer.WriteLineAsync($"{key}=\"{escaped}\"".AsMemory(), ct);
        }
    }

    private static string BackupPath(string root, string tier, DateOnly date, string format) =>
        Path.Combine(root, tier, date.ToString(format, CultureInfo.InvariantCulture) + ".sql.gz");

    private static FileStream CreatePrivateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = PrivateMode;
        return new FileStream(path, options);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private void ProtectExistingBackups(CancellationToken ct)
    {
        var root = new DirectoryInfo(BackupRoot);
        if (!root.Exists || root.LinkTarget is not null) return;

        try
        {
            foreach (var target in root.EnumerateDirectories("db-*"))
            {
                if (target.LinkTarget is not null) continue;
                foreach (var (tier, format) in new[] { ("daily", "yyyy-MM-dd"), ("monthly", "yyyy-MM"), ("yearly", "yyyy") })
                {
                    var directory = new DirectoryInfo(Path.Combine(target.FullName, tier));
                    if (!directory.Exists || directory.LinkTarget is not null) continue;
                    foreach (var file in directory.EnumerateFiles("*.sql.gz*"))
                    {
                        ct.ThrowIfCancellationRequested();
                        string name = file.Name;
                        if (name.EndsWith(".partial", StringComparison.Ordinal)) name = name[..^8];
                        if (file.LinkTarget is not null || !name.EndsWith(".sql.gz", StringComparison.Ordinal) ||
                            !DateOnly.TryParseExact(name[..^7], format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                            continue;
                        try
                        {
                            if (File.GetUnixFileMode(file.FullName) != PrivateMode)
                                File.SetUnixFileMode(file.FullName, PrivateMode);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            log.LogWarning(ex, "无法限制 MySQL 备份文件权限：{File}", file.FullName);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "无法检查已有 MySQL 备份文件的权限");
        }
    }

    private static IEnumerable<FileInfo> Enumerate(string root, string tier, string format)
    {
        var directory = new DirectoryInfo(Path.Combine(root, tier));
        if (!directory.Exists) return [];
        return directory.EnumerateFiles("*.sql.gz")
            .Where(file => DateOnly.TryParseExact(file.Name[..^7], format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .OrderByDescending(file => file.Name, StringComparer.Ordinal);
    }

    private static async Task CopyIfMissingAsync(string source, string destination, CancellationToken ct)
    {
        if (File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string partial = destination + ".partial";
        try
        {
            File.Delete(partial);
            await using (var input = File.OpenRead(source))
            await using (var output = CreatePrivateFile(partial))
                await input.CopyToAsync(output, ct);
            File.Move(partial, destination);
        }
        finally
        {
            File.Delete(partial);
        }
    }

    private static void Trim(string root, string tier, string format)
    {
        foreach (var old in Enumerate(root, tier, format).Skip(5))
            old.Delete();
    }
}
