using Exiled.API.Features;
using Exiled.API.Interfaces;
using PlayerRoles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Log = Exiled.API.Features.Log;

namespace SocketServer
{
    public sealed class Config : IConfig
    {
        [Description("设置为起始TCP监听端口，若被占用将自动递增探测")]
        public int TcpPort { get; set; } = 10087;

        [Description("设置为服务器IP 一般不用改")]
        public string IP { get; set; } = "127.0.0.1";

        [Description("设置为服务器名称（如：1服、测试服等）")]
        public string ServerName { get; set; } = "1服";

        [Description("显示DisplayMode为1时显的东西")]
        public string ContentText { get; set; } = "";

        [Description("0显示时间 1显示ContentText里面的东西 2空白")]
        public int DisplayMode { get; set; } = 2;

        [Description("QQ机器人后台监听服务的IP")]
        public string BotIP { get; set; } = "127.0.0.1";

        [Description("QQ机器人后台监听服务的端口号")]
        public int BotPort { get; set; } = 10088;

        [Description("安全验证Token，须与机器人端的Token一致")]
        public string AuthToken { get; set; } = "QchaSecret_123";

        [Description("排序权重，>0时按此排序，=0时自动排序")]
        public int SortOrder { get; set; } = 0;

        [Description("机器人回连本服务器使用的IP地址（跨机部署须手动配置，同机留空即可）")]
        public string ConnectHost { get; set; } = "";

        public bool IsEnabled { get; set; } = true;
        public bool Debug { get; set; } = false;
    }

    public sealed class Main : Plugin<Config>
    {
        public static readonly Dictionary<RoleTypeId, string> TranslateOfRoleType = new Dictionary<RoleTypeId, string>()
        {
            {RoleTypeId.NtfPrivate,"九尾狐列兵" },
            {RoleTypeId.NtfCaptain,"九尾狐指挥官" },
            {RoleTypeId.NtfSergeant,"九尾狐中士" },
            {RoleTypeId.NtfSpecialist,"九尾狐收容专家" },
            {RoleTypeId.FacilityGuard,"设施保安" },
            {RoleTypeId.ChaosConscript,"混沌征召兵" },
            {RoleTypeId.ChaosMarauder,"混沌掠夺者" },
            {RoleTypeId.ChaosRepressor,"混沌压制者" },
            {RoleTypeId.ChaosRifleman,"混沌步枪手" },
            {RoleTypeId.Scp096,"SCP-096" },
            {RoleTypeId.Scp049,"SCP-049" },
            {RoleTypeId.Scp173,"SCP-173" },
            {RoleTypeId.Scp939,"SCP-939" },
            {RoleTypeId.Scp106,"SCP-106" },
            {RoleTypeId.Scp0492,"SCP-049-2" },
            {RoleTypeId.Scp079,"SCP-079" },
            {RoleTypeId.ClassD,"D级人员" },
            {RoleTypeId.Scientist,"科学家" },
            {RoleTypeId.Tutorial,"教程角色" },
            {RoleTypeId.Overwatch,"监管模式" },
            {RoleTypeId.CustomRole,"本地角色？" },
            {RoleTypeId.Spectator,"观察者" },
            {RoleTypeId.Filmmaker,"导演模式" },
            {RoleTypeId.None,"空" },
        };

        public override string Author => "Fantasy Galaxy";
        public override string Name => "Server_Qcha";
        public override Version Version => new Version(1, 3, 0);

        public static Main Instance { get; private set; }

        private TcpCommandServer _server;
        private bool _started;
        private int _actualPort;
        private string _resolvedHost;

        private CancellationTokenSource _heartbeatCts;
        private Task _heartbeatTask;

        private readonly object _acLock = new object();
        private string _acPayload = "null";

        public override void OnEnabled()
        {
            Instance = this;
            Log.Info("[Server_Qcha] 插件已加载 v1.3.0，开始初始化...");
            Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
            StopServer();
            Instance = null;
            base.OnDisabled();
        }

