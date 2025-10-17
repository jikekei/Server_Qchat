using CommandSystem;
using Exiled.API.Features;
using Exiled.API.Interfaces;
using PlayerRoles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Log = Exiled.API.Features.Log;
using Player = Exiled.API.Features.Player;
using Respawn = Exiled.API.Features.Respawn;
using Round = Exiled.API.Features.Round;
using Server = Exiled.API.Features.Server;



namespace SocketServer
{
   
    public class Config : IConfig
    {
        [Description("设置为服务器端口号")]
        public int TcpPort { get; set; } = 10087;
        [Description("设置为服务器IP 一般不用改")]
        public string IP { get; set; } = "127.0.0.1";
        /// <summary>
        /// 设置为服务器名称（如：1服、测试服等）
        /// </summary>
        [Description("设置为服务器名称")]
        public string ServerName { get; set; } = "1服";

        /// <summary>
        /// 显示的内容文本（与显示模式相关）
        /// </summary>
        [Description("显示DisplayMode为1时显的东西")]
        public string ContentText { get; set; } = "";

        /// <summary>
        /// 显示模式：0=时间，1=内容，2=空白
        /// </summary>
        [Description("0显示时间 1显示ContentText里面的东西 2空白")]
        public int DisplayMode { get; set; } = 2;

        public bool IsEnabled { get; set; } = true;
        public bool Debug { get; set; } = false;
    }
    public class Main : Plugin<Config>
    {
        public static Dictionary<RoleTypeId, string> TranslateOfRoleType = new Dictionary<RoleTypeId, string>()
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
        { RoleTypeId
            .Scp3114, "SCP-3114" },
    };
        public override string Author => "Fantasy Galaxy定制插件[3037240065]";
        public override string Name => "CX查询插件";
        public override Version Version => new Version(1, 1, 1);
        public static Main Maina;
        public string s1 = "null";
        public override void OnEnabled()
        {
            Maina = this;
            Log.Info("Loaded plugin, register events...");
            Exiled.Events.Handlers.Server.WaitingForPlayers += Wait;
            base.OnEnabled();
        }

