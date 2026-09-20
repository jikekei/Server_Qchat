using Microsoft.Extensions.Logging;

namespace Server.Qcat.Logging;

/// <summary>
/// 日志级别的持久化与读取 —— 落在 ContentRoot 下的 <c>logging-level.json</c>。
///
/// <para><b>为什么这样做就能"改了立刻生效、又不用重启"：</b></para>
/// 该文件在 <c>Program.cs</c> 里被注册为**带 <c>reloadOnChange</c> 的配置源**，
/// 且位于 appsettings 之后（因此能覆盖它）。.NET 默认主机已把
/// <c>Logging:LogLevel</c> 绑定到 <c>LoggerFilterOptions</c>（通过 <c>IOptionsMonitor</c>），
/// 文件一变 → 配置重载 → 选项重绑 → <c>LoggerFactory</c> 刷新过滤器。
/// 于是**不需要任何自定义 ILoggerProvider**，控制台输出与所有分类的过滤都会同步变化。
///
/// <para><b>优先级：</b>环境变量（<c>Logging__LogLevel__Default</c>）在同一配置源列表里排在
/// 本文件之后，所以环境变量优先级更高 —— 部署时用环境变量锁定的级别不会被面板覆盖。</para>
///
/// <para><b>⚠️ 不要在本类上定义 <c>TryParse</c> 方法。</b>
/// 本类型会被当作 Minimal API 的处理方法参数使用，而 ASP.NET Core 的参数绑定器会扫描
/// 参数类型上的 <c>TryParse</c>；一旦找到一个签名不符的方法（例如
/// <c>TryParse(string, out LogLevel)</c>），它会直接抛
/// <c>InvalidOperationException</c>，导致**整个应用所有接口**在首次匹配路由时全部 500。
/// 需要解析级别请用 <see cref="Normalize"/> + <c>Enum.TryParse</c>。</para>
/// </summary>
public sealed class LogLevelStore
{
    /// <summary>文件名（相对 ContentRoot）。</summary>
    public const string FileName = "logging-level.json";

    /// <summary>可供选择的级别名（顺序由宽到严）。</summary>
    public static readonly string[] LevelNames =
    {
        "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None",
    };

    /// <summary>面板上给出的建议分类（允许自行输入其它分类名）。</summary>
    public static readonly string[] SuggestedCategories =
    {
        "Server.Qcat.LocalAdmin",
        "Server.Qcat.Socket",
        "Server.Qcat.Web",
        "Server.Qcat.Bot",
        "Server.Qcat.Data",
        "Microsoft.AspNetCore",
        "Microsoft.Hosting.Lifetime",
    };

    private readonly string _path;
    private readonly IConfiguration _configuration;
    private readonly ILogger _log;

    public LogLevelStore(string contentRootPath, IConfiguration configuration, ILogger log)
    {
        _path = Path.Combine(contentRootPath, FileName);
        _configuration = configuration;
        _log = log;
    }

    /// <summary>配置文件完整路径（面板展示用）。</summary>
    public string ConfigPath => _path;

    /// <summary>配置文件是否已存在。</summary>
    public bool Exists => File.Exists(_path);

    /// <summary>面板可管理的级别设置。</summary>
    public sealed record Settings(string Default, List<CategoryOverride> Categories);

    /// <summary>单个分类的级别覆盖。</summary>
    public sealed record CategoryOverride(string Category, string Level);

    /// <summary>
    /// 读取当前**生效**的级别：直接从配置读取（而不是只读文件），
    /// 这样即使级别来自环境变量或 appsettings，面板也能显示真实值。
    /// </summary>
    public Settings Load()
    {
        var section = _configuration.GetSection("Logging:LogLevel");

        string def = Normalize(section["Default"]) ?? "Information";

        var categories = new List<CategoryOverride>();
        foreach (var child in section.GetChildren())
        {
            // "Default" 不是分类，是全局默认
            if (string.Equals(child.Key, "Default", StringComparison.OrdinalIgnoreCase))
                continue;

            string? level = Normalize(child.Value);
            if (level is not null)
                categories.Add(new CategoryOverride(child.Key, level));
        }

        categories.Sort((a, b) => string.CompareOrdinal(a.Category, b.Category));
        return new Settings(def, categories);
    }

    /// <summary>
    /// 保存并使其生效。写文件即触发配置热重载，无需重启。
    /// </summary>
    /// <param name="settings">已校验的设置。</param>
    public void Save(Settings settings)
    {
        var logLevel = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Default"] = settings.Default,
        };

        foreach (var c in settings.Categories)
            logLevel[c.Category] = c.Level;

        var model = new Dictionary<string, object?>
        {
            ["Logging"] = new Dictionary<string, object?>
            {
                ["LogLevel"] = logLevel,
            },
        };

        string json = System.Text.Json.JsonSerializer.Serialize(model, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        // 原地整写：文件监视器在句柄关闭后触发重载
        File.WriteAllText(_path, json);
        _log.LogInformation("日志级别已更新并写入 {Path}（无需重启，已热生效）", _path);
    }

    /// <summary>
    /// 把用户输入规范化为合法的级别名；非法返回 null。
    /// 支持 "info" / "INFO" 这类大小写写法。
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        foreach (string name in LevelNames)
        {
            if (string.Equals(name, raw.Trim(), StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return null;
    }
}
