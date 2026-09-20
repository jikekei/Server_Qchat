using System;
using CommandSystem;
using Exiled.API.Features;

namespace SocketServer
{
    [CommandHandler(typeof(ClientCommandHandler))]
    public class AcCommand : ICommand
    {
        public string Command => "ac";

        public string[] Aliases => Array.Empty<string>();

        public string Description => "发送内容到QQ群聊";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count == 0)
            {
                response = "用法: .ac <内容>";
                return false;
            }

            string content = string.Join(" ", arguments);
            var player = Player.Get(sender);
            string name = player != null ? player.Nickname : "控制台";

            var config = Main.Instance?.Config;
            if (config != null)
            {
                string truncatedContent = content.Length > 50 ? content.Substring(0, 50) : content;
                Log.Info($"[Server_Qcha] .ac 指令 → 玩家 [{name}] 发送: {truncatedContent}...");

                string message = $"来自服务器 [{config.ServerName}]:\n玩家 [{name}] 发送了：{content}";
                BotNotificationClient.SendNotification(config.BotIP, config.BotPort, config.AuthToken, "ac", message);

                // 向在线的管理员广播该消息
                foreach (var p in Player.List)
                {
                    if (p.RemoteAdminAccess)
                    {
                        p.Broadcast(10, $"[AC推送] 玩家 {name} 发送了：\n{content}");
                    }
                }

                response = "消息已发送至QQ群";
                return true;
            }

            response = "发送失败：插件配置未就绪";
            return false;
        }
    }
}