        public void Wait()
        {

            Log.Info("SocketConnect ! Task.Run(delegate ()  ");

            Task.Run(delegate ()
            {
                try
                {
                    Log.Info("SocketConnect ! version 1.1.1");
                    int port2 = Maina.Config.TcpPort;
                    IPAddress any = IPAddress.Parse(Maina.Config.IP);
                    var ReceiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IPEndPoint ipendPoint = new IPEndPoint(any, port2);
                    ReceiveSocket.Bind(new IPEndPoint(any, port2));
                    ReceiveSocket.Listen(10);
                    // 创建 PerformanceCounter 实例来获取CPU使用率
                    PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    PerformanceCounter memoryCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                    while (true)
                    {
                        Socket socket = ReceiveSocket.Accept();
                        byte[] array = new byte[1024];
                        int count = socket.Receive(array);
                        string returna = Encoding.UTF8.GetString(array, 0, count);
                        if (returna == "ac")
                        {
                            string aaa = Maina.s1;
                            byte[] bytes = Encoding.UTF8.GetBytes("来自服务器:\r\n" + Maina.Config.ServerName + aaa);
                            socket.Send(bytes);
                            socket.Close();
                            s1 = "null";
                        }
                        if (returna == "cx")
                        {
                            float cpuUsage = cpuCounter.NextValue(); float memoryUsage = memoryCounter.NextValue();
                            string a = string.Empty;
                            a += $"{Maina.Config.ServerName}";
                            a += $"\r\n在线人数:{Player.List.Count().ToString()}/{Server.MaxPlayerCount}";
                            a += $"\r\n在线管理:{Player.List.ToList().FindAll(x => x.RemoteAdminAccess).Count}人";
                            if (Maina.Config.DisplayMode == 2)
                                a += $"\r\n";
                            if (Maina.Config.DisplayMode == 0)
                                a += "\r\n查询时间 " + DateTime.Now;
                            if (Maina.Config.DisplayMode == 1)
                                a += $"\r\n{Maina.Config.ContentText}";
                            //a += $"\r\nIP:{Server.IpAddress}:{Server.Port}";
                            //a += "\r\n查询时间 " + DateTime.Now;


                            byte[] bytes = Encoding.UTF8.GetBytes(a);
                            socket.Send(bytes);
                            socket.Close();
                            Log.Debug($"接收消息{returna} - {a}");
                      
                        }
                        else if (returna == "info")
                        {
                            string a = string.Empty;
                            a += $"服务器#{Maina.Config.ServerName} - 查询Success!!";
                            a += $"\r\nDD人数:{Player.Get(PlayerRoles.RoleTypeId.ClassD).Count()}";
                            a += $"\r\n博士人数:{Player.Get(PlayerRoles.RoleTypeId.Scientist).Count()}人";
                            a += $"\r\nSCP人数:{Player.Get(PlayerRoles.Team.SCPs).Count()}";
                            a += $"\r\n回合进行时间：{Round.ElapsedTime}";
                            a += $"\r\n回合次数：{Round.UptimeRounds}";
                            a += $"\r\n下一波刷新时间：{Respawn.ProtectionTime}";
                            a += "\r\n查询时间" + DateTime.Now;
                            a += $"\r\n";
                            a += $"\r\n{Maina.Config.ContentText}";
                            byte[] bytes = Encoding.UTF8.GetBytes(a);
                            socket.Send(bytes);
                            socket.Close();
                            Log.Debug($"接收消息{returna} - {a}");

                        }
                        else if (returna == "start")
                        {
                            string aaa = string.Empty;
                            if (Round.IsStarted)
                            {
                                aaa = "回合已经开启了";
                            }
                            else
                            {
                                Round.Start();
                                aaa = "回合启动成功";
                            }
                            byte[] bytes = Encoding.UTF8.GetBytes(aaa);
                            socket.Send(bytes);
                            socket.Close();
                        }
                        else if (returna == "rest" && Round.ElapsedTime.TotalSeconds < 60)
                        {
                            string aaa = string.Empty;
                            Round.Restart(false);
                            aaa = "回合启动成功";
                            byte[] bytes = Encoding.UTF8.GetBytes(aaa);
                            socket.Send(bytes);
                            socket.Close();
                        }
                        else if (returna == "allrest")
                        {
                            string aaa = string.Empty;
                            Server.Restart();
                            aaa = "服务器启动成功";
                            byte[] bytes = Encoding.UTF8.GetBytes(aaa);
                            socket.Send(bytes);
                            socket.Close();
                        }
                        else if (returna.Contains("bc"))
                        {
                            string[] array2 = returna.Split('&');
                            string aaa = string.Empty;
                            Exiled.API.Features.Map.Broadcast(15, "[管理员消息]" + array2[1]);
                            aaa = "bc发送成功";
                            byte[] bytes = Encoding.UTF8.GetBytes(aaa);
                            socket.Send(bytes);
                            socket.Close();
                        }
                        else if (returna.Contains("ychhe"))
                        {
                            Round.IsLobbyLocked = true;
                            string[] array2 = returna.Split('&');
                            string aaa = string.Empty;
                            Exiled.API.Features.Map.Broadcast(17, "[该信息为自动发送]" + array2[1]);
                            aaa = "bc发送成功";
                            byte[] bytes = Encoding.UTF8.GetBytes(aaa);
                            socket.Send(bytes);
                            socket.Close();
                        }
                        else if (returna == ("list"))
                        {
                            string aaa = string.Empty;
                            foreach (var item in Player.List)
                            {
                                aaa += $"\r\n{item.Nickname}-{item.Id}";
                            }
                            byte[] bytes = Encoding.UTF8.GetBytes(aaa);
                            socket.Send(bytes);
                            socket.Close();
                        }
                        //else if (returna.Contains("kick"))
                        //{
                        //    string[] array2 = returna.Split('&');
                        //    string aaa = string.Empty;
                        //    Player a2a = Player.List.ToList().Find(x => x.Id.ToString() == array2[1]);
                        //    if (a2a == null)
                        //    { aaa = "踢出失败"; }
                        //    else
                        //    {
                        //        a2a.Kick("Q群联动封禁");
                        //        aaa = "踢出玩家成功";
                        //    }

                        //    byte[] bytes = Encoding.UTF8.GetBytes(aaa);
                        //    socket.Send(bytes);
                        //    socket.Close();
                        //}
                        else if (returna.Contains("kick"))
                        {
                            try
                            {
                                string[] array2 = returna.Split('&');
                                if (array2.Length < 4) // 确保数组长度至少为4（包括命令本身和三个参数）
                                {
                                    string errorMessage = "参数不足";
                                    Log.Error(errorMessage); // 使用Log.Error记录错误信息
                                    byte[] bytes0 = Encoding.UTF8.GetBytes(errorMessage);
                                    socket.Send(bytes0);
                                    socket.Close();
                                    //return; // 如果参数不足，直接返回，不进行后续操作
                                }

                                if (!int.TryParse(array2[1], out int id))
                                {
                                    string errorMessage = "第一个参数必须是整数";
                                    Log.Error(errorMessage); // 使用Log.Error记录错误信息
                                    byte[] bytes0 = Encoding.UTF8.GetBytes(errorMessage);
                                    socket.Send(bytes0);
                                    socket.Close();
                                    /*return*/
                                    ; // 如果第一个参数不是整数，直接返回，不进行后续操作
                                }

                                string param2 = array2[2]; // 第二个参数
                                if (!int.TryParse(array2[3], out int param3))
                                {
                                    string errorMessage = "第三个参数必须是整数";
                                    Log.Error(errorMessage); // 使用Log.Error记录错误信息
                                    byte[] bytes1 = Encoding.UTF8.GetBytes(errorMessage);
                                    socket.Send(bytes1);
                                    socket.Close();
                                    //return; // 如果第三个参数不是整数，直接返回，不进行后续操作
                                }
                                Player a2a = Player.List.ToList().Find(x => x.Id.ToString() == array2[1]);
                                //Player a2a = Player.List.ToList().Find(x => x.Id == id);
                                string aaa;

                                if (a2a == null)
                                {
                                    aaa = "踢出失败,未找到指定ID的玩家";
                                    Log.Error("未找到指定ID的玩家"); // 使用Log.Error记录错误信息
                                }
                                else
                                {
                                    try
                                    {
                                        a2a.Ban(param3, param2); // 假设Ban方法接受两个参数

                                        aaa = $"来自服务器生效禁令\r\n封禁ID\r\n{a2a.UserId}\r\n封禁IP{a2a.IPAddress} \r\n封禁时间:{param3} \r\n原因:{param2}";
                                        Log.Info("成功踢出玩家，ID: " + id); // 使用Log.Info记录成功信息
                                    }
                                    catch (Exception ex)
                                    {
                                        aaa = "踢出失败,\"踢出玩家时出错，ID: \" + id + \", 错误信息: \" + ex.Message";
                                        Log.Error("踢出玩家时出错，ID: " + id + ", 错误信息: " + ex.Message); // 使用Log.Error记录错误信息
                                    }
                                }

                                byte[] bytes = Encoding.UTF8.GetBytes(aaa);
                                socket.Send(bytes);
                                socket.Close();
                            }
                            catch (Exception ex)
                            {
                                Log.Error("处理kick命令时发生异常: " + ex.Message); // 使用Log.Error记录异常信息
                                byte[] bytes = Encoding.UTF8.GetBytes("服务器错误");
                                socket.Send(bytes);
                                socket.Close();
                            }
                        }

                        //else if (returna.Contains("setadmin"))
                        //{
                        //    try
                        //    {
                        //        string[] array2 = returna.Split('&');
                        //        if (array2.Length < 3) // 确保数组长度至少为3（包括命令本身和两个参数）
                        //        {
                        //            string errorMessage = "参数不足";
                        //            Log.Error(errorMessage); // 使用Log.Error记录错误信息
                        //            byte[] bytes0 = Encoding.UTF8.GetBytes(errorMessage);
                        //            socket.Send(bytes0);
                        //            socket.Close();
                        //            return; // 如果参数不足，直接返回，不进行后续操作
                        //        }

                        //        if (!int.TryParse(array2[1], out int playerId))
                        //        {
                        //            string errorMessage = "第一个参数必须是整数";
                        //            Log.Error(errorMessage); // 使用Log.Error记录错误信息
                        //            byte[] bytes0 = Encoding.UTF8.GetBytes(errorMessage);
                        //            socket.Send(bytes0);
                        //            socket.Close();
                        //            return; // 如果第一个参数不是整数，直接返回，不进行后续操作
                        //        }

                        //        string permissionLevel = array2[2]; // 第二个参数，假设为权限级别

                        //        Player a2a = Player.List.ToList().Find(x => x.Id == playerId);
                        //        string aaa;

                        //        if (a2a == null)
                        //        {
                        //            aaa = "设置管理员失败,未找到指定ID的玩家";
                        //            Log.Error("未找到指定ID的玩家"); // 使用Log.Error记录错误信息
                        //        }
                        //        else
                        //        {
                        //            try
                        //            {
                        //                // 根据permissionLevel设置相应的权限
                        //                // 假设我们有一个方法SetPermissions来设置权限
                        //                if (permissionLevel == "下权")
                        //                {
                        //                    var userGroup = new UserGroup
                        //                    {
                        //                        BadgeColor = "magenta",
                        //                        BadgeText = "无权限helper",
                        //                        Permissions = 004, // 根据实际情况设置权限
                        //                        Cover = true,
                        //                        HiddenByDefault = false,
                        //                        Shared = true,
                        //                        KickPower = 1,
                        //                        RequiredKickPower = 1
                        //                    };
                        //                    a2a.ReferenceHub.serverRoles.SetGroup(userGroup, true, true, true);
                        //                }
                        //                else
                        //                if (permissionLevel == "服务器B级赞助者")
                        //                {
                        //                    var userGroup = new UserGroup
                        //                    {
                        //                        BadgeColor = "yellow",
                        //                        BadgeText = "服务器B级赞助者",
                        //                        Permissions = 001, // 根据实际情况设置权限
                        //                        Cover = true,
                        //                        HiddenByDefault = false,
                        //                        Shared = true,
                        //                        KickPower = 1,
                        //                        RequiredKickPower = 1
                        //                    };
                        //                    a2a.ReferenceHub.serverRoles.SetGroup(userGroup, true, true, true);
                        //                }
                        //                else
                        //                if (permissionLevel == "服务器A级赞助者")
                        //                {
                        //                    var userGroup = new UserGroup
                        //                    {
                        //                        BadgeColor = "yellow",
                        //                        BadgeText = "服务器A级赞助者",
                        //                        Permissions = 002, // 根据实际情况设置权限
                        //                        Cover = true,
                        //                        HiddenByDefault = false,
                        //                        Shared = true,
                        //                        KickPower = 1,
                        //                        RequiredKickPower = 1
                        //                    };
                        //                    a2a.ReferenceHub.serverRoles.SetGroup(userGroup, true, true, true);
                        //                }
                        //                else
                        //                {
                        //                    aaa = "设置管理员失败，未知的权限级别";
                        //                    Log.Error("设置管理员时出错，未知的权限级别"); // 使用Log.Error记录错误信息
                        //                }

                        //                aaa = $"成功设置玩家{a2a.UserId}，权限级别{permissionLevel}";
                        //                Log.Info("成功设置玩家为管理员，ID: " + playerId + ", 权限级别: " + permissionLevel); // 使用Log.Info记录成功信息
                        //            }
                        //            catch (Exception ex)
                        //            {
                        //                aaa = "设置管理员失败，错误信息: " + ex.Message;
                        //                Log.Error("设置管理员时出错，ID: " + playerId + ", 错误信息: " + ex.Message); // 使用Log.Error记录错误信息
                        //            }
                        //        }

                        //        byte[] bytes = Encoding.UTF8.GetBytes(aaa);
                        //        socket.Send(bytes);
                        //        socket.Close();
                        //    }
                        //    catch (Exception ex)
                        //    {
                        //        Log.Error("处理setadmin命令时发生异常: " + ex.Message); // 使用Log.Error记录异常信息
                        //        byte[] bytes = Encoding.UTF8.GetBytes("服务器错误");
                        //        socket.Send(bytes);
                        //        socket.Close();
                        //    }
                        //}

                        Thread.Sleep(1000); // 等待1000毫秒（1秒）

                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Socket连接失败: " + ex.Message); // 使用Log.Error记录异常信息
                }
            });
            Log.Debug("Started");
        }
    }
}