#if NET8_0_OR_GREATER
#nullable disable
#endif
using System.Collections.Generic;

namespace Qchat.GameAdmin
{
    public static class PermissionNames
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "KickingAndShortTermBanning", "踢出与短期封禁" }, { "BanningUpToDay", "一天以内封禁" }, { "LongTermBanning", "长期封禁" },
            { "ForceclassSelf", "更改自己的角色" }, { "ForceclassToSpectator", "将玩家转为观察者" }, { "ForceclassWithoutRestrictions", "无限制更改角色" },
            { "GivingItems", "给予物品" }, { "WarheadEvents", "核弹控制" }, { "RespawnEvents", "刷新支援队伍" }, { "RoundEvents", "回合控制" },
            { "SetGroup", "设置玩家权限组" }, { "GameplayData", "查看游戏数据" }, { "Overwatch", "监管模式" }, { "FacilityManagement", "设施管理" },
            { "PlayersManagement", "玩家管理" }, { "PermissionsManagement", "权限管理" }, { "ServerConsoleCommands", "服务器控制台命令" },
            { "ViewHiddenBadges", "查看隐藏本地称号" }, { "ServerConfigs", "服务器配置" }, { "Broadcasting", "发送广播" },
            { "PlayerSensitiveDataAccess", "查看玩家敏感数据" }, { "Noclip", "穿墙飞行" }, { "AFKImmunity", "免除挂机处理" },
            { "AdminChat", "管理员聊天" }, { "ViewHiddenGlobalBadges", "查看隐藏全局称号" }, { "Announcer", "播报控制" }, { "Effects", "设置效果" },
            { "FriendlyFireDetectorImmunity", "免除友伤检测" }, { "FriendlyFireDetectorTempDisable", "临时关闭友伤检测" },
            { "ServerLogLiveFeed", "实时服务器日志" }, { "ExecuteAs", "以他人身份执行" }, { "Vanish", "隐身" }
        };
        public static string Name(string key) { string name; return Names.TryGetValue(key, out name) ? name : "扩展权限：" + key; }
    }
}
