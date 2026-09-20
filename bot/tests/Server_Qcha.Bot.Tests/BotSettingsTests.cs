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
}
