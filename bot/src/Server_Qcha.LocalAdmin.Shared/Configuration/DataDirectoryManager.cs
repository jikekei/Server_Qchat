namespace Server.Qcat.Configuration;

/// <summary>
/// 管理持久化本地数据目录（默认为 ContentRoot 下的 data/ 文件夹）。
/// 统一存放：
/// - panel.db (及其 -wal, -shm)：Web 面板本地 SQLite 数据库
/// - bot-settings.json：机器人设置（NapCat / 官方 API、群白名单、通知目标、MySQL 连接串）
/// - localadmin-servers.json：LocalAdmin 托管服务器定义
/// - logging-level.json：动态日志级别配置
/// - player-history.json：在线人数历史采样记录
///
/// 在系统启动时自动创建 data/ 目录，并无缝自动迁移根目录下的旧版数据文件，
/// 保证用户升级后既有数据 100% 不丢失。
/// </summary>
public static class DataDirectoryManager
{
    public const string DataDirName = "data";

    public static string GetDataDirectory(string contentRootPath)
    {
        string path = Path.Combine(contentRootPath, DataDirName);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }

    public static string GetDataFilePath(string contentRootPath, string fileName)
    {
        return Path.Combine(GetDataDirectory(contentRootPath), fileName);
    }

    /// <summary>
    /// 检查并自动迁移旧版根目录下的数据文件到 data/ 文件夹。
    /// </summary>
    public static void EnsureDataDirectoryAndMigrate(string contentRootPath, Action<string>? logInfo = null)
    {
        string dataDir = GetDataDirectory(contentRootPath);

        // 待迁移的文件映射：根目录旧文件名 -> data 目录新文件名
        var filesToMigrate = new[]
        {
            "bot-settings.json",
            "localadmin-servers.json",
            "logging-level.json",
            "panel.db"
        };

        foreach (var file in filesToMigrate)
        {
            string oldPath = Path.Combine(contentRootPath, file);
            string newPath = Path.Combine(dataDir, file);

            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                try
                {
                    File.Move(oldPath, newPath);
                    logInfo?.Invoke($"已将数据文件自动迁移至 {DataDirName}/ 目录: {file}");

                    // SQLite 特殊附带文件 (-wal, -shm)
                    if (file == "panel.db")
                    {
                        string walOld = oldPath + "-wal";
                        string walNew = newPath + "-wal";
                        if (File.Exists(walOld) && !File.Exists(walNew))
                        {
                            File.Move(walOld, walNew);
                            logInfo?.Invoke($"已迁移 SQLite WAL 文件: {file}-wal");
                        }

                        string shmOld = oldPath + "-shm";
                        string shmNew = newPath + "-shm";
                        if (File.Exists(shmOld) && !File.Exists(shmNew))
                        {
                            File.Move(shmOld, shmNew);
                            logInfo?.Invoke($"已迁移 SQLite SHM 文件: {file}-shm");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logInfo?.Invoke($"迁移数据文件 {file} 失败：{ex.Message}");
                }
            }
        }
    }
}
