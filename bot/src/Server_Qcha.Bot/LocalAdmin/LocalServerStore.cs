using System.Text.Encodings.Web;
using System.Text.Json;
using Server.Qcat.Configuration;

namespace Server.Qcat.LocalAdmin;

/// <summary>
/// 托管服务器定义的持久化存储 —— 放在 ContentRoot 下的 <c>localadmin-servers.json</c>。
///
/// 存在的意义：让面板能在**运行时**增删改服务器，而不必手改 appsettings.json 再重启。
///
/// 来源优先级（务必理解，否则会被"改了 appsettings 没生效"困惑）：
/// 1. 该文件**存在** → 以文件为唯一事实来源，appsettings 里的 Servers 不再被读取；
/// 2. 该文件**不存在** → 以 appsettings 的 LocalAdmin:Servers 作为种子（首次在面板里
///    保存时，会把种子一并落盘，此后文件即生效）。
///
/// 写入策略：先写同目录临时文件再原子替换，避免写一半崩溃导致配置丢失。
/// </summary>
public sealed class LocalServerStore
{
    /// <summary>存储文件名（相对 ContentRoot）。</summary>
    public const string FileName = "localadmin-servers.json";

    private readonly string _path;
    private readonly ILogger _log;

    // 与 appsettings.json 保持一致的写法：PascalCase 属性名、缩进、中文不转义，
    // 这样生成的文件可以直接照着往 appsettings 里抄，反之亦然。
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public LocalServerStore(string contentRootPath, ILogger log)
    {
        _path = Path.Combine(contentRootPath, FileName);
        _log = log;
    }

    /// <summary>配置文件的完整路径（用于在面板上展示，方便用户直接去编辑）。</summary>
    public string ConfigPath => _path;

    /// <summary>配置文件当前是否存在（存在即代表它是事实来源）。</summary>
    public bool Exists => File.Exists(_path);

    /// <summary>文件承载的数据结构。带 Version 便于以后无损演进。</summary>
    private sealed class FileModel
    {
        public int Version { get; set; } = 1;
        public List<LocalServerDefinition> Servers { get; set; } = new();
    }

    /// <summary>
    /// 尝试从文件读取。文件不存在返回 null（调用方改用 appsettings 种子）。
    /// 解析失败时会先把坏文件改名备份，再返回 null，避免后续保存直接覆盖掉用户数据。
    /// </summary>
    public List<LocalServerDefinition>? TryLoad(out string? error)
    {
        error = null;

        if (!File.Exists(_path))
            return null;

        try
        {
            string json = File.ReadAllText(_path);
            var model = JsonSerializer.Deserialize<FileModel>(json, JsonOptions);

            if (model?.Servers is null)
            {
                error = "配置文件格式不对：缺少 Servers 数组";
                return null;
            }

            return model.Servers;
        }
        catch (Exception ex)
        {
            error = $"读取 {FileName} 失败：{ex.Message}";
            _log.LogError(ex, "读取本地服务器配置文件失败，已备份为 .bad 并回退到 appsettings 种子");

            TryBackupBrokenFile();
            return null;
        }
    }

    /// <summary>整体覆盖保存（原子写：临时文件 → 替换）。</summary>
    public void Save(IReadOnlyList<LocalServerDefinition> servers)
    {
        var model = new FileModel { Version = 1, Servers = servers.ToList() };
        string json = JsonSerializer.Serialize(model, JsonOptions);

        string? dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);

        _log.LogInformation("已保存 {Count} 台本地服务器配置 → {Path}", servers.Count, _path);
    }

    private void TryBackupBrokenFile()
    {
        try
        {
            string backup = $"{_path}.bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(_path, backup, overwrite: true);
            _log.LogWarning("损坏的配置文件已备份为：{Backup}", backup);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "备份损坏的配置文件失败");
        }
    }
}
