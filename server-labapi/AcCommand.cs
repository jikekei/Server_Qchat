using System;
using CommandSystem;
using Player = LabApi.Features.Wrappers.Player;
using Server = LabApi.Features.Wrappers.Server;

namespace SocketServer
{
    /// <summary>
    /// 客户端指令 <c>.ac &lt;内容&gt;</c>：把玩家输入的内容推到 QQ 群，并向在线管理员广播。
    /// <para>
    /// 迁移说明：指令注册方式<b>没有变</b>——LabAPI 仍在插件装载时扫描插件程序集里
    /// 带 <c>[CommandHandler]</c> 特性的 <see cref="ICommand"/> 并自动注册
    /// （调用顺序：LoadConfigs → RegisterCommands → Enable），
    /// 因此这里既不需要手动注册，也不需要改特性。
    /// </para>
    /// </summary>
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
                // EXILED: p.Broadcast(10, text) → LabAPI: p.SendBroadcast(text, 10, flags, 是否清空旧广播)
                foreach (var p in Player.List)
                {
                    if (p.RemoteAdminAccess)
                    {
                        p.SendBroadcast($"[AC推送] 玩家 {name} 发送了：\n{content}", 10, Broadcast.BroadcastFlags.Normal, false);
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
