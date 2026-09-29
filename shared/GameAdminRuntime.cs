using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using YamlDotNet.RepresentationModel;

namespace Qchat.GameAdmin
{
    /// <summary>One main-thread queue per server. Nothing mutates Unity state on the TCP worker.</summary>
    public sealed class GameAdminRuntime : MonoBehaviour
    {
        private static GameAdminRuntime instance;
        private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private Harmony harmony;
        private string configPath, pluginPath, pluginSourcePath, statePath;
        private AdminSnapshot active;
        private string failure;
        private readonly Dictionary<string, Tuple<string, string>> replies = new Dictionary<string, Tuple<string, string>>();
        private readonly Queue<string> replyOrder = new Queue<string>();
        private float nextRefresh;
#if LABAPI
        private const string Framework = "LabAPI";
        private const string InheritanceKey = "inherited_groups";
#else
        private const string Framework = "EXILED";
        private const string InheritanceKey = "inheritance";
#endif
        public static void StartRuntime()
        {
            if (instance != null) return;
            var obj = new GameObject("Qcha.GameAdmin");
            DontDestroyOnLoad(obj);
            instance = obj.AddComponent<GameAdminRuntime>();
            try { instance.Initialize(); }
            catch (Exception ex) { instance.failure = ex.Message; Debug.LogError("[Qcha GameAdmin] " + ex); }
        }
        public static void StopRuntime()
        {
            if (instance == null) return;
            instance.harmony?.UnpatchAll("qcha.game-admin");
            Destroy(instance.gameObject);
            instance = null;
        }
        public static string Dispatch(string json)
        {
            var runtime = instance;
            if (runtime == null) return AdminJson.Write(new AdminReply { Error = "权限服务尚未就绪", Code = "unavailable" });
            var result = new TaskCompletionSource<string>();
            // On timeout the queued work is cancelled, never applied later without the caller knowing.
            var gate = new object(); bool cancelled = false;
            runtime.queue.Enqueue(() =>
            {
                lock (gate)
                {
                    if (cancelled) return;
                    try { result.TrySetResult(runtime.Handle(json)); }
                    catch (Exception ex) { result.TrySetResult(AdminJson.Write(new AdminReply { Error = ex.Message, Code = "error" })); }
                }
            });
            if (!result.Task.Wait(TimeSpan.FromSeconds(12)))
            {
                lock (gate) { if (!result.Task.IsCompleted) cancelled = true; }
                if (cancelled) return AdminJson.Write(new AdminReply { Error = "主线程繁忙，请重新读取核实", Code = "timeout" });
            }
            return result.Task.Result;
        }
        private void Update()
        {
            Action action;
            while (queue.TryDequeue(out action)) action();
            // External config edits and reload commands are picked up without a connected panel.
            if (Time.realtimeSinceStartup < nextRefresh || failure != null) return;
            nextRefresh = Time.realtimeSinceStartup + 2;
            try
            {
                if (active != null && Revision() != active.Revision) { active = Read(); Apply(); }
            }
            catch (Exception ex) { Debug.LogError("[Qcha GameAdmin] Reload failed: " + ex.Message); }
        }
        private IEnumerable<string> Paths()
        {
            yield return configPath; yield return pluginPath; yield return statePath;
            if (!File.Exists(pluginPath)) yield return pluginSourcePath;
            if (ServerStatic.SharedGroupsConfig != null && !string.IsNullOrEmpty(ServerStatic.SharedGroupsConfig.Path)) yield return ServerStatic.SharedGroupsConfig.Path;
            if (ServerStatic.SharedGroupsMembersConfig != null && !string.IsNullOrEmpty(ServerStatic.SharedGroupsMembersConfig.Path)) yield return ServerStatic.SharedGroupsMembersConfig.Path;
        }
        private string Revision() { return AdminFiles.Revision(Paths().Distinct()); }
        private void Initialize()
        {
            configPath = ServerStatic.RolesConfig.Path;
            statePath = configPath + ".qga.json";
#if LABAPI
            pluginSourcePath = Path.Combine(LabApi.Loader.Features.Paths.PathManager.Configs.FullName, "permissions.yml");
#else
            pluginSourcePath = Exiled.Permissions.Permissions.Instance.Config.FullPath;
#endif
            // Both frameworks can share their default permissions file between server instances.
            // Import it on first use, then keep this server's editable permissions beside its RA config.
            pluginPath = configPath + ".qga.plugins.yml";
            AdminFiles.Recover(statePath + ".journal");
            harmony = new Harmony("qcha.game-admin");
            InstallPatches();
            active = Read();
            Apply();
        }
        private void InstallPatches()
        {
            // Runs before framework root/FullPermissions shortcuts. Console/host senders are untouched.
#if LABAPI
            var type = typeof(LabApi.Features.Permissions.Providers.DefaultPermissionsProvider);
            foreach (var name in new[] { "HasPermission", "HasPermissions", "HasAnyPermission" })
            {
                var method = type.GetMethods().Single(m => m.Name == name && m.GetParameters()[0].ParameterType == typeof(LabApi.Features.Wrappers.Player));
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GameAdminRuntime).GetMethod(nameof(PluginPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
#else
            var type = typeof(Exiled.Permissions.Extensions.Permissions);
            foreach (var method in type.GetMethods().Where(m => m.Name == "CheckPermission" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(string)))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GameAdminRuntime).GetMethod(nameof(PluginPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
#endif
            harmony.Patch(typeof(ServerRoles).GetMethod("RefreshPermissions"), prefix: new HarmonyMethod(typeof(GameAdminRuntime).GetMethod(nameof(RefreshPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
        }
        private static bool PluginPrefix(MethodBase __originalMethod, object[] __args, ref bool __result)
        {
            if (instance == null || instance.active == null || __args[0] == null) return true;
            var userId = Property(__args[0], "UserId") as string ?? Property(__args[0], "SenderId") as string;
            var member = instance.active.Members.FirstOrDefault(m => m.UserId == userId);
            // Framework default groups also apply to ordinary players. Do not intercept console senders.
            if (member == null && !string.IsNullOrEmpty(userId) && userId.Contains("@"))
            {
#if LABAPI
                const string defaultKey = "default";
#else
                var defaultKey = Exiled.Permissions.Extensions.Permissions.Groups.FirstOrDefault(g => g.Value.IsDefault).Key;
#endif
                if (defaultKey != null && instance.active.Groups.Any(g => g.Key == defaultKey)) member = new AdminMember { UserId = userId, Group = defaultKey };
            }
            if (member == null) return true;
            var nodes = __args[1] as string[] ?? new[] { __args[1] as string };
            try
            {
                __result = __originalMethod.Name == "HasAnyPermission"
                    ? nodes.Any(n => n != null && AdminPolicy.Plugin(instance.active, member, n))
                    : nodes.All(n => n != null && AdminPolicy.Plugin(instance.active, member, n));
            }
            catch { __result = false; }
            return false;
        }
        private static void RefreshPrefix() { if (instance?.active != null) instance.InstallDerivedGroups(); }
        private static object Property(object obj, string name)
        {
            return obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj, null)
                ?? obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);
        }
        private static IDictionary HandlerMap(string name)
        {
            var handler = ServerStatic.PermissionsHandler;
            return (IDictionary)(Property(handler, name) ?? Property(handler, "_" + char.ToLowerInvariant(name[0]) + name.Substring(1)));
        }
        private static YamlMappingNode LoadYaml(string path)
        {
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(File.ReadAllText(path))) return new YamlMappingNode();
            var stream = new YamlStream();
            stream.Load(new StringReader(File.ReadAllText(path)));
            return (YamlMappingNode)stream.Documents[0].RootNode;
        }
        private static List<string> YamlList(YamlMappingNode map, string key)
        {
            YamlNode node;
            return map.Children.TryGetValue(new YamlScalarNode(key), out node) && node is YamlSequenceNode
                ? ((YamlSequenceNode)node).Children.Select(n => ((YamlScalarNode)n).Value).ToList() : new List<string>();
        }
        private YamlMappingNode PluginConfig() { return LoadYaml(File.Exists(pluginPath) ? pluginPath : pluginSourcePath); }
        private static void SetYamlList(YamlMappingNode map, string key, IEnumerable<string> list)
        {
            map.Children[new YamlScalarNode(key)] = new YamlSequenceNode(list.Select(n => (YamlNode)new YamlScalarNode(n)));
        }
        private AdminSnapshot Read()
        {
            var result = new AdminSnapshot { Framework = Framework, Revision = Revision() };
            foreach (DictionaryEntry item in HandlerMap("Permissions"))
                result.Catalog.Add(new PermissionDescriptor { Key = (string)item.Key, Name = PermissionNames.Name((string)item.Key) });
            var native = new NativeConfig(File.ReadAllText(configPath));
            var plugin = PluginConfig();
            var state = File.Exists(statePath) ? AdminJson.Read<AdminSnapshot>(File.ReadAllText(statePath)) : new AdminSnapshot();
            var keys = native.List("Roles");
            foreach (var key in keys)
            {
                var group = new AdminGroup
                {
                    Key = key, Badge = native.Scalar(key + "_badge"), Color = native.Scalar(key + "_color", "default"),
                    Cover = native.Scalar(key + "_cover", "true") == "true", HiddenByDefault = native.Scalar(key + "_hidden", "false") == "true",
                    KickPower = ParseByte(native.Scalar(key + "_kick_power", "0")), RequiredKickPower = ParseByte(native.Scalar(key + "_required_kick_power", "0")),
                    PluginsEnabled = state.Groups.FirstOrDefault(g => g.Key == key)?.PluginsEnabled ?? true
                };
                foreach (var perm in native.Map("Permissions"))
                    if (perm.Value.Trim('[', ']').Split(',').Select(s => s.Trim()).Contains(key)) group.Permissions.Add(perm.Key);
                result.Groups.Add(group);
            }
            // Shared groups remain visible. Editing shared files would violate per-server isolation.
            foreach (DictionaryEntry entry in HandlerMap("Groups"))
            {
                var key = (string)entry.Key;
                if (keys.Contains(key) || key.StartsWith(AdminValidation.DerivedPrefix)) continue;
                var value = (UserGroup)entry.Value;
                if (!(bool)(Property(value, "Shared") ?? false)) continue;
                result.Groups.Add(new AdminGroup { Key = key, Badge = value.BadgeText, Color = value.BadgeColor, Shared = true,
                    KickPower = value.KickPower, RequiredKickPower = value.RequiredKickPower, Cover = value.Cover, HiddenByDefault = value.HiddenByDefault,
                    Permissions = result.Catalog.Where(c => (value.Permissions & Convert.ToUInt64(HandlerMap("Permissions")[c.Key])) != 0).Select(c => c.Key).ToList() });
            }
            foreach (var entry in plugin.Children)
            {
                var key = ((YamlScalarNode)entry.Key).Value;
                var map = entry.Value as YamlMappingNode;
                if (map == null) continue;
                var group = result.Groups.FirstOrDefault(g => g.Key == key);
                if (group == null) { group = new AdminGroup { Key = key, Badge = key, PluginOnly = true }; result.Groups.Add(group); }
                group.PluginPermissions = YamlList(map, "permissions");
                group.Inheritance = YamlList(map, InheritanceKey);
                group.PluginsEnabled = state.Groups.FirstOrDefault(g => g.Key == key)?.PluginsEnabled ?? true;
            }
            foreach (var item in native.Map("Members"))
            {
                var member = state.Members.FirstOrDefault(m => m.UserId == item.Key && m.Group == item.Value) ?? new AdminMember { UserId = item.Key, Group = item.Value };
                result.Members.Add(member);
            }
            var sharedMembers = ServerStatic.SharedGroupsMembersConfig == null ? new Dictionary<string, string>()
                : new NativeConfig(File.ReadAllText(ServerStatic.SharedGroupsMembersConfig.Path)).Map("SharedMembers");
            foreach (var item in sharedMembers)
            {
                if (result.Members.Any(m => m.UserId == item.Key) || !result.Groups.Any(g => g.Key == item.Value)) continue;
                result.Members.Add(new AdminMember { UserId = item.Key, Group = item.Value, Shared = true });
            }
            return result;
        }
        private static byte ParseByte(string value) { byte number; return byte.TryParse(value, out number) ? number : (byte)0; }
        private string Handle(string json)
        {
            if (failure != null) return AdminJson.Write(new AdminReply { Code = "unavailable", Error = failure });
            var request = AdminJson.Read<AdminRequest>(json);
            var current = Read();
            if (request.Operation == "read" || request.Operation == "capabilities") return AdminJson.Write(new AdminReply { Success = true, Snapshot = current });
            if (string.IsNullOrEmpty(request.RequestId) || request.RequestId.Length > 80) throw new InvalidOperationException("缺少请求标识");
            Tuple<string, string> cached;
            if (replies.TryGetValue(request.RequestId, out cached))
            {
                if (cached.Item1 != json) throw new InvalidOperationException("请求标识已被其他操作使用");
                return cached.Item2;
            }
            if (current.Revision != request.Revision) return AdminJson.Write(new AdminReply { Code = "conflict", Error = "配置已改变，请重新读取后编辑", Snapshot = current });
            var next = AdminChanges.Apply(current, request);
            var files = Serialize(next, current);
            if (Revision() != request.Revision) return AdminJson.Write(new AdminReply { Code = "conflict", Error = "保存前配置已改变，请重新读取" });
            AdminFiles.Commit(statePath + ".journal", files, () => { active = Read(); Apply(); });
            active = Read();
            var reply = AdminJson.Write(new AdminReply { Success = true, Persisted = true, Applied = true, Snapshot = active });
            replies.Add(request.RequestId, Tuple.Create(json, reply)); replyOrder.Enqueue(request.RequestId);
            if (replyOrder.Count > 256) replies.Remove(replyOrder.Dequeue());
            return reply;
        }
        private Dictionary<string, string> Serialize(AdminSnapshot snapshot, AdminSnapshot previous)
        {
            var native = new NativeConfig(File.ReadAllText(configPath));
            var localGroups = snapshot.Groups.Where(g => !g.PluginOnly && !g.Shared).ToList();
            native.Set("Roles", null, localGroups.Select(g => g.Key));
            native.Set("Members", null, snapshot.Members.Where(m => !m.Shared).Select(m => m.UserId + ": " + m.Group));
            var permissions = native.Map("Permissions");
            foreach (var item in snapshot.Catalog)
                permissions[item.Key] = "[" + string.Join(", ", localGroups.Where(g => g.Permissions.Contains(item.Key)).Select(g => g.Key)) + "]";
            native.Set("Permissions", null, permissions.Select(p => p.Key + ": " + p.Value));
            foreach (var group in localGroups)
            {
                native.Set(group.Key + "_badge", group.Badge); native.Set(group.Key + "_color", group.Color);
                native.Set(group.Key + "_cover", group.Cover.ToString().ToLowerInvariant()); native.Set(group.Key + "_hidden", group.HiddenByDefault.ToString().ToLowerInvariant());
                native.Set(group.Key + "_kick_power", group.KickPower.ToString()); native.Set(group.Key + "_required_kick_power", group.RequiredKickPower.ToString());
            }
            var plugin = PluginConfig();
            foreach (var group in previous.Groups.Where(g => !snapshot.Groups.Any(n => n.Key == g.Key))) plugin.Children.Remove(new YamlScalarNode(group.Key));
            foreach (var group in snapshot.Groups.Where(g => !g.Shared))
            {
                YamlNode node;
                if (!plugin.Children.TryGetValue(new YamlScalarNode(group.Key), out node)) { node = new YamlMappingNode(); plugin.Children[new YamlScalarNode(group.Key)] = node; }
                var map = (YamlMappingNode)node;
                SetYamlList(map, "permissions", group.PluginPermissions); SetYamlList(map, InheritanceKey, group.Inheritance);
            }
            var yaml = new YamlStream(new YamlDocument(plugin)); var writer = new StringWriter(); yaml.Save(writer, false);
            return new Dictionary<string, string> { { configPath, native.ToString() }, { pluginPath, writer.ToString() }, { statePath, AdminJson.Write(snapshot) } };
        }
        private void Apply()
        {
            // Do not rely on framework Reload() re-reading disk: LabAPI 1.1.7 only recomputes its cache.
            ServerStatic.RolesConfig.Reload();
            ServerStatic.PermissionsHandler = new PermissionsHandler(ref ServerStatic.RolesConfig, ref ServerStatic.SharedGroupsConfig, ref ServerStatic.SharedGroupsMembersConfig);
#if LABAPI
            var provider = LabApi.Features.Permissions.PermissionsManager.GetProvider<LabApi.Features.Permissions.Providers.DefaultPermissionsProvider>();
            var field = typeof(LabApi.Features.Permissions.Providers.DefaultPermissionsProvider).GetField("_permissionsDictionary", BindingFlags.Instance | BindingFlags.NonPublic);
            if (provider == null || field == null) throw new InvalidOperationException("LabAPI 默认权限提供器不可用");
            var dictionary = new Dictionary<string, LabApi.Features.Permissions.Providers.PermissionGroup>();
            foreach (var entry in PluginConfig().Children)
            {
                var map = entry.Value as YamlMappingNode;
                if (map != null) dictionary[((YamlScalarNode)entry.Key).Value] = new LabApi.Features.Permissions.Providers.PermissionGroup(YamlList(map, InheritanceKey).ToArray(), YamlList(map, "permissions").ToArray());
            }
            field.SetValue(provider, dictionary);
            provider.ReloadPermissions();
#else
            Exiled.Permissions.Extensions.Permissions.Reload();
#endif
            InstallDerivedGroups();
#if LABAPI
            foreach (var player in LabApi.Features.Wrappers.Player.List.Where(p => p.PlayerId >= 0)) RefreshPlayer(player.UserId, player.ReferenceHub);
#else
            foreach (var player in Exiled.API.Features.Player.List.Where(p => p.Id >= 0)) RefreshPlayer(player.UserId, player.ReferenceHub);
#endif
            active.Revision = Revision();
        }
        private void RefreshPlayer(string userId, ReferenceHub hub)
        {
            // SetGroup(null) revokes an online removed admin; RefreshPermissions alone does not.
            hub.serverRoles.SetGroup(ServerStatic.PermissionsHandler.GetUserGroup(userId));
        }
        private void InstallDerivedGroups()
        {
            var groups = HandlerMap("Groups"); var members = HandlerMap("Members"); var permissions = HandlerMap("Permissions");
            foreach (var key in groups.Keys.Cast<string>().Where(k => k.StartsWith(AdminValidation.DerivedPrefix)).ToArray()) groups.Remove(key);
            foreach (var member in active.Members.Where(m => !m.Shared))
            {
                if (!groups.Contains(member.Group)) continue;
                if (member.Allow.Count == 0 && member.Deny.Count == 0) { members[member.UserId] = member.Group; continue; }
                var source = (UserGroup)groups[member.Group];
                var derived = new UserGroup { BadgeText = source.BadgeText, BadgeColor = source.BadgeColor, Cover = source.Cover, HiddenByDefault = source.HiddenByDefault,
                    KickPower = source.KickPower, RequiredKickPower = source.RequiredKickPower };
                foreach (var key in AdminPolicy.Native(active.Groups.First(g => g.Key == member.Group), member)) derived.Permissions |= Convert.ToUInt64(permissions[key]);
                var name = AdminValidation.DerivedPrefix + AdminFiles.Hash(member.UserId).Substring(0, 24);
                var field = typeof(UserGroup).GetField("Name"); if (field != null) field.SetValue(derived, name);
                groups[name] = derived; members[member.UserId] = name;
            }
        }
    }
}
