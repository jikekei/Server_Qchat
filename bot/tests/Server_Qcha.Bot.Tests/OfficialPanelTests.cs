using System.Text.Json;
using Server.Qcat.Bot;
using Xunit;

namespace Server.Qcat.Tests;

public class OfficialPanelTests
{
    [Fact]
    public void BuildOfficialPanelItems_Group_MeetsAllOfficialLimits()
    {
        var items = BotCommandCatalog.BuildOfficialPanelItems(isGroup: true);

        // 官方限制：一个指令面板最多 20 个元素
        Assert.NotEmpty(items);
        Assert.True(items.Count <= 20, $"Items count ({items.Count}) exceeds limit 20");
        Assert.Equal(11, items.Count);

        foreach (var item in items)
        {
            // 官方限制：name 最多 14 个字符
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.True(item.Name.Length <= 14, $"Name '{item.Name}' length {item.Name.Length} exceeds 14");
            Assert.StartsWith("/", item.Name);

            // 官方限制：desc 最多 30 个字符
            Assert.False(string.IsNullOrWhiteSpace(item.Desc));
            Assert.True(item.Desc.Length <= 30, $"Desc '{item.Desc}' length {item.Desc.Length} exceeds 30");

            // 官方限制：type 必须为 command 或 link
            Assert.Equal("command", item.Type);
        }

        // 管理指令必须标记为 only_admin = true
        var adminItems = items.Where(i => i.OnlyAdmin).ToList();
        Assert.Equal(4, adminItems.Count);
        Assert.Contains(adminItems, i => i.Name == "/bc");
        Assert.Contains(adminItems, i => i.Name == "/round");
        Assert.Contains(adminItems, i => i.Name == "/ban");
        Assert.Contains(adminItems, i => i.Name == "/setadmin");

        // 普通指令必须标记为 only_admin = false
        var normalItems = items.Where(i => !i.OnlyAdmin).ToList();
        Assert.Equal(7, normalItems.Count);
        Assert.Contains(normalItems, i => i.Name == "/help");
        Assert.Contains(normalItems, i => i.Name == "/cx");
        Assert.Contains(normalItems, i => i.Name == "/info");
        Assert.Contains(normalItems, i => i.Name == "/list");
        Assert.Contains(normalItems, i => i.Name == "/bd");
        Assert.Contains(normalItems, i => i.Name == "/me");
        Assert.Contains(normalItems, i => i.Name == "/version");
    }

    [Fact]
    public void BuildOfficialPanelItems_C2c_ExcludesAdminCommands()
    {
        var items = BotCommandCatalog.BuildOfficialPanelItems(isGroup: false);

        Assert.NotEmpty(items);
        Assert.True(items.Count <= 20);
        Assert.Equal(7, items.Count);

        // 单聊场景全部为普通玩家指令，不允许出现管理指令
        Assert.All(items, item =>
        {
            Assert.False(item.OnlyAdmin);
            Assert.True(item.Name.Length <= 14);
            Assert.True(item.Desc.Length <= 30);
            Assert.Equal("command", item.Type);
        });

        Assert.DoesNotContain(items, i => i.Name == "/bc");
        Assert.DoesNotContain(items, i => i.Name == "/round");
        Assert.DoesNotContain(items, i => i.Name == "/ban");
        Assert.DoesNotContain(items, i => i.Name == "/setadmin");
    }

    [Fact]
    public void OfficialPanelRecord_DeserializesFromOfficialJsonResponse()
    {
        string officialSampleJson = """
        {
          "records": [
            {
              "panel_id": "p_102030405_x8k2",
              "scope": "c2c",
              "target_type": "all",
              "panel": {
                "items": [
                  {
                    "type": "command",
                    "name": "查询天气",
                    "desc": "查询当前天气",
                    "only_admin": false
                  }
                ],
                "remark": "C2C面板",
                "version": 1
              },
              "created_at": "2024-01-15T10:30:00Z",
              "updated_at": "2024-01-15T10:30:00Z",
              "version": 1
            }
          ],
          "next_cursor": "",
          "is_end": true
        }
        """;

        var result = JsonSerializer.Deserialize<OfficialPanelsQueryResult>(officialSampleJson);

        Assert.NotNull(result);
        Assert.NotNull(result.Records);
        Assert.Single(result.Records);
        Assert.True(result.IsEnd);

        var record = result.Records[0];
        Assert.Equal("p_102030405_x8k2", record.PanelId);
        Assert.Equal("c2c", record.Scope);
        Assert.Equal("all", record.TargetType);
        Assert.Equal(1, record.Version);
        Assert.Equal("C2C面板", record.Panel.Remark);
        Assert.Single(record.Panel.Items);

        var item = record.Panel.Items[0];
        Assert.Equal("command", item.Type);
        Assert.Equal("查询天气", item.Name);
        Assert.Equal("查询当前天气", item.Desc);
        Assert.False(item.OnlyAdmin);
    }
}
