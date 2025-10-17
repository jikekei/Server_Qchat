using EleCho.GoCqHttpSdk;
using EleCho.GoCqHttpSdk.Action;
using EleCho.GoCqHttpSdk.Message;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SocketServer_GoHttpQQBOT;
using System.Data;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions; // 引入 MySQL 数据库操作的命名空间
using TWCore;
//using System.Data.Common; // 添加此行以引入 DbDataReader
//using MySql.Data.MySqlClient; // 添加此行以引入 MySqlConnection
//using MySql.Data.MySqlClient.Replication; // 添加此行以引入 MySqlReplicationConnection
//待添加功能，yiming _2024.12.7

        string GetCurrentTimeFormatted()
{
    DateTime now = DateTime.Now; // 获取当前日期和时间
                                 // 将时间格式化为指定格式
    return now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
}

int 异常日志数量 = 0;
int 警告次数 = 0;

while (true)
{
    static async Task<string> SocketServerAsync(string serverIP, int port, string text, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++) // 最多尝试3次连接
        {
            try
            {
                using TcpClient client = new TcpClient();
                // 设置连接超时
                var connectTask = client.ConnectAsync(serverIP, port);
                if (await Task.WhenAny(connectTask, Task.Delay(10000, cancellationToken)) == connectTask) // 10秒超时
                {
                    using NetworkStream stream = client.GetStream();
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    DateTime now = DateTime.Now;
                    string currentDate = now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
                    // 发送数据
                    Console.WriteLine($"[{currentDate}]正在发送数据: {text} 到 {serverIP}:{port}");
                    await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);

                    // 设置接收超时
                    client.ReceiveTimeout = 2; // 10秒超时

                    byte[] array = new byte[1024];

                    // 设置接收超时为10秒
                    var readTask = stream.ReadAsync(array, 0, array.Length, cancellationToken);
                    var delayTask = Task.Delay(2000, cancellationToken); // 10秒延迟

                    // 等待其中一个任务完成
                    if (await Task.WhenAny(readTask, delayTask) == readTask)
                    {
                        // 如果读取完成
                        int count = await readTask; // 确保读取成功
                        Console.WriteLine($"[{currentDate}]数据接收成功");
                        return Encoding.UTF8.GetString(array, 0, count);
                    }
                    else
                    {
                        // 如果超时
                        Console.WriteLine($"[{currentDate}]接收数据超时");
                        return "null";
                    }
                }
                else
                {
                    Console.WriteLine("连接超时，尝试重新连接...");
                }
            }
            catch (SocketException se)
            {
                Console.WriteLine($"Socket错误: {se.Message}，尝试重新连接...");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("请求被取消，尝试重新连接...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SocketServerAsync 发生错误: {ex.Message}");
                return "null";
            }

            await Task.Delay(1000); // 等待1秒再重试
        }
        return "null"; // 如果所有重试都失败返回null
    }
    Console.WriteLine("GitHub 项目地址: https://github.com/jikekei/Server_Qchat");

    Console.WriteLine("\r\n[Server-Qcat]: 使用前请确保已开放以下端口：");
    Console.WriteLine(" - 正向 WebSocket 端口：6700");
    Console.WriteLine(" - 服务器端 TCP 端口");

    Console.WriteLine(); // 空行分隔

    Console.WriteLine("请输入服务器查询端口（如有多个请用 '*' 分隔，例如：7777*7778*7779）：");

    string ports = Console.ReadLine() ?? "31146*31150*31160*31170*31182*31171*31192"; // 默认端口
    string[] port1 = ports.Split('*').Select(p => p.Trim()).ToArray();
    List<int> validPorts = new List<int>();

    foreach (var port in port1)
    {
        if (int.TryParse(port, out int parsedPort))
        {
            validPorts.Add(parsedPort);
        }
        else
        {
            Console.WriteLine($"输入的端口 '{port}' 无效，请输入有效的整数端口。");
        }
    }
    Console.WriteLine("\r\n[DIRSystem]: ");
    Console.WriteLine("\r\n[DIRSystem]: 正在连接WebSK-Success-Sk6700端口");

    CqWsSession session = new CqWsSession(new CqWsSessionOptions
    {
        BaseUri = new Uri("ws://localhost:6700"),  // WebSocket 地址
    });
    string userMention;
    string ipAddress = "180.188.21.118"; // 要Ping的IP地址
    Ping ping = new Ping();
    bool ContainsSensitiveWord(string input)
    {
        // 将mgc字符串按换行符分割成数组
        string[] sensitiveWords = YourBotClass.mgc.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        // 检查输入是否包含在敏感词数组中
        return sensitiveWords.Contains(input);
    }

    session.UseGroupMessage(async context =>
    {
        Console.WriteLine("收到信息");
        try
        {
            if ((context.Message.Text == "cx" || context.Message.Text == "info" || context.Message.Text.StartsWith("#") || context.Message.Text.StartsWith("＃")))
            {

                string responseMessage = string.Empty;
                userMention = $"\r\n本次查询由@{context.Sender.Nickname} 触发"; // @触发用户

                switch (context.Message.Text)
                {
                    case "cx":
                        responseMessage = await HandleCxCommand(validPorts);
                        break;
                    case "info":
                        responseMessage = await HandleInfoCommand(validPorts);
                        break;
                    case "#1":
                    case "rj":
                        responseMessage = await HandlePlayerListCommand(validPorts, 0);
                        break;
                    default:
                        if (context.Message.Text.StartsWith("#") || context.Message.Text.StartsWith("＃"))
                        {
                            int portIndex;
                            if (int.TryParse(context.Message.Text.Substring(1), out portIndex))
                            {
                                responseMessage = await HandlePlayerListCommand(validPorts, portIndex - 1);
                            }
                            else
                            {
                                responseMessage = "请求失败 awa";
                            }
                        }
                        break;
                }

                await session.SendGroupMessageAsync(context.GroupId, new EleCho.GoCqHttpSdk.Message.CqMessage(responseMessage + $"\r\n查询时间 {DateTime.Now}"));
            }
            else if (context.Message.Text.StartsWith("/ban "))
            {
                if (!(context.Sender.Role == CqRole.Admin || context.Sender.Role == CqRole.Owner))
                {
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage("你没权限hahah"));
                    return;
                }

                string[] parts = context.Message.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    await SendErrorMessageAsync();
                    return;
                }

                if (int.TryParse(parts[1], out int userId) && userId >= 0 && userId < validPorts.Count)
                {
                    string reason = $"{parts[2]}&{parts[4]}&{parts[3]}";
                    string response = await SocketServerAsync("127.0.0.1", validPorts[userId - 1], $"kick&{reason}", CancellationToken.None);
                    response = response == "null" ? "服务器不在线 awa" : response;
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage(response));
                }
                else
                {
                    await SendErrorMessageAsync();
                }

                async Task SendErrorMessageAsync()
                {
                    string errorMessage = "请输入正确的用户ID格式: /ban <服务器索引> <ID> <时间> <原因>";
                    Console.WriteLine("无法将输入转换为整数或索引超出范围: " + parts[1]);
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage(errorMessage));
                }
            }
            else if (context.Message.Text == "/异常回合自动化检测" || context.Message.Text.Contains("/detection"))
            {
                Console.WriteLine("开始回合自动化检测 /detection");
              await  回合自动化检测(context.GroupId);
            }
            else if (context.Message.Text.StartsWith("/round "))
            {
                if (!(context.Sender.Role == CqRole.Admin || context.Sender.Role == CqRole.Owner))
                {
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage("你没权限hahah"));
                    return;
                }

                string[] parts = context.Message.Text.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    SendErrorMessageAsync();
                    return;
                }

                if (int.TryParse(parts[1], out int userId))
                {
                    if (validPorts.Count == 0)
                    {
                        await session.SendGroupMessageAsync(context.GroupId, new CqMessage("没有有效的端口。"));
                        return;
                    }

                    if (userId < 1 || userId > validPorts.Count) // 服务器索引从1开始
                    {
                        await session.SendGroupMessageAsync(context.GroupId, new CqMessage("请求的服务器索引无效 awa"));
                        return;
                    }

                    string response = await SocketServerAsync("127.0.0.1", validPorts[userId - 1], "rest", CancellationToken.None); // 只发送"bc"作为命令
                    response = response == "null" ? "服务器不在线 awa" : response;
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage(response));
                }
                else
                {
                    SendErrorMessageAsync();
                }

                async void SendErrorMessageAsync()
                {
                    string errorMessage = "请输入正确的用户ID格式: /round <服务器索引>";
                    Console.WriteLine("无法将输入转换为整数: " + parts[1]);
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage(errorMessage));
                }
            }
            else if (context.Message.Text.StartsWith("/bc "))
            {
                if(!(context.Sender.Role == CqRole.Admin || context.Sender.Role == CqRole.Owner))
                {
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage("你没权限hahah"));

                    return;
                }
                string[] parts = context.Message.Text.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    SendErrorMessageAsync();
                    return;
                }

                if (int.TryParse(parts[1], out int userId))
                {
                    if (validPorts.Count == 0)
                    {
                        await session.SendGroupMessageAsync(context.GroupId, new CqMessage("没有有效的端口。"));
                        return;
                    }

                    if (userId < 0 || userId >= validPorts.Count)
                    {
                        await session.SendGroupMessageAsync(context.GroupId, new CqMessage("请求的服务器索引无效 awa"));
                        return;
                    }

                    string reason = parts[2];


                    string response = await SocketServerAsync("127.0.0.1", validPorts[userId - 1], $"bc&{reason}", CancellationToken.None);
                    response = response == "null" ? "服务器不在线 awa" : response;
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage(response));
                }
                else
                {
                    SendErrorMessageAsync();
                }

                async void SendErrorMessageAsync()
                {
                    string errorMessage = "请输入正确的用户ID格式: /bc <服务器索引> <内容>";
                    Console.WriteLine("无法将输入转换为整数: " + parts[1]);
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage(errorMessage));
                }
            }
            else if (context.Message.Text.StartsWith("/setadmin "))
            {
                if (!(context.Sender.Role == CqRole.Admin || context.Sender.Role == CqRole.Owner))
                {
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage("你没权限 hahah"));
                    return;
                }

                string[] parts = context.Message.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                {
                    SendErrorMessageAsync();
                    return;
                }

                if (int.TryParse(parts[1], out int userId))
                {
                    if (validPorts.Count == 0)
                    {
                        await session.SendGroupMessageAsync(context.GroupId, new CqMessage("没有有效的端口。"));
                        return;
                    }

                    if (userId < 0 || userId >= validPorts.Count)
                    {
                        await session.SendGroupMessageAsync(context.GroupId, new CqMessage("请求的服务器索引无效 awa"));
                        return;
                    }

                    string reason = parts[2];
                    string reason0 = parts[3];

                    try
                    {
                        string response = await SocketServerAsync("127.0.0.1", validPorts[userId - 1], $"bc&{reason}&{reason0}", CancellationToken.None);
                        response = response == "null" ? "服务器不在线 awa" : response;
                        await session.SendGroupMessageAsync(context.GroupId, new CqMessage(response));
                    }
                    catch (Exception ex)
                    {
                        await session.SendGroupMessageAsync(context.GroupId, new CqMessage("请求服务器时发生错误: " + ex.Message));
                    }
                }
                else
                {
                    SendErrorMessageAsync();
                }

                async Task SendErrorMessageAsync()
                {
                    string errorMessage = "请输入正确的用户ID格式: /setadmin <服务器索引> <ID> <权限分组> ";
                    Console.WriteLine("无法将输入转换为整数: " + parts[1]);
                    await session.SendGroupMessageAsync(context.GroupId, new CqMessage(errorMessage));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"消息处理错误: {ex.Message}");
            //await session.SendGroupMessageAsync(context.GroupId, new EleCho.GoCqHttpSdk.Message.CqMessage(""));
        }
    });

    async Task<string> HandleCxCommand(List<int> validPorts)
    {
        //Console.WriteLine($"消息处理 HandleCxCommand");
        //string apiResponse = await FetchOnlineUserCount(); // 调用API方法
        //string s1 = $"幻梦银河 --- 插件1服 \r\n在线人数:{apiResponse}\r\n在线管理:？？未知 \r\n[已隐藏无人服务器]";
        //return s1;
        if (validPorts.Count == 0) return "没有有效的端口。";

        int totalOnlineCount = 0; // 使用总在线人数的变量
        var tasks = validPorts.Select(async (port, index) =>
        {
            string response = await SocketServerAsync("127.0.0.1", port, "cx", CancellationToken.None);

            // 直接返回不在线的情况
            if ((response.Contains("在线人数:0/45") || response.Contains("在线人数:0/40")))
            {
                return string.Empty; // 如果服务器不在线，则返回空字符串
            }

            // 使用正则表达式提取在线人数
            Match match = Regex.Match(response, @"在线人数:(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int onlineCount))
            {
                totalOnlineCount += onlineCount; // 累加在线人数
                return response; // 返回响应
            }

            return response == "null" ? $"#{index + 1}服不在线 awa\r\n" : response;
        });

        var results = await Task.WhenAll(tasks); // 等待所有任务完成
        //string responseMessage = await MC0();
        // 使用 string.Join 合并结果，并添加总在线人数
        return string.Join(string.Empty, results) + /*responseMessage*/ $"总在线人数:{totalOnlineCount}\r\n[已隐藏无人服务器]";
    }



    //async Task<string> HandleCxCommand(List<int> validPorts)
    // {

    //     //string apiResponse = await FetchOnlineUserCount(); // 调用API方法
    //     //string apiResponse2 = await FetchOnlineUserCount2(); // 调用API方法
    //     //string s1 = $"幻梦银河 --- [14.0]beta测试1服 \r\n在线人数:{apiResponse}\r\n幻梦银河 --- [14.0]beta测试2服 \r\n在线人数:{apiResponse2}";
    //     //return s1;
    //     if (validPorts.Count == 0) return "没有有效的端口。";
    //     int ss1 = 0;
    //     var tasks = validPorts.Select(async (port, index) =>
    //     {
    //         string response = await SocketServerAsync("127.0.0.1", port, "cx", CancellationToken.None);
    //         if( response.Contains("0/45") || response.Contains("0/40"))
    //         {
    //             response = "";
    //         }
    //         Match match = Regex.Match(response, @"<在线人数:(\d+)>");
    //         // 检查是否有匹配，并将匹配的数字加到 ss1
    //         if (match.Success)
    //         {
    //             // 提取匹配的数字并转换为 int
    //             if (int.TryParse(match.Groups[1].Value, out int onlineCount))
    //             {
    //                 ss1 += onlineCount;
    //             }
    //         }
    //         return response == "null" ? $"#{index + 1}服不在线 awa\r\n" : response;
    //     });


    //     var results = await Task.WhenAll(tasks); // 等待所有任务完成

    //     // 使用 string.Join 合并结果，并添加总在线人数
    //     return string.Join("", results) + $"\r\n总在线人数:{ss1}";
    // }

    // 处理“info”命令
    async Task<string> HandleInfoCommand(List<int> validPorts)
    {
        if (validPorts.Count == 0) return "没有有效的端口。";

        var tasks = validPorts.Select(async (port, index) =>
        {
            string response = await SocketServerAsync("127.0.0.1", port, "info", CancellationToken.None);
            return response == "null" ? $"#{index + 1}服不在线 awa\r\n" : response;
        });

        return string.Concat(await Task.WhenAll(tasks));
    }

    // 处理玩家列表命令
    async Task<string> HandlePlayerListCommand(List<int> validPorts, int portIndex)
    {
        if (validPorts.Count == 0) return "没有有效的端口。";
        if (portIndex < 0 || portIndex >= validPorts.Count) return "请求的服务器索引无效 awa";

        string response = await SocketServerAsync("127.0.0.1", validPorts[portIndex], "list", CancellationToken.None);
        return response == "null" ? "请求失败 awa" : $"服务器{portIndex + 1}服玩家列表" + response;
    }

    async Task<string> SaveQQNumberToDatabase(string playerId, long qqNumber)
    {
        string connectionString = "server=127.0.0.1;database=hmyh;user=hmyh;password=hmyh;";

        using (MySqlConnection connection = new MySqlConnection(connectionString))
        {
            try
            {
                await connection.OpenAsync();

                // 检查玩家是否已经存在
                string checkPlayerQuery = "SELECT COUNT(*) FROM playerdata WHERE Id = @id;";
                using (MySqlCommand checkCommand = new MySqlCommand(checkPlayerQuery, connection))
                {
                    checkCommand.Parameters.AddWithValue("@Id", playerId);
                    int playerCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                    if (playerCount > 0) // 如果存在该玩家，则更新QQ号
                    {
                        string updateQuery = "UPDATE playerdata SET QQ_ID = @QQ_ID WHERE Id = @id;";
                        using (MySqlCommand command = new MySqlCommand(updateQuery, connection))
                        {
                            command.Parameters.AddWithValue("@Id", playerId);
                            command.Parameters.AddWithValue("@QQ_ID", qqNumber);
                            await command.ExecuteNonQueryAsync();
                            return "绑定成功 qwq";
                        }
                    }
                    else // 如果不存在则返回提示
                    {
                        return "服务器数据库内未找到该玩家的ID。请输入/bd <@Steam64> 绑定 QQ 号。\r\n例如 \r\n/bd 76561199888888888@Steam";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存QQ号到数据库失败: {ex.Message}");
                return "保存QQ号时发生错误。";
            }
        }
    }
    

//// 从数据库获取与QQ号相关的所有信息
//async Task<string> GetAllDataByQQNumber(string qqNumber)
//{
//    string connectionString = "server=127.0.0.1;database=player_mvp;user=player_mvp;password=player_mvp;";

//    using (MySqlConnection connection = new MySqlConnection(connectionString))
//    {
//        try
//        {
//            await connection.OpenAsync();

//            // 查询所有与 QQ 号 相关的数据
//            string query = "SELECT * FROM player_kills WHERE qq_number = @qqNumber;";
//            using (MySqlCommand command = new MySqlCommand(query, connection))
//            {
//                command.Parameters.AddWithValue("@qqNumber", qqNumber);

//                // 将 MySqlDataReader 改为 DbDataReader
//                using (DbDataReader reader = await command.ExecuteReaderAsync())
//                {
//                    if (reader.HasRows)
//                    {
//                        StringBuilder resultBuilder = new StringBuilder();

//                        while (await reader.ReadAsync())
//                        {
//                            // 假设表的字段有 player_id, player_name, kills 和 game_data
//                            string playerId = reader.GetString("player_id");
//                            string playerName = reader.GetString("player_name");
//                            int kills = reader.GetInt32("kills");
//                            int gameData = reader.GetInt32("game_data");

//                            resultBuilder.AppendLine($"玩家 ID: {playerId}, 玩家名称: {playerName}, 击杀数: {kills}, 游戏数据: {gameData}");
//                        }

//                        return resultBuilder.ToString();
//                    }
//                    else
//                    {
//                        return "没有找到与该 QQ 号相关的任何数据。 请输入/bd <@steam64> 绑定 QQ 号。";
//                    }
//                }
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"获取数据失败: {ex.Message}");
//            return "获取数据时发生错误。";
//        }
//    }
//}

// 从数据库获取与QQ号相关的所有信息
async Task<string> GetAllDataByQQNumber(string qqNumber)
    {
        string connectionString = "server=127.0.0.1;database=hmyh;user=hmyh;password=hmyh;";

        using (MySqlConnection connection = new MySqlConnection(connectionString))
        {
            try
            {
                await connection.OpenAsync();

                // 查询所有与 QQ 号 相关的数据
                string query = "SELECT * FROM playerdata WHERE QQ_ID = @QQ_ID;";
                using (MySqlCommand command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@QQ_ID", qqNumber);

                    using (MySqlDataReader reader = (MySqlDataReader)await command.ExecuteReaderAsync())
                    {
                        if (reader.HasRows)
                        {
                            StringBuilder resultBuilder = new StringBuilder();

                            while (await reader.ReadAsync())
                            {
                                // 假设表的字段有 player_id, player_name, kills 和 game_data
                                int ScpsKilled = reader.GetInt32("ScpsKilled");

                                int PlayersKilled = reader.GetInt32("PlayersKilled");

                                int kills = reader.GetInt32("IsAdmin");

                                int gameData = reader.GetInt32("PlayTime");

                                int Deaths = reader.GetInt32("Deaths");

                                string PlayerName = reader.GetString("PlayerName");

                                string Admin = reader.GetString("Admin");


                                double hours = (double)gameData / 3600; // 将秒数转换为小时数，并保留小数部分
                                //var s2 = kills / savedDeathCount;
                                string dnt  = "暂无数据";
                                if(kills == 0)
                                {
                                    dnt = "未开启DNT";
                                }else if(kills == 1)
                                {
                                    dnt = "您开启了DNT，我们不会采集任何数据";
                                    resultBuilder.AppendLine($"{dnt}\r\n同时我们建议您关闭DNT，因为它会让我们无法收集信息。\r\n您获取不了击杀数据\r\n-幻梦银河社区");
                                    return resultBuilder.ToString();
                                }
                                double kd = 0;
                                if (Deaths == 0)
                                {
                                    // 避免除以零的情况，可以设定KD为击杀数，或者直接设为无穷大（double.PositiveInfinity）
                                    kd = 0;
                                }
                                else
                                {
                                    kd = (ScpsKilled * 5 + PlayersKilled) / (double)Deaths;
                                }
                                kd = Math.Round(kd, 6);

                                resultBuilder.AppendLine(
    $"          玩家信息统计\r\n----------------------------\n" +
    $"玩家名称：{PlayerName}\n" +
    $"SCP 击杀数：{ScpsKilled}\n" +
    $"玩家击杀数：{PlayersKilled}\n" +
    $"游玩时间：{hours:F2} 小时\n" +
    $"死亡次数：{Deaths}\n" +
    $"补充说明：{Admin}\n" +
    $"DNT 状态：{dnt}\n" +
    $"KD 比率：{kd}"
);

                                //resultBuilder.AppendLine($"玩家名称: {PlayerName}\r\nScp击杀数: {ScpsKilled}\r\n玩家击杀数: {PlayersKilled}\r\n玩家游玩时间: {s1}分钟\r\n玩家死亡数: {Deaths}\r\n补充：{Admin}\r\nDNT状态: {dnt}\r\n玩家KD: {kd}");
                            }

                            return resultBuilder.ToString();
                        }
                        else
                        {
                            return "您没有绑定账号，请输入/bd <@steam64> 绑定 QQ 号。\r\n例如 \r\n/bd 76561199888888888@Steam";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取数据失败: {ex.Message}");
                return "获取数据时发生错误。";
            }
        }
    }

    await Task.Run(async () => // 使用 Task.Run 启动一个新的任务
    {
        // 其他代码...

        // 在后台启动 MonitorPortsAsync，不会阻塞主线程
        //_ = MonitorPortsAsync(validPorts, CancellationToken.None); // 使用 _ 表示我们不需要等待这个任务完成
        //_ = CleanFloorPlugin0();
        // 这里是主程序的其他逻辑
        // 例如运行 WebSocket 会话
        await session.RunAsync();
    });


    async Task CleanFloorPlugin0()
    {
        while (true)
        {
            int count = await SocketServer_GoHttpQQBOT.异常回合检测.CheckAsync0();
            int count31192 = await SocketServer_GoHttpQQBOT.异常回合检测31192.CheckAsync0();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[自动化回合异常检测]回合异常检查 ，异常日志数量：{count}");
            Console.WriteLine($"[自动化回合异常检测]31192 回合异常检查 ，异常日志数量：{count31192}");
            Console.ForegroundColor = ConsoleColor.White;
            if (count > 10 && 警告次数 <= 3)
            {
                //await session.SendGroupMessageAsync(1015444727, new EleCho.GoCqHttpSdk.Message.CqMessage($"回合异常检查，\r\n检查到当前回合异常请管理员上线手动检查已确认，异常日志数量：{count}\r\n输入/detection将启动自动重启程序。即使您不是管理员也可以激活\r\n时间:{DateTime.Now}"));
                await session.SendPrivateMessageAsync(3037240065, new EleCho.GoCqHttpSdk.Message.CqMessage($"回合异常检查，\r\n检查到当前回合异常，异常日志数量：{count}\r\n时间:{DateTime.Now}"));
                await Task.Delay(8000);
                await 自动重启(31140);
                //await session.SendGroupMessageAsync(760821335, new EleCho.GoCqHttpSdk.Message.CqMessage($"回合异常检查，\r\n检查到当前回合异常请管理员上线手动检查已确认，异常日志数量：{count}\r\n输入/detection将启动自动重启程序。即使您不是管理员也可以激活\r\n时间:{DateTime.Now}"));
                警告次数 ++;
            }
            if (count31192 > 10 && 警告次数 <= 3)
            {
                await session.SendPrivateMessageAsync(3037240065, new EleCho.GoCqHttpSdk.Message.CqMessage($"回合异常检查，\r\n检查到当前回合异常，异常日志数量：{count31192}\r\n时间:{DateTime.Now}"));
                await Task.Delay(8000);
                await 自动重启(31192);
                警告次数++;
            }
            if(count == 0 && count31192 == 0)
            {
                警告次数 = 0;
            }
            异常日志数量 = count;
            await Task.Delay(1000);
        }
    }
// MonitorPortsAsync 的实现
async Task MonitorPortsAsync(List<int> validPorts, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // 监听端口
            //await CleanFloorPlugin(validPorts, cancellationToken);
            // 防止 CPU 占用过高，延迟后再继续检查端口
            await Task.Delay(1000);
        }
    }

    async Task CleanFloorPlugin(List<int> validPorts, CancellationToken cancellationToken)
    {
        while (true)
        {
            //try
            //{
            //    await session.SetGroupNicknameAsync(1015444727, 2684156059, "芝!💩!🥚!🍔!");
            //    //await session.InvokeActionAsync<CqSetGroupNicknameAction, CqSetGroupNicknameActionResult>(new CqSetGroupNicknameAction(1015444727, 2684156059, "芝!💩!🥚!🍔!"));
            //    Console.WriteLine((1015444727, 2684156059, "芝!💩!🥚!🍔!"));
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine($"发生错误: {ex.Message}");
            //}




            foreach (var port in validPorts)
            {
                // 检查取消请求
                cancellationToken.ThrowIfCancellationRequested();

                // 调用 SocketServerAsync 进行监听
                string response = await SocketServerAsync("127.0.0.1", port, "ac", cancellationToken);

                // 检查响应并进行处理
                if (response != "null" && response != null && !response.Contains("null") && response != "")
                {
                    Console.WriteLine($"从端口 {port} 接收到响应: {response}");
                    // 发送消息
                    await session.SendGroupMessageAsync(760821335, new EleCho.GoCqHttpSdk.Message.CqMessage($"{response}\r\n" +
                        $"求助时间:{DateTime.Now}"));
                    await session.SendGroupMessageAsync(1015444727, new EleCho.GoCqHttpSdk.Message.CqMessage($"{response}\r\n" +
                        $"求助时间:{DateTime.Now}"));
                    //await session.SendGroupMessageAsync(978734408, new EleCho.GoCqHttpSdk.Message.CqMessage($"{response}\r\n" +
                    //    $"求助时间:{DateTime.Now}"));
                    await session.SendPrivateMessageAsync(3037240065, new EleCho.GoCqHttpSdk.Message.CqMessage($"{response}\r\n" +
                        $"求助时间:{DateTime.Now}"));

                    // 有效响应后退出当前循环
                    response = "null";
                }
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"[SocketServerAsync监听] 从端口 {port} 接收到响应: {response}");
                Console.ForegroundColor = ConsoleColor.White;
            }
            await Task.Delay(3000);
        }
    }
    // 创建方法：调用外部API获取数据



    // 创建方法：调用外部API获取数据
    async Task<string> CallApiAsync(string apiUrl)
    {
        try
        {
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://api.scplist.kr/api/servers/82357"); // 设置 BaseAddress
            // 发送 GET 请求
            HttpResponseMessage response = await httpClient.GetAsync(apiUrl);

            // 确保成功 (状态码 200)
            response.EnsureSuccessStatusCode();

            // 读取响应内容
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody; // 返回响应内容
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"请求失败: {e.Message}");
            return "null"; // 如果请求失败，返回 null
        }
    }

    async Task<string> FetchServerData()
    {
        string apiUrl = "/api/servers"; // 这里使用的相对路径
        string response = await CallApiAsync(apiUrl); // 调用先前定义的 CallApiAsync

        if (response == "null")
        {
            return "服务器数据获取失败。"; // 处理失败情况
        }
        else
        {
            // 假设我们要进一步处理返回的数据，例如，将其打印到控制台
            Console.WriteLine("获取到的服务器数据:");
            Console.WriteLine(response); // 打印响应内容
            return response; // 返回获取到的数据
        }
    }
    async Task<string> FetchOnlineUserCount0(string hello)
    {
        string apiUrl = $"http://api.hmyhfwq.cn:5000/api/ai?prompt={hello}"; // 这里使用的相对路径
        string response = await CallApiAsync(apiUrl); // 调用先前定义的 CallApiAsync

        if (response == "null")
        {
            Console.WriteLine("服务器数据获取失败。");
            return "API调用失败，请前往 https://fwqzt.hmyhfwq.cn/zh 查看API状态。"; // 返回 0 表示获取失败
        }

        // 解析 JSON 响应以提取在线用户数量
        try
        {
            // 使用 System.Text.Json 库解析 JSON
            var jsonDocument = JsonDocument.Parse(response);
            var onlineUserCount = jsonDocument.RootElement.GetProperty("aiResponse").ToString(); // 获取在线用户数量
            return onlineUserCount; // 返回在线用户数量
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析服务器数据失败: {ex.Message}");
            return "解析服务器数据失败: {ex.Message}"; // 解析失败返回 0
        }
    }

    async Task<string> FetchOnlineUserCount_IP(string hello)
    {
        try
        {
            // 使用HttpClient获取IP信息
            using (HttpClient client = new HttpClient())
            {
                string apiUrl = $"https://api.mir6.com/api/ip?ip={hello}&type=json";
                HttpResponseMessage response = await client.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                // 解析JSON并提取相关字段
                JObject json = JObject.Parse(responseBody);
                if (json["code"]?.ToObject<int>() == 200)
                {
                    JObject data = json["data"] as JObject;
                    if (data != null)
                    {
                        var IP = data["ip"]?.ToString() ?? "未知";
                        var 地区 = data["location"]?.ToString() ?? "未知";
                        var 运营商名称 = data["isp"]?.ToString() ?? "未知";
                        var 网络类型 = data["net"]?.ToString() ?? "未知";

                        // 使用Ping获取延迟信息
                        using (Ping ping = new Ping())
                        {
                            PingReply reply = await ping.SendPingAsync(hello);
                            long 延迟 = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;

                            return $"响应IP: {IP}\r\n地区: {地区},\r\n运营商名称: {运营商名称},\r\n网络类型: {网络类型}\r\n延迟: {延迟}ms";
                        }
                    }
                    else
                    {
                        Console.WriteLine("JSON 数据中没有 'data' 对象");
                        return "解析服务器数据失败: JSON 数据中没有 'data' 对象";
                    }
                }
                else
                {
                    Console.WriteLine($"API 返回错误代码: {json["code"]}, 消息: {json["msg"]}");
                    return $"解析服务器数据失败: API 返回错误代码: {json["code"]}, 消息: {json["msg"]}";
                }
            }
        }
        catch (HttpRequestException hre)
        {
            Console.WriteLine($"HTTP 请求失败: {hre.Message}");
            return $"HTTP 请求失败: {hre.Message}";
        }
        catch (PingException pe)
        {
            Console.WriteLine($"Ping 请求失败: {pe.Message}");
            return $"Ping 请求失败: {pe.Message}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析服务器数据失败: {ex.Message}");
            return $"解析服务器数据失败: {ex.Message}";
        }
    }
    async Task 自动重启(int hello)
    {
        //if (异常日志数量 > 10)
        //await SocketServerAsync("127.0.0.1", hello, $"rest", CancellationToken.None);
        //await Task.Delay(5000);
        //警告次数 = 0;
    }
    async Task 回合自动化检测(long hello)
    {
        await session.SendGroupMessageAsync(hello, new EleCho.GoCqHttpSdk.Message.CqMessage($"正在启动异常回合检查程序请稍等..."));
        if (异常日志数量 > 10)
        {
            Console.WriteLine($"异常回合检查，异常日志数量：{异常日志数量}");
            
            await SocketServerAsync("127.0.0.1", 31140, $"ychhe&检查到当前回合异常，将在15秒后自动重启", CancellationToken.None);
            await Task.Delay(15000);
            await SocketServerAsync("127.0.0.1", 31140, $"rest", CancellationToken.None);
            await session.SendGroupMessageAsync(hello, new EleCho.GoCqHttpSdk.Message.CqMessage($"自动化检测完成\r\n检测到异常回合异常日志数量：{异常日志数量}\r\n已自动处理"));
        }
        else
        {
            await Task.Delay(5000);

            await session.SendGroupMessageAsync(hello, new EleCho.GoCqHttpSdk.Message.CqMessage($"自动化检测完成\r\n未检测到异常回合\r\n无需处理"));
        }
        警告次数 = 0;
    }



    // 创建一个新的方法来获取在线人数
    async Task<string> FetchOnlineUserCount()
    {
        string apiUrl = "http://api.hmyhfwq.cn:5000/api/list/77218/"; // 这里使用的相对路径
        string response = await CallApiAsync(apiUrl); // 调用先前定义的 CallApiAsync

        if (response == "null")
        {
            Console.WriteLine("服务器数据获取失败。");
            return "API调用失败，请前往 https://api.hmyhfwq.cn/ 查看API状态。"; // 返回 0 表示获取失败
        }

        // 解析 JSON 响应以提取在线用户数量
        try
        {
            // 使用 System.Text.Json 库解析 JSON
            var jsonDocument = JsonDocument.Parse(response);
            var onlineUserCount = jsonDocument.RootElement.GetProperty("players").ToString(); // 获取在线用户数量
            return onlineUserCount; // 返回在线用户数量
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析服务器数据失败: {ex.Message}");
            return "解析服务器数据失败: {ex.Message}"; // 解析失败返回 0
        }

    }
    HttpClient client = new HttpClient();
    async Task<string> FetchOnlineUserCount2()
    {
        string apiUrl = "https://api.scplist.kr/api/servers/83161"; // 这里使用的相对路径
        string response = await CallApiAsync(apiUrl); // 调用先前定义的 CallApiAsync

        if (response == "null")
        {
            Console.WriteLine("服务器数据获取失败。");
            return "0"; // 返回 0 表示获取失败
        }

        // 解析 JSON 响应以提取在线用户数量
        try
        {
            // 使用 System.Text.Json 库解析 JSON
            var jsonDocument = JsonDocument.Parse(response);
            var onlineUserCount = jsonDocument.RootElement.GetProperty("players").ToString(); // 获取在线用户数量
            return onlineUserCount; // 返回在线用户数量
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析服务器数据失败: {ex.Message}");
            return "0"; // 解析失败返回 0
        }
    }
    static async Task<string> CheckWordsHttpAsync0(string content, string extstr = "")
    {
        try
        {
            string response = "";
            response = await CheckWordsHttpAsync(content, extstr);
            var jsonDocument = JsonDocument.Parse(response);
            var wordList = jsonDocument.RootElement.GetProperty("word_list").EnumerateArray();

            // 假设我们只需要第一个敏感词的信息
            if (wordList.Any())
            {
                var firstWord = wordList.First();
                var onlineUserCount0 = firstWord.GetProperty("category").ToString();
                var onlineUserCount1 = firstWord.GetProperty("level").ToString();

                return $"当前信息包含{onlineUserCount0} {onlineUserCount1}敏感词。";
            }
            else
            {
                return "当前信息不包含敏感词。";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析服务器数据失败: {ex.Message}");
            string response = "";
            response = await CheckWordsHttpAsync(content, extstr);
            Console.WriteLine($"{response}");
            return $"解析服务器数据失败: {ex.Message}";
        }

    }


    static async Task<string> CheckWordsHttpAsync(string content, string extstr = "")
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                var requestBody = new
                {
                    content = content,
                    extstr = extstr
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var contentToSend = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("http://localhost:8080/wordscheck", contentToSend);

                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                return responseBody;
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"请求失败: {ex.Message}");
            return "请求失败: {ex.Message}";
        }
    }

    async Task<string> MC0()
    {
        HttpClient client = new HttpClient();

        // 设置API的URL
        string apiUrl = $"https://motdbe.blackbe.work/api/java?host=180.188.21.48:2345";

        // 发送GET请求
        HttpResponseMessage response = await client.GetAsync(apiUrl);

        try
        {
            // 确保请求成功
            response.EnsureSuccessStatusCode();

            // 读取响应内容
            string responseBody = await response.Content.ReadAsStringAsync();

            // 解析JSON并提取相关字段
            JObject json = JObject.Parse(responseBody);
            var onlineUserCount = json["online"].ToString(); // 获取在线用户数量
            var s1 = json["max"].ToString(); // 获取最大用户数量
            var s3 = json["delay"].ToString(); // 获取延迟
            if (onlineUserCount == "0")
            {
                return ""; // 返回 0 表示获取失败
            }
            string s2 = $"幻梦银河 --- 乌托邦3.2 \r\n在线人数: {onlineUserCount}/{s1} \r\n延迟:{s3}ms";
            return s2; // 返回在线用户数量
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"请求失败: {ex.Message}");
            return "0"; // 请求失败返回 0
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析服务器数据失败: {ex.Message}");
            return "0"; // 解析失败返回 0
        }
    }

    await session.RunAsync();
}


