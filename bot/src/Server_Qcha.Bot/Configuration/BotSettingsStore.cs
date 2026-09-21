using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Server.Qcat.Configuration;

/// <summary>
/// QQ 机器人相关设置的持久化存储 —— 落在 ContentRoot 下的 <c>bot-settings.json</c>。
///
/// 机制说明：
/// 1. 该文件在 <c>Program.cs</c> 里被注册为带 <c>reloadOnChange: true</c> 的配置源；
/// 2. 写入时使用临时文件原子替换，避免并发或写入中断导致文件损坏；
/// 3. 文件写入后，ASP.NET Core 配置系统会自动触发重载，绑定在 <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> 上的
///    <see cref="GoCqHttpOptions"/> 和 <see cref="BotOptions"/> 即刻生效，无需重启应用。
/// </summary>
public sealed class BotSettingsStore
{
    public const string FileName = "bot-settings.json";
    public const string RelativePath = "data/bot-settings.json";

    private readonly string _path;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BotSettingsStore> _log;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null, // 保持 PascalCase 与 appsettings 一致
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // 枚举按名称写出（Mode: "NapCat" / "OfficialQq"），便于人工阅读与手工编辑
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public BotSettingsStore(string contentRootPath, IConfiguration configuration, ILogger<BotSettingsStore> log)
    {
        _path = DataDirectoryManager.GetDataFilePath(contentRootPath, FileName);
        _configuration = configuration;
        _log = log;
    }

    public string ConfigPath => _path;
    public bool Exists => File.Exists(_path);

    public sealed class BotSettingsModel
    {
        public BotOptions Bot { get; set; } = new();
        public GoCqHttpOptions GoCqHttp { get; set; } = new();
        public OfficialQqOptions OfficialQq { get; set; } = new();
        public MySqlOptions MySql { get; set; } = new();
    }

    /// <summary>
    /// 读取当前生效的机器人配置。优先读取当前实时生效的 Configuration，
    /// 确保即使配置来自环境变量、appsettings 也能完整反映在面板上。
    /// </summary>
    public BotSettingsModel LoadCurrent()
    {
        var model = new BotSettingsModel();
        _configuration.GetSection("Bot").Bind(model.Bot);
        _configuration.GetSection("GoCqHttp").Bind(model.GoCqHttp);
        _configuration.GetSection("OfficialQq").Bind(model.OfficialQq);
        _configuration.GetSection("MySql").Bind(model.MySql);

        // 规范化，确保数组不为 null
        model.Bot.AllowedGroupIds ??= Array.Empty<long>();
        model.Bot.NotifyGroupIds ??= Array.Empty<long>();
        model.Bot.NotifyPrivateUserIds ??= Array.Empty<long>();

        model.OfficialQq.AdminOpenIds ??= Array.Empty<string>();
        model.OfficialQq.AllowedGroupOpenIds ??= Array.Empty<string>();
        model.OfficialQq.NotifyGroupOpenIds ??= Array.Empty<string>();
        model.OfficialQq.NotifyPrivateOpenIds ??= Array.Empty<string>();

        return model;
    }

    /// <summary>
    /// 保存设置到 <c>bot-settings.json</c>。原子写入，即刻触发配置热重载。
    /// </summary>
    public void Save(BotSettingsModel model)
    {
        model.Bot.AllowedGroupIds ??= Array.Empty<long>();
        model.Bot.NotifyGroupIds ??= Array.Empty<long>();
        model.Bot.NotifyPrivateUserIds ??= Array.Empty<long>();

        model.OfficialQq.AdminOpenIds ??= Array.Empty<string>();
        model.OfficialQq.AllowedGroupOpenIds ??= Array.Empty<string>();
        model.OfficialQq.NotifyGroupOpenIds ??= Array.Empty<string>();
        model.OfficialQq.NotifyPrivateOpenIds ??= Array.Empty<string>();

        string json = JsonSerializer.Serialize(model, JsonOptions);

        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);

        _log.LogInformation("QQ 机器人设置已保存到 {Path}，配置热重载已触发", _path);
    }
}
