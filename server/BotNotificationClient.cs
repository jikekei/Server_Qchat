using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Log = Exiled.API.Features.Log;

namespace SocketServer
{
    public static class BotNotificationClient
    {
        // 供一次性推送（如 .ac）使用，不堵塞主线程
        public static void SendNotification(string host, int port, string token, string type, string message)
        {
            Task.Run(async () =>
            {
                try
                {
                    string json = $"{{\"type\":\"{type}\",\"data\":{{\"message\":\"{EscapeJson(message)}\"}}}}";
                    bool ok = await SendNotificationRawAsync(host, port, token, json);
                    if (ok)
                    {
                        Log.Debug("[Server_Qcha] .ac 推送成功，机器人已确认");
                    }
                    else
                    {
                        Log.Warn("[Server_Qcha] .ac 推送失败");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"[Server_Qcha] .ac 推送异常: {ex.Message}");
                }
            });
        }

        // 供心跳和注册等需要结果的流程同步等待使用
        public static async Task<bool> SendNotificationAsync(string host, int port, string token, string type, string name, string connectHost, int commandPort, int gamePort, int sortOrder)
        {
            string json;
            if (type == "unregister")
            {
                json = $"{{\"type\":\"unregister\",\"data\":{{\"connectHost\":\"{EscapeJson(connectHost)}\",\"port\":{commandPort}}}}}";
            }
            else
            {
                json = $"{{\"type\":\"{type}\",\"data\":{{\"name\":\"{EscapeJson(name)}\",\"connectHost\":\"{EscapeJson(connectHost)}\",\"port\":{commandPort},\"gamePort\":{gamePort},\"sortOrder\":{sortOrder}}}}}";
            }

            return await SendNotificationRawAsync(host, port, token, json);
        }

        private static async Task<bool> SendNotificationRawAsync(string host, int port, string token, string jsonPayload)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    var delayTask = Task.Delay(5000); // 5s timeout
                    var winner = await Task.WhenAny(connectTask, delayTask);
                    if (winner != connectTask)
                    {
                        return false;
                    }

                    using (var stream = client.GetStream())
                    {
                        string payload = string.IsNullOrEmpty(token) ? jsonPayload : $"{token}||{jsonPayload}";
                        byte[] bytes = Encoding.UTF8.GetBytes(payload);
                        await stream.WriteAsync(bytes, 0, bytes.Length);

                        // 半断开发送端，告诉机器人数据发完了，但保留接收端以读取响应
                        client.Client.Shutdown(SocketShutdown.Send);

                        var buffer = new byte[128];
                        var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
                        var readWinner = await Task.WhenAny(readTask, Task.Delay(5000));
                        if (readWinner != readTask)
                        {
                            return false;
                        }

                        int readCount = await readTask;
                        if (readCount <= 0)
                        {
                            return false;
                        }

                        string response = Encoding.UTF8.GetString(buffer, 0, readCount).Trim();
                        return response == "OK";
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
        }
    }
}
