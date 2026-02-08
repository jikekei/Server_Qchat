using Exiled.API.Features;
using Exiled.API.Interfaces;
using PlayerRoles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Log = Exiled.API.Features.Log;

namespace SocketServer
{
    public sealed class Config : IConfig
    {
        [Description("设置为服务器端口号")]
        public int TcpPort { get; set; } = 10087;

        [Description("设置为服务器IP 一般不用改")]
        public string IP { get; set; } = "127.0.0.1";

        [Description("设置为服务器名称（如：1服、测试服等）")]
        public string ServerName { get; set; } = "1服";

        [Description("显示DisplayMode为1时显的东西")]
        public string ContentText { get; set; } = "";

        [Description("0显示时间 1显示ContentText里面的东西 2空白")]
        public int DisplayMode { get; set; } = 2;

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
        public override Version Version => new Version(1, 2, 0);

        private TcpCommandServer _server;
        private bool _started;

        private readonly object _acLock = new object();
        private string _acPayload = "null";

        public override void OnEnabled()
        {
            Log.Info("Loaded plugin, register events...");
            Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
            StopServer();
            base.OnDisabled();
        }

        private void OnWaitingForPlayers()
        {
            // WaitingForPlayers fires multiple times across server lifetime. Start once.
            if (_started)
                return;

            _started = true;

            try
            {
                var dispatcher = new CommandDispatcher(
                    serverName: Config.ServerName,
                    contentText: () => Config.ContentText,
                    displayMode: () => Config.DisplayMode,
                    debug: () => Config.Debug,
                    consumeAcPayload: ConsumeAcPayload);

                _server = new TcpCommandServer(Config.IP, Config.TcpPort, dispatcher.Dispatch);
                _server.Start();
                Log.Info($"SocketServer started at {Config.IP}:{Config.TcpPort}");
            }
            catch (Exception ex)
            {
                Log.Error("SocketServer start failed: " + ex);
                _started = false;
            }
        }

        private void StopServer()
        {
            try
            {
                _server?.Stop();
                _server = null;
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

        private string ConsumeAcPayload()
        {
            lock (_acLock)
            {
                var payload = _acPayload;
                _acPayload = "null";
                return payload;
            }
        }

        // If you later need to push an "ac" payload from other parts of the plugin,
        // call SetAcPayload(...). Not used by default.
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
