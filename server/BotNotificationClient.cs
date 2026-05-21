using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Log = Exiled.API.Features.Log;

namespace SocketServer
{
    public static class BotNotificationClient
    {
        public static void SendNotification(string host, int port, string token, string message)
        {
            Task.Run(async () =>
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
                            Log.Warn($"连接QQ机器人通知服务超时: {host}:{port}");
                            return;
                        }

                        using (var stream = client.GetStream())
                        {
                            string payload = string.IsNullOrEmpty(token) ? message : $"{token}||{message}";
                            byte[] bytes = Encoding.UTF8.GetBytes(payload);
                            await stream.WriteAsync(bytes, 0, bytes.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"发送通知到QQ机器人失败: {ex.Message}");
                }
            });
        }
    }
}
