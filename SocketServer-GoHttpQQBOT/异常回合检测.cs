using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

public class PlayerData
{
    public string Id { get; set; }
    public string PlayerName { get; set; }
    public int ScpsKilled { get; set; }
    public int PlayersKilled { get; set; }
    public int PlayTime { get; set; }
    public int Deaths { get; set; }
    public string Admin { get; set; }
    public bool IsAdmin { get; set; }

    public PlayerData(string id, string playerName, int scpsKilled, int playersKilled, int playTime, int deaths, string admin, bool isAdmin)
    {
        Id = id;
        PlayerName = playerName;
        ScpsKilled = scpsKilled;
        PlayersKilled = playersKilled;
        PlayTime = playTime;
        Deaths = deaths;
        Admin = admin;
        IsAdmin = isAdmin;
    }
}
namespace SocketServer_GoHttpQQBOT {
    internal class mysqilaip
    {
        public static async Task<DataTable> QueryAsync(string sql, params MySqlParameter[] parameters)
        {
            var result = new DataTable();

            using (var conn = new MySqlConnection("server=127.0.0.1;database=hmyh;user=hmyh;password=hmyh;"))
            using (var cmd = new MySqlCommand(sql, conn))
            {
                if (parameters != null)
                    cmd.Parameters.AddRange(parameters);

                await conn.OpenAsync();
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    result.Load(reader);
                }
            }

            return result;
        }

        public static async Task<List<PlayerData>> GetTopTotalKillersAsync(int top = 10)
        {
            string sql = @"
        SELECT *, (PlayersKilled + ScpsKilled) AS TotalKills
        FROM PlayerData
        ORDER BY TotalKills DESC
        LIMIT @Top";

            var table = await QueryAsync(sql, new MySqlParameter("@Top", top));
            var topKillers = new List<PlayerData>();

            foreach (DataRow row in table.Rows)
            {
                var player = new PlayerData(
                    id: row["Id"].ToString(),
                    playerName: row["PlayerName"].ToString(),
                    scpsKilled: Convert.ToInt32(row["ScpsKilled"]),
                    playersKilled: Convert.ToInt32(row["PlayersKilled"]),
                    playTime: Convert.ToInt32(row["PlayTime"]),
                    deaths: Convert.ToInt32(row["Deaths"]),
                    admin: row["Admin"]?.ToString(),
                    isAdmin: Convert.ToBoolean(row["IsAdmin"])
                );

                topKillers.Add(player);
            }

            return topKillers;
        }
    }
    internal class 异常回合检测
    {

        public static async Task<int> CheckAsync0()
        {
            string folderPath = "C:\\Users\\Administrator\\AppData\\Roaming\\SCP Secret Laboratory\\LocalAdminLogs\\31140"; // 替换为你的文件夹路径
            string errorPattern = @"at InventorySystem\.Items\.Firearms\.Ammo\.ReserveAmmoSync\+<>c\.<Init>b__2_3 \(\) \[0x00008\] in <2343be033e9f4e37923f780ece756d8e>:0";
            string latestFilePath = FindLatestFile(folderPath);
            Console.WriteLine($"最新的文件路径: {latestFilePath}");
            int totalCount = await CountTotalErrorsAsync(latestFilePath, errorPattern);
            return totalCount;
        }
        public static string FindLatestFile(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"目录 {folderPath} 不存在。");
            }

            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
            var latestFile = files.OrderByDescending(f => new FileInfo(f).LastWriteTime).FirstOrDefault();

            if (latestFile == null)
            {
                throw new FileNotFoundException("文件夹中没有找到任何文件。");
            }

            return latestFile;
        }


        static async Task<int> CountTotalErrorsAsync(string filePath, string errorPattern)
        {
            int totalCount = 0;

            try
            {
                using (StreamReader reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (Regex.IsMatch(line, errorPattern))
                        {
                            totalCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取文件 {filePath} 时发生错误: {ex.Message}");
            }

            return totalCount;
        }
    }

    internal class 异常回合检测31192
    {

        public static async Task<int> CheckAsync0()
        {
            //string folderPath = "C:\\Users\\Administrator\\AppData\\Roaming\\SCP Secret Laboratory\\LocalAdminLogs\\31192"; // 替换为你的文件夹路径
            //string errorPattern = @"at InventorySystem\.Items\.Firearms\.Ammo\.ReserveAmmoSync\+<>c\.<Init>b__2_3 \(\) \[0x00008\] in <35a9db24741f426a95f2ae582bf171b2>:0";
            //string latestFilePath = 异常回合检测.FindLatestFile(folderPath);
            //Console.WriteLine($"最新的文件路径: {latestFilePath}");
            //int totalCount = await CountTotalErrorsAsync(latestFilePath, errorPattern);
            return 0;
        }

        //static string FindLatestFile(string folderPath)
        //{
        //    if (!Directory.Exists(folderPath))
        //    {
        //        throw new DirectoryNotFoundException($"目录 {folderPath} 不存在。");
        //    }

        //    var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
        //    Console.WriteLine($"找到的文件数量: {files.Length}");

        //    var latestFile = files.OrderByDescending(f => new FileInfo(f).LastWriteTime).FirstOrDefault();

        //    if (latestFile == null)
        //    {
        //        throw new FileNotFoundException("文件夹中没有找到任何文件。");
        //    }

        //    Console.WriteLine($"最新的文件路径: {latestFile}");
        //    return latestFile;
        //}

        static async Task<int> CountTotalErrorsAsync(string filePath, string errorPattern)
        {
            int totalCount = 0;

            try
            {
                using (StreamReader reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (Regex.IsMatch(line, errorPattern))
                        {
                            totalCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取文件 {filePath} 时发生错误: {ex.Message}");
            }

            return totalCount;
        }
    }
}
