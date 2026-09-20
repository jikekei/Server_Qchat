using System;
using System.Linq;
using System.Text;
using PlayerRoles;
using Round = LabApi.Features.Wrappers.Round;
using Server = LabApi.Features.Wrappers.Server;
using Player = LabApi.Features.Wrappers.Player;
using RespawnWaves = LabApi.Features.Wrappers.RespawnWaves;
using RoundRestart = RoundRestarting.RoundRestart;

namespace SocketServer
{
    /// <summary>
    /// 命令通道的业务分发器。机器人端发来的每个短连接请求都会走到这里。
    /// <para>
    /// 协议：命令全小写，参数以 <c>&amp;</c> 分隔。参见仓库根目录 <c>通信协议文档.md</c>。
    /// </para>
    /// </summary>
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
            var players = Player.List.Where(p => !p.Nickname.Equals("Dedicated Server", StringComparison.OrdinalIgnoreCase) && !p.Nickname.StartsWith("Dedicated Server@", StringComparison.OrdinalIgnoreCase)).ToList();
            int online = players.Count;
            int admins = players.Count(p => p.RemoteAdminAccess);

            var sb = new StringBuilder();
            sb.Append(_serverName);
            sb.Append("\r\n在线人数:").Append(online).Append("/").Append(Server.MaxPlayers);
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
            sb.Append("\r\nDD人数:").Append(Player.List.Count(p => p.Role == RoleTypeId.ClassD));
            sb.Append("\r\n博士人数:").Append(Player.List.Count(p => p.Role == RoleTypeId.Scientist)).Append("人");
            sb.Append("\r\nSCP人数:").Append(Player.List.Count(p => p.Team == Team.SCPs));
            // EXILED: Round.ElapsedTime → LabAPI: Round.Duration
            sb.Append("\r\n回合进行时间：").Append(Round.Duration.ToString(@"hh\:mm\:ss"));
            // EXILED: Round.UptimeRounds → LabAPI 无包装器，直接取游戏静态计数器
            sb.Append("\r\n回合次数：").Append(RoundRestart.UptimeRounds);
            // EXILED: Respawn.ProtectionTime → LabAPI: 两条刷新波的时间取较小者
            sb.Append("\r\n下一波刷新时间：").Append(GetNextWaveSeconds().ToString("0.0")).Append("秒");
            sb.Append("\r\n查询时间").Append(DateTime.Now);
            sb.Append("\r\n\r\n").Append(_contentText());
            return sb.ToString();
        }

        /// <summary>
        /// 取九尾狐/混沌刷新波中更早到来的那一个剩余时间（秒）。
        /// </summary>
        private static float GetNextWaveSeconds()
        {
            float mtf = RespawnWaves.PrimaryMtfWave?.TimeLeft ?? float.MaxValue;
            float chaos = RespawnWaves.PrimaryChaosWave?.TimeLeft ?? float.MaxValue;
            float next = Math.Min(mtf, chaos);
            return next == float.MaxValue ? 0f : next;
        }

        private string HandleStart()
        {
            if (Round.IsRoundStarted)
                return "回合已经开启了";

            Round.Start();
            return "回合启动成功";
        }

        private string HandleRest()
        {
            // Keep legacy guard: only allow restart early in the round.
            if (Round.Duration.TotalSeconds >= 60)
                return "拒绝：回合开始超过60秒";

            // EXILED: Round.Restart(false) → LabAPI: (是否快速重启, 是否覆盖回合结束动作, 覆盖后的动作)
            // overrideRestartAction = false 表示沿用服务器自身配置的回合结束动作，等价于旧行为。
            Round.Restart(false, false, ServerStatic.NextRoundAction.DoNothing);
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

            // EXILED: Map.Broadcast(15, text) → LabAPI: Server.SendBroadcast(text, 15, flags, 是否清空旧广播)
            Server.SendBroadcast("[管理员消息]" + message, 15, Broadcast.BroadcastFlags.Normal, false);
            return "bc发送成功";
        }

        private string HandleAutoBroadcast(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "ychhe参数不足";

            Round.IsLobbyLocked = true;
            Server.SendBroadcast("[该信息为自动发送]" + message, 17, Broadcast.BroadcastFlags.Normal, false);
            return "bc发送成功";
        }

        private string HandleList()
        {
            var sb = new StringBuilder();
            foreach (var p in Player.List)
            {
                if (p.Nickname.Equals("Dedicated Server", StringComparison.OrdinalIgnoreCase) || p.Nickname.StartsWith("Dedicated Server@", StringComparison.OrdinalIgnoreCase))
                    continue;
                // EXILED: p.Id → LabAPI: p.PlayerId
                sb.Append("\r\n").Append(p.Nickname).Append("-").Append(p.PlayerId);
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

            var player = Player.List.FirstOrDefault(x => x.PlayerId.ToString() == id);
            if (player == null)
                return "踢出失败,未找到指定ID的玩家";

            try
            {
                // Avoid returning IP in response (PII). Keep message minimal.
                // 注意：LabAPI 的签名为 Ban(reason, duration)，与 EXILED 的 Ban(duration, reason) 参数顺序相反。
                player.Ban(reason, duration);
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
