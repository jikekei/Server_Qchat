using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;
using Server.Qcat.Services;

namespace Server_Qcha.Bot.Tests;

public sealed class MySqlBackupTests
{
    [Fact]
    public async Task ArchivesTheCurrentBackupAndTrimsOnlyDailyAndMonthlyCopies()
    {
        using var fixture = new BackupFixture();
        var service = fixture.Service;
        var date = DateOnly.FromDateTime(DateTime.Now);
        string root = Path.Combine(fixture.Root, service.GetStatus().Directory!);

        for (int index = 0; index < 7; index++)
            WriteBackup(root, "daily", date.AddDays(-index), "yyyy-MM-dd");
        for (int index = 1; index <= 6; index++)
            WriteBackup(root, "monthly", date.AddMonths(-index), "yyyy-MM");
        for (int index = 1; index <= 6; index++)
            WriteBackup(root, "yearly", date.AddYears(-index), "yyyy");
        File.WriteAllText(Path.Combine(root, "daily", "unrelated.sql.gz"), "preserve");

        Assert.True((await service.BackupNowAsync(default)).Success);
        var files = service.GetStatus().Files;
        Assert.Equal(5, files.Count(file => file.Tier == "daily"));
        Assert.Equal(5, files.Count(file => file.Tier == "monthly"));
        Assert.Equal(7, files.Count(file => file.Tier == "yearly"));
        Assert.Contains(files, file => file.Tier == "monthly" && file.Name == FileName(date, "yyyy-MM"));
        Assert.Contains(files, file => file.Tier == "yearly" && file.Name == FileName(date, "yyyy"));
        Assert.True(File.Exists(Path.Combine(root, "daily", "unrelated.sql.gz")));

        using var archive = File.OpenRead(Path.Combine(root, "monthly", FileName(date, "yyyy-MM")));
        using var content = new StreamReader(new GZipStream(archive, CompressionMode.Decompress));
        Assert.Equal("SELECT 1;", await content.ReadToEndAsync());
    }

    [Fact]
    public async Task FailedExportPreservesExistingBackupsAndDoesNotPublishANewOne()
    {
        using var fixture = new BackupFixture();
        var service = fixture.Service;
        var date = DateOnly.FromDateTime(DateTime.Now);
        string root = Path.Combine(fixture.Root, service.GetStatus().Directory!);
        for (int index = 1; index <= 5; index++)
            WriteBackup(root, "daily", date.AddDays(-index), "yyyy-MM-dd");

        Assert.False((await service.BackupNowAsync(default)).Success);
        var status = service.GetStatus();
        Assert.Equal(5, status.Files.Count);
        Assert.DoesNotContain(status.Files, file => file.Name == FileName(date, "yyyy-MM-dd"));
        Assert.NotNull(status.Error);
        Assert.Empty(Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories));
    }

    private static string FileName(DateOnly date, string format) =>
        date.ToString(format, CultureInfo.InvariantCulture) + ".sql.gz";

    private static void WriteBackup(string root, string tier, DateOnly date, string format)
    {
        string directory = Path.Combine(root, tier);
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, FileName(date, format)));
        using var writer = new StreamWriter(new GZipStream(file, CompressionLevel.Fastest));
        writer.Write("SELECT 1;");
    }

    private sealed class BackupFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("qcha-backup-test-").FullName;
        public MySqlBackupService Service => new(
            new FixedOptions<MySqlOptions>(new() { ConnectionString = "Server=localhost;Database=test;User ID=test;Password=test" }),
            new FixedOptions<MySqlBackupOptions>(new() { DumpExecutable = Path.Combine(Root, "missing-mysqldump") }),
            new TestEnvironment(Root), NullLogger<MySqlBackupService>.Instance);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
