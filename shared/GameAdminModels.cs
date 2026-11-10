#if NET8_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Qchat.GameAdmin
{
    // Shared source: net481/net48 game plugins and net8 panel. Permission keys never cross JS as a bit mask.
    public sealed class PermissionDescriptor
    {
        public string Key { get; set; }
        public string Name { get; set; }
    }
    public sealed class AdminGroup
    {
        public bool Shared { get; set; }
        public bool PluginOnly { get; set; }
        public string Key { get; set; }
        public string Badge { get; set; } = "";
        public string Color { get; set; } = "default";
        public byte KickPower { get; set; }
        public byte RequiredKickPower { get; set; }
        public bool HiddenByDefault { get; set; }
        public bool Cover { get; set; } = true;
        public List<string> Permissions { get; set; } = new List<string>();
        public bool PluginsEnabled { get; set; } = true;
        public List<string> PluginPermissions { get; set; } = new List<string>();
        public List<string> Inheritance { get; set; } = new List<string>();
    }
    public sealed class AdminMember
    {
        public bool Shared { get; set; }
        public string UserId { get; set; }
        public string Group { get; set; }
        public bool? PluginsEnabled { get; set; }
        public List<string> Allow { get; set; } = new List<string>();
        public List<string> Deny { get; set; } = new List<string>();
        public List<string> PluginAllow { get; set; } = new List<string>();
        public List<string> PluginDeny { get; set; } = new List<string>();
    }
    public sealed class AdminSnapshot
    {
        public string Revision { get; set; }
        public string Framework { get; set; }
        public List<PermissionDescriptor> Catalog { get; set; } = new List<PermissionDescriptor>();
        public List<AdminGroup> Groups { get; set; } = new List<AdminGroup>();
        public List<AdminMember> Members { get; set; } = new List<AdminMember>();
    }
    public sealed class AdminRequest
    {
        public string Operation { get; set; } = "read";
        public string Revision { get; set; }
        public string RequestId { get; set; }
        public string Key { get; set; }
        public AdminGroup Group { get; set; }
        public AdminMember Member { get; set; }
    }
    public sealed class AdminReply
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Code { get; set; }
        public bool Persisted { get; set; }
        public bool Applied { get; set; }
        public AdminSnapshot Snapshot { get; set; }
    }
    public static class AdminJson
    {
        public static string Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        public static T Read<T>(string text)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        public static T Clone<T>(T value) { return Read<T>(Write(value)); }
    }
    public static class AdminPolicy
    {
        public static IEnumerable<string> Native(AdminGroup group, AdminMember member)
        {
            return group.Permissions.Union(member.Allow).Except(member.Deny);
        }
        public static bool Matches(string pattern, string node)
        {
            return pattern == ".*" || pattern == "*" || string.Equals(pattern, node, StringComparison.OrdinalIgnoreCase)
                || (pattern.EndsWith(".*", StringComparison.Ordinal) && node.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase));
        }
        public static IEnumerable<string> PluginNodes(AdminSnapshot snapshot, AdminGroup group, HashSet<string> visited = null)
        {
            visited = visited ?? new HashSet<string>();
            if (!visited.Add(group.Key)) throw new InvalidOperationException("权限组继承存在循环: " + group.Key);
            var nodes = new List<string>(group.PluginPermissions);
            foreach (var key in group.Inheritance)
            {
                var parent = snapshot.Groups.FirstOrDefault(g => g.Key == key);
                if (parent == null) throw new InvalidOperationException("继承组不存在: " + key);
                nodes.AddRange(PluginNodes(snapshot, parent, visited));
            }
            visited.Remove(group.Key);
            return nodes.Distinct(StringComparer.OrdinalIgnoreCase);
        }
        public static bool Plugin(AdminSnapshot snapshot, AdminMember member, string node)
        {
            var group = snapshot.Groups.First(g => g.Key == member.Group);
            if (!(member.PluginsEnabled ?? group.PluginsEnabled)) return false;
            if (member.PluginDeny.Any(p => Matches(p, node))) return false;
            return member.PluginAllow.Concat(PluginNodes(snapshot, group)).Any(p => Matches(p, node));
        }
    }
}
