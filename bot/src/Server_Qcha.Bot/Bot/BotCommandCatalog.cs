using System.Text;

namespace Server.Qcat.Bot;

/// <summary>指令适用的平台范围。</summary>
[Flags]
public enum BotPlatformScope
{
    None = 0,
    NapCat = 1,
    OfficialQq = 2,
    All = NapCat | OfficialQq,
}

/// <summary>
/// 一条指令的元数据。
///
/// 字段约束来自 QQ 开放平台管理端对「指令」的配置要求：
/// 指令名不超过 8 个中文字符或 16 个英文字符，且应简单凝练；指令与参数之间必须用空格分隔。
/// 因此这里所有 <see cref="Name"/> 与 <see cref="Aliases"/> 均为小写英文短词，长度天然满足限制。
/// </summary>
public sealed record BotCommandDefinition(
    string Name,
    string[] Aliases,
    string Usage,
    string Summary,
    bool AdminOnly,
    BotPlatformScope Scope = BotPlatformScope.All)
{
    /// <summary>是否匹配给定名称（含别名，大小写不敏感）。</summary>
    public bool Matches(string candidate)
    {
        if (string.Equals(Name, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var a in Aliases)
        {
            if (string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

/// <summary>
/// 指令目录 —— 全项目唯一的指令事实来源。
///
/// 用途有三：
/// 1. 指令解析时把别名归一化到规范名（<see cref="Resolve"/>）；
/// 2. 生成面向用户的帮助文本（<see cref="BuildHelpText"/>）；
/// 3. 生成 QQ 开放平台管理端「指令配置」对照清单（<see cref="BuildOfficialConsoleManifest"/>），
///    官方要求指令名称不超过 8 个中文字符 / 16 个英文字符、介绍不超过 15 个中文字符 / 30 个英文字符。
/// </summary>
public static class BotCommandCatalog
{
    public static readonly IReadOnlyList<BotCommandDefinition> All = new List<BotCommandDefinition>
    {
        new("help", new[] { "h", "menu" },
            "/help", "查看全部可用指令", false),

        new("cx", Array.Empty<string>(),
            "/cx", "查询所有在线服务器的人数", false),

        new("info", Array.Empty<string>(),
            "/info", "查询所有服务器运行信息", false),

        new("list", Array.Empty<string>(),
            "/list <服务器序号>", "查看指定服务器玩家列表", false),

        new("bind", new[] { "bd" },
            "/bd <Steam64>", "绑定你的 Steam64 账号", false),

        new("me", new[] { "stat" },
            "/me", "查询自己绑定的玩家数据", false),

        new("version", new[] { "qcha", "ver" },
            "/version", "查看机器人版本信息", false),

        new("broadcast", new[] { "bc" },
            "/bc <服务器序号> <内容>", "向指定服务器广播消息", true),

        new("round", Array.Empty<string>(),
            "/round <服务器序号>", "重启指定服务器回合", true),

        new("ban", new[] { "kick" },
            "/ban <服务器序号> <ID> <时间> <原因>", "踢出或封禁玩家", true),

        new("setadmin", Array.Empty<string>(),
            "/setadmin <服务器序号> <ID> <分组>", "设置玩家权限组", true),
    };

    /// <summary>把用户输入的命令词（可含别名）解析为目录中的定义。</summary>
    public static BotCommandDefinition? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var def in All)
        {
            if (def.Matches(name))
                return def;
        }
        return null;
    }

    /// <summary>生成面向用户的帮助文本。按权限隐藏管理指令，并给出平台相关的触发说明。</summary>
    public static string BuildHelpText(BotPlatform platform, bool isAdmin)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Qcha 指令帮助");
        sb.AppendLine("-------------------------");

        if (platform == BotPlatform.OfficialQq)
        {
            sb.AppendLine("群聊：@机器人 后接指令，或直接发送 /指令");
            sb.AppendLine("私聊：直接发送指令即可");
        }
        else
        {
            sb.AppendLine("群聊与私聊均可直接发送指令");
        }

        sb.AppendLine("指令与参数之间用空格分隔，例如 /bc 1 维护中");
        sb.AppendLine();
        sb.AppendLine("普通指令：");
        foreach (var def in All)
        {
            if (def.AdminOnly)
                continue;
            sb.AppendLine($"  {def.Usage,-40} {def.Summary}");
        }

        if (isAdmin)
        {
            sb.AppendLine();
            sb.AppendLine("管理指令（群主 / 群管理员）：");
            foreach (var def in All)
            {
                if (!def.AdminOnly)
                    continue;
                sb.AppendLine($"  {def.Usage,-40} {def.Summary}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>官方平台下群成员角色不足时的兜底提示。</summary>
    public const string NoPermissionHint = "该指令需要管理权限";

    /// <summary>
    /// 生成 QQ 开放平台管理端「指令配置」清单，便于开发者照抄到后台
    /// （官方要求：指令名 ≤8 个中文字符或 16 个英文字符，指令介绍 ≤15 个中文字符或 30 个英文字符）。
    /// </summary>
    public static string BuildOfficialConsoleManifest()
    {
        var sb = new StringBuilder();
        sb.AppendLine("QQ 开放平台管理端 → 指令配置（按官方字数限制整理）");
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine("序号 | 指令名      | 指令介绍");
        sb.AppendLine("------------------------------------------------------------");

        int index = 1;
        foreach (var def in All)
        {
            string intro = def.Summary;
            if (intro.Length > 15)
                intro = intro[..15];

            sb.AppendLine($"{index,4} | /{def.Name,-11} | {intro}");
            index++;
        }

        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine($"合计 {All.Count} 条（官方上限 24 条）。");
        sb.AppendLine("权限菜单建议：普通指令选「所有用户」，其余选「仅频道主和管理员」。");
        sb.AppendLine("使用场景建议：勾选 QQ 群 与 消息列表（单聊）。");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 生成符合 QQ 开放平台「指令面板」（/v2/panels）规格的面板元素清单。
    /// 限制：数量 ≤ 20，name ≤ 14 字符，desc ≤ 30 字符。
    /// </summary>
    /// <param name="isGroup">true 为群聊场景（包含管理指令且标记 only_admin=true），false 为单聊场景（仅包含普通指令）。</param>
    public static IReadOnlyList<BotOfficialPanelItem> BuildOfficialPanelItems(bool isGroup)
    {
        var list = new List<BotOfficialPanelItem>();
        foreach (var def in All)
        {
            if (!isGroup && def.AdminOnly)
                continue;

            // 优先使用 Usage 中的首词触发名（如 "/help"、"/bd"、"/cx"）
            string trigger = def.Usage.Split(' ', 2)[0];
            if (string.IsNullOrWhiteSpace(trigger))
                trigger = $"/{def.Name}";

            if (trigger.Length > 14)
                trigger = trigger[..14];

            string desc = def.Summary;
            if (desc.Length > 30)
                desc = desc[..30];

            list.Add(new BotOfficialPanelItem(
                Name: trigger,
                Desc: desc,
                Type: "command",
                OnlyAdmin: isGroup && def.AdminOnly
            ));

            if (list.Count >= 20)
                break;
        }

        return list;
    }
}

/// <summary>QQ 开放平台指令面板中的元素定义。</summary>
public sealed record BotOfficialPanelItem(
    string Name,
    string Desc,
    string Type = "command",
    bool OnlyAdmin = false,
    string? Link = null);