        private void OnWaitingForPlayers()
        {
            if (_started)
                return;

            _started = true;

            // 1. 解析回连主机地址
            _resolvedHost = Config.ConnectHost;
            if (string.IsNullOrEmpty(_resolvedHost))
            {
                if (Config.IP != "0.0.0.0")
                {
                    _resolvedHost = Config.IP;
                }
                else
                {
                    Log.Info("[Server_Qcha] ConnectHost 未配置且监听 IP 为 0.0.0.0，尝试自动检测本机 IP...");
                    try
                    {
                        _resolvedHost = GetLocalIPv4Address();
                        Log.Info($"[Server_Qcha] 自动检测本机 IP: {_resolvedHost}");
                    }
                    catch
                    {
                        _resolvedHost = "127.0.0.1";
                        Log.Warn("[Server_Qcha] 无法自动检测本机 IP，回退使用 127.0.0.1。跨机部署请手动配置 connect_host");
                    }
                }
            }

            Log.Info($"[Server_Qcha] 配置 → BasePort={Config.TcpPort}, ServerName={Config.ServerName}, SortOrder={Config.SortOrder}, ConnectHost={_resolvedHost}, BotEndpoint={Config.BotIP}:{Config.BotPort}");

            var dispatcher = new CommandDispatcher(
                serverName: Config.ServerName,
                contentText: () => Config.ContentText,
                displayMode: () => Config.DisplayMode,
                debug: () => Config.Debug,
                consumeAcPayload: ConsumeAcPayload);

            // 2. 合并探测与绑定为原子操作
            _actualPort = Config.TcpPort;
            bool success = false;
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    Log.Debug($"[Server_Qcha] 尝试绑定端口 {_actualPort}...");
                    _server = new TcpCommandServer(Config.IP, _actualPort, dispatcher.Dispatch, () => Config.AuthToken);
                    _server.Start();
                    success = true;
                    Log.Info($"[Server_Qcha] TCP 命令服务已启动 → {Config.IP}:{_actualPort} (从 BasePort {Config.TcpPort} 探测)");
                    break;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    Log.Debug($"[Server_Qcha] 端口 {_actualPort} 被占用，尝试 {_actualPort + 1}...");
                    _actualPort++;
                }
            }

            if (!success)
            {
                Log.Error($"[Server_Qcha] 端口探测失败：从 {Config.TcpPort} 到 {Config.TcpPort + 99} 均被占用，TCP 服务未启动");
                _started = false;
                return;
            }

