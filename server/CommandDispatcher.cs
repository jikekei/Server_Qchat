using Exiled.API.Features;
using System;
using System.Linq;
using System.Text;
using Player = Exiled.API.Features.Player;
using Respawn = Exiled.API.Features.Respawn;
using Round = Exiled.API.Features.Round;
using Server = Exiled.API.Features.Server;

namespace SocketServer
{
    internal sealed class CommandDispatcher
    {
        private readonly string _serverName;
        private readonly Func<string> _contentText;
        private readonly Func<int> _displayMode;
        private readonly Func<bool> _debug;
        private readonly Func<string> _consumeAcPayload;

        public CommandDispatcher(
            string serverName,
            Func<string> contentText,
            Func<int> displayMode,
            Func<bool> debug,
            Func<string> consumeAcPayload)
        {
            _serverName = serverName ?? "";
            _contentText = contentText ?? (() => "");
            _displayMode = displayMode ?? (() => 2);
            _debug = debug ?? (() => false);
            _consumeAcPayload = consumeAcPayload ?? (() => "null");
        }

        public string Dispatch(string request)
        {
            if (string.IsNullOrWhiteSpace(request))
                return "empty command";

            // Exact commands
            if (request == "ac")
                return HandleAc();
            if (request == "cx")
                return HandleCx();
            if (request == "info")
                return HandleInfo();
            if (request == "start")
                return HandleStart();
            if (request == "rest")
                return HandleRest();
            if (request == "allrest")
                return HandleAllRest();
            if (request == "list")
                return HandleList();

            // Parameterized commands: bc&..., ychhe&..., kick&...
            if (request.StartsWith("bc&", StringComparison.Ordinal))
                return HandleBroadcast(request.Substring(3));
            if (request.StartsWith("ychhe&", StringComparison.Ordinal))
                return HandleAutoBroadcast(request.Substring(6));
            if (request.StartsWith("kick&", StringComparison.Ordinal))
                return HandleKick(request);

            return "unknown command";
        }

        private string HandleAc()
        {
            var payload = _consumeAcPayload();
            return "来自服务器:\r\n" + _serverName + payload;
        }

        private string HandleCx()
        {
            var players = Player.List.ToList();
            int online = players.Count;
            int admins = players.Count(p => p.RemoteAdminAccess);

            var sb = new StringBuilder();
            sb.Append(_serverName);
            sb.Append("\r\n在线人数:").Append(online).Append("/").Append(Server.MaxPlayerCount);
            sb.Append("\r\n在线管理:").Append(admins).Append("人");

            int mode = _displayMode();
            if (mode == 2)
                sb.Append("\r\n");
            else if (mode == 0)
                sb.Append("\r\n查询时间 ").Append(DateTime.Now);
            else if (mode == 1)
                sb.Append("\r\n").Append(_contentText());

            return sb.ToString();
        }

        private string HandleInfo()
        {
            var sb = new StringBuilder();
            sb.Append("服务器#").Append(_serverName).Append(" - 查询Success!!");
            sb.Append("\r\nDD人数:").Append(Player.Get(PlayerRoles.RoleTypeId.ClassD).Count());
            sb.Append("\r\n博士人数:").Append(Player.Get(PlayerRoles.RoleTypeId.Scientist).Count()).Append("人");
            sb.Append("\r\nSCP人数:").Append(Player.Get(PlayerRoles.Team.SCPs).Count());
            sb.Append("\r\n回合进行时间：").Append(Round.ElapsedTime);
            sb.Append("\r\n回合次数：").Append(Round.UptimeRounds);
            sb.Append("\r\n下一波刷新时间：").Append(Respawn.ProtectionTime);
            sb.Append("\r\n查询时间").Append(DateTime.Now);
            sb.Append("\r\n\r\n").Append(_contentText());
            return sb.ToString();
        }

        private string HandleStart()
        {
            if (Round.IsStarted)
                return "回合已经开启了";

            Round.Start();
            return "回合启动成功";
        }

        private string HandleRest()
        {
            // Keep legacy guard: only allow restart early in the round.
            if (Round.ElapsedTime.TotalSeconds >= 60)
                return "拒绝：回合开始超过60秒";

            Round.Restart(false);
            return "回合重启成功";
        }

        private string HandleAllRest()
        {
            Server.Restart();
            return "服务器重启成功";
        }

        private string HandleBroadcast(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "bc参数不足";

            Map.Broadcast(15, "[管理员消息]" + message);
            return "bc发送成功";
        }

        private string HandleAutoBroadcast(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "ychhe参数不足";

            Round.IsLobbyLocked = true;
            Map.Broadcast(17, "[该信息为自动发送]" + message);
            return "bc发送成功";
        }

        private string HandleList()
        {
            var sb = new StringBuilder();
            foreach (var p in Player.List)
            {
                sb.Append("\r\n").Append(p.Nickname).Append("-").Append(p.Id);
            }
            return sb.ToString();
        }

        private string HandleKick(string request)
        {
            // Protocol: kick&<id>&<reason>&<time>
            var parts = request.Split('&');
            if (parts.Length < 4)
                return "kick参数不足";

            string id = parts[1];
            string timeRaw = parts[parts.Length - 1];
            string reason = string.Join("&", parts.Skip(2).Take(parts.Length - 3));

            int duration;
            if (!int.TryParse(timeRaw, out duration))
                return "kick时间参数必须是整数";

            var player = Player.List.FirstOrDefault(x => x.Id.ToString() == id);
            if (player == null)
                return "踢出失败,未找到指定ID的玩家";

            try
            {
                // Avoid returning IP in response (PII). Keep message minimal.
                player.Ban(duration, reason);
                return "封禁成功\r\n封禁ID:" + player.UserId + "\r\n封禁时间:" + duration + "\r\n原因:" + reason;
            }
            catch (Exception ex)
            {
                if (_debug())
                    return "封禁失败: " + ex.Message;
                return "封禁失败";
            }
        }
    }
}

