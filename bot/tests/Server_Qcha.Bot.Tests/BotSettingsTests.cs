using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Qcat.Configuration;
using Server.Qcat.Web;
using Xunit;

namespace Server.Qcat.Tests;

public class BotSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public BotSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bot_settings_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void BotSettingsStore_SaveAndLoad_Works()
    {
        var config = new ConfigurationBuilder().Build();
        var store = new BotSettingsStore(_tempDir, config, NullLogger<BotSettingsStore>.Instance);

        Assert.False(store.Exists);

        var model = new BotSettingsStore.BotSettingsModel
        {
            GoCqHttp = new GoCqHttpOptions
            {
                WsBaseUri = "ws://192.168.1.100:6700",
                ReconnectDelaySeconds = 8,
                MaxReconnectDelaySeconds = 60,
            },
            Bot = new BotOptions
            {
                AllowedGroupIds = new long[] { 11111, 22222 },
                NotifyGroupIds = new long[] { 11111 },
                NotifyPrivateUserIds = new long[] { 99999 },
                AcTargetGroupId = 11111,
            }
        };

        store.Save(model);

        Assert.True(store.Exists);
        Assert.True(File.Exists(store.ConfigPath));

        // 从文件中直接反序列化验证
        var reloadedConfig = new ConfigurationBuilder()
            .AddJsonFile(store.ConfigPath)
            .Build();

        var loadedStore = new BotSettingsStore(_tempDir, reloadedConfig, NullLogger<BotSettingsStore>.Instance);
        var loaded = loadedStore.LoadCurrent();

        Assert.Equal("ws://192.168.1.100:6700", loaded.GoCqHttp.WsBaseUri);
        Assert.Equal(8, loaded.GoCqHttp.ReconnectDelaySeconds);
        Assert.Equal(60, loaded.GoCqHttp.MaxReconnectDelaySeconds);
        Assert.Equal(new long[] { 11111, 22222 }, loaded.Bot.AllowedGroupIds);
        Assert.Equal(new long[] { 11111 }, loaded.Bot.NotifyGroupIds);
        Assert.Equal(new long[] { 99999 }, loaded.Bot.NotifyPrivateUserIds);
        Assert.Equal(11111, loaded.Bot.AcTargetGroupId);
    }

    [Fact]
    public void PanelPermission_BotManage_Integration_Works()
    {
        Assert.True(PanelPermission.Admin.HasFlag(PanelPermission.BotManage));
        Assert.True(PanelPermission.Owner.HasFlag(PanelPermission.BotManage));
        Assert.False(PanelPermission.Viewer.HasFlag(PanelPermission.BotManage));
        Assert.False(PanelPermission.Operator.HasFlag(PanelPermission.BotManage));

        var keys = PanelPermissions.ToKeys(PanelPermission.BotManage);
        Assert.Contains("bot.manage", keys);

        var parsed = PanelPermissions.FromKeys(new[] { "bot.manage" });
        Assert.Equal(PanelPermission.BotManage, parsed);
    }

    [Fact]
    public void DataDirectoryManager_AutoMigration_Works()
    {
        string dummyDb = Path.Combine(_tempDir, "panel.db");
        string dummyWal = Path.Combine(_tempDir, "panel.db-wal");
        string dummyBot = Path.Combine(_tempDir, "bot-settings.json");
        string dummyLa = Path.Combine(_tempDir, "localadmin-servers.json");
        string dummyLog = Path.Combine(_tempDir, "logging-level.json");

        File.WriteAllText(dummyDb, "sqlite_db_content");
        File.WriteAllText(dummyWal, "sqlite_wal_content");
        File.WriteAllText(dummyBot, "bot_content");
        File.WriteAllText(dummyLa, "la_content");
        File.WriteAllText(dummyLog, "log_content");

        DataDirectoryManager.EnsureDataDirectoryAndMigrate(_tempDir);

        string targetDb = Path.Combine(_tempDir, "data", "panel.db");
        string targetWal = Path.Combine(_tempDir, "data", "panel.db-wal");
        string targetBot = Path.Combine(_tempDir, "data", "bot-settings.json");
        string targetLa = Path.Combine(_tempDir, "data", "localadmin-servers.json");
        string targetLog = Path.Combine(_tempDir, "data", "logging-level.json");

        Assert.True(File.Exists(targetDb));
        Assert.True(File.Exists(targetWal));
        Assert.True(File.Exists(targetBot));
        Assert.True(File.Exists(targetLa));
        Assert.True(File.Exists(targetLog));

        Assert.False(File.Exists(dummyDb));
        Assert.False(File.Exists(dummyWal));
        Assert.False(File.Exists(dummyBot));
        Assert.False(File.Exists(dummyLa));
        Assert.False(File.Exists(dummyLog));
    }
}