            // 3. 启动动态注册与心跳任务
            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = Task.Run(() => HeartbeatLoop(_heartbeatCts.Token));
        }

        private async Task HeartbeatLoop(CancellationToken ct)
        {
            // 首次注册
            Log.Info($"[Server_Qcha] 向机器人 {Config.BotIP}:{Config.BotPort} 发送注册包 → name={Config.ServerName}, connectHost={_resolvedHost}, port={_actualPort}, gamePort={Server.Port}, sortOrder={Config.SortOrder}");
            try
            {
                bool registered = await BotNotificationClient.SendNotificationAsync(
                    Config.BotIP, Config.BotPort, Config.AuthToken, "register",
                    Config.ServerName, _resolvedHost, _actualPort, Server.Port, Config.SortOrder);

                if (registered)
                {
                    Log.Info("[Server_Qcha] 注册成功，机器人已确认");
                }
                else
                {
                    Log.Warn("[Server_Qcha] 注册包发送后 5 秒未收到机器人响应，将在心跳周期中重试");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Server_Qcha] 注册失败：无法连接到机器人 {Config.BotIP}:{Config.BotPort} → {ex.Message}");
            }

            int normalInterval = 30; // 30 秒
            int consecutiveFailures = 0;

            while (!ct.IsCancellationRequested)
            {
                // 等待下一个心跳间隔
                int delaySec = normalInterval;
                if (consecutiveFailures > 0)
                {
                    delaySec = Math.Min(normalInterval * (int)Math.Pow(2, consecutiveFailures), 300);
                }

                try
                {
                    await Task.Delay(delaySec * 1000, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (ct.IsCancellationRequested)
                    break;

                Log.Debug($"[Server_Qcha] 心跳发送 → {Config.BotIP}:{Config.BotPort} (port={_actualPort})");
                bool ok = false;
                string errorMsg = "";
                try
                {
                    ok = await BotNotificationClient.SendNotificationAsync(
                        Config.BotIP, Config.BotPort, Config.AuthToken, "heartbeat",
                        Config.ServerName, _resolvedHost, _actualPort, Server.Port, Config.SortOrder);
                }
                catch (Exception ex)
                {
                    errorMsg = ex.Message;
                }

                if (ok)
                {
                    if (consecutiveFailures >= 10)
                    {
                        Log.Info("[Server_Qcha] 心跳恢复成功，退出慢速重试模式");
                    }
                    consecutiveFailures = 0;
                    Log.Debug("[Server_Qcha] 心跳成功，连续失败计数重置");
                }
                else
                {
                    consecutiveFailures++;
                    int nextBackoff = Math.Min(normalInterval * (int)Math.Pow(2, consecutiveFailures), 300);
                    if (consecutiveFailures < 10)
                    {
                        Log.Warn($"[Server_Qcha] 心跳失败 (第 {consecutiveFailures}/10 次): {errorMsg}，{nextBackoff}秒后重试");
                    }
                    else
                    {
                        Log.Error($"[Server_Qcha] 心跳连续失败 {consecutiveFailures} 次，进入慢速重试模式（每 300 秒一次）。请检查机器人 {Config.BotIP}:{Config.BotPort} 是否在线");
                    }
                }
            }
        }

        private void StopServer()
        {
            // 1. 发送注销包
            if (_started && !string.IsNullOrEmpty(_resolvedHost))
            {
                Log.Info($"[Server_Qcha] 插件关闭，发送注销包 → port={_actualPort}");
                try
                {
                    // 同步等待注销结果，超时 5 秒
                    var unregisterTasks = BotNotificationClient.SendNotificationAsync(
                        Config.BotIP, Config.BotPort, Config.AuthToken, "unregister",
                        Config.ServerName, _resolvedHost, _actualPort, Server.Port, Config.SortOrder);
                    
                    if (unregisterTasks.Wait(5000) && unregisterTasks.Result)
                    {
                        Log.Info("[Server_Qcha] 注销成功，机器人已确认");
                    }
                    else
                    {
                        Log.Warn("[Server_Qcha] 注销包发送失败: 响应超时（机器人端将在心跳超时后自动清理）");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"[Server_Qcha] 注销包发送失败: {ex.Message}（机器人端将在心跳超时后自动清理）");
                }
            }

            // 2. 停止心跳任务
            if (_heartbeatCts != null)
            {
                try
                {
                    _heartbeatCts.Cancel();
                    _heartbeatTask?.Wait(2000);
                }
                catch { }
                finally
                {
                    _heartbeatCts.Dispose();
                    _heartbeatCts = null;
                    _heartbeatTask = null;
                }
                Log.Info("[Server_Qcha] 心跳循环已停止");
            }

            // 3. 关闭 TCP 命令服务
            try
            {
                _server?.Stop();
                _server = null;
                Log.Info("[Server_Qcha] TCP 命令服务已停止");
            }
            catch (Exception ex)
            {
                Log.Error("SocketServer stop failed: " + ex);
            }
            finally
            {
                _started = false;
            }
        }

        private static string GetLocalIPv4Address()
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        private string ConsumeAcPayload()
        {
            lock (_acLock)
            {
                var payload = _acPayload;
                _acPayload = "null";
                return payload;
            }
        }

        public void SetAcPayload(string payload)
        {
            if (payload == null)
                payload = "null";

            lock (_acLock)
            {
                _acPayload = payload;
            }
        }
    }
}
