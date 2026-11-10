#if NET8_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Qchat.GameAdmin
{
    // SCPSL's config format is a line-oriented dialect, not a general YAML document.
    // Only replace the requested key's data; unrelated keys and comments survive verbatim.
    public sealed class NativeConfig
    {
        private readonly List<string> lines;
        private readonly string newline;
        public NativeConfig(string text)
        {
            newline = text.Contains("\r\n") ? "\r\n" : "\n";
            lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        }
        private int Index(string key) { return lines.FindIndex(l => Regex.IsMatch(l, "^" + Regex.Escape(key) + @"\s*:")); }
        public string Scalar(string key, string fallback = "")
        {
            int i = Index(key);
            return i < 0 ? fallback : lines[i].Substring(lines[i].IndexOf(':') + 1).Trim();
        }
        public List<string> List(string key)
        {
            int start = Index(key);
            var result = new List<string>();
            if (start < 0) return result;
            for (int i = start + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (Regex.IsMatch(line, @"^[^\s#-][^:]*:")) break;
                if (line.TrimStart().StartsWith("- ")) result.Add(line.Trim().Substring(2).Trim());
            }
            return result;
        }
        public Dictionary<string, string> Map(string key)
        {
            var result = new Dictionary<string, string>();
            foreach (var entry in List(key))
            {
                var colon = entry.IndexOf(':');
                if (colon > 0) result[entry.Substring(0, colon).Trim()] = entry.Substring(colon + 1).Trim();
            }
            return result;
        }
        public void Set(string key, string value, IEnumerable<string> items = null)
        {
            int start = Index(key);
            if (start < 0) { start = lines.Count; lines.Add(key + ":"); }
            lines[start] = key + ":" + (value == null ? "" : " " + value);
            for (int i = start + 1; i < lines.Count;)
            {
                if (Regex.IsMatch(lines[i], @"^[^\s#-][^:]*:")) break;
                if (lines[i].TrimStart().StartsWith("- ")) lines.RemoveAt(i); else i++;
            }
            if (items != null) lines.InsertRange(start + 1, items.Select(s => " - " + s));
        }
        public override string ToString() { return string.Join(newline, lines); }
    }
    public static class AdminValidation
    {
        public const string DerivedPrefix = "qga_";
        public static void Validate(AdminSnapshot snapshot)
        {
            var known = new HashSet<string>(snapshot.Catalog.Select(c => c.Key));
            if (snapshot.Groups.Select(g => g.Key).Distinct().Count() != snapshot.Groups.Count) throw new InvalidOperationException("组名重复");
            foreach (var group in snapshot.Groups)
            {
                if (string.IsNullOrEmpty(group.Key) || !Regex.IsMatch(group.Key, @"^[A-Za-z0-9_-]{1,64}$") || group.Key.StartsWith(DerivedPrefix))
                    throw new InvalidOperationException("组标识须为 1–64 位字母、数字、下划线或连字符，不能以 qga_ 开头");
                SafeText(group.Badge, 128); SafeText(group.Color, 40);
                if (string.IsNullOrWhiteSpace(group.Badge) || string.IsNullOrWhiteSpace(group.Color)) throw new InvalidOperationException("称号和颜色不能为空（隐藏称号请使用颜色 none）");
                Keys(group.Permissions, known);
                Nodes(group.PluginPermissions);
                if (group.Inheritance == null) throw new InvalidOperationException("继承列表不能为空值");
                AdminPolicy.PluginNodes(snapshot, group).ToArray();
            }
            if (snapshot.Members.Select(m => m.UserId).Distinct().Count() != snapshot.Members.Count) throw new InvalidOperationException("UserID 重复");
            foreach (var member in snapshot.Members)
            {
                if (member.UserId == null || !Regex.IsMatch(member.UserId, @"^[A-Za-z0-9._-]+@[A-Za-z0-9._-]+$") || member.UserId.Length > 160)
                    throw new InvalidOperationException("请输入持久 UserID，例如 7656119…@steam");
                if (!snapshot.Groups.Any(g => g.Key == member.Group)) throw new InvalidOperationException("管理员的权限组不存在");
                Keys(member.Allow, known); Keys(member.Deny, known);
                Nodes(member.PluginAllow); Nodes(member.PluginDeny);
                if (member.Allow.Intersect(member.Deny).Any()) throw new InvalidOperationException("原生权限不能同时允许和拒绝");
            }
        }
        private static void SafeText(string value, int max)
        {
            if (value == null || value.Length > max || value.Any(char.IsControl) || value.Contains("#")) throw new InvalidOperationException("文本包含非法字符或过长");
        }
        private static void Keys(List<string> values, HashSet<string> known)
        {
            if (values == null) throw new InvalidOperationException("权限列表不能为空值");
            var unsupported = values.Where(v => !known.Contains(v)).ToArray();
            if (unsupported.Length > 0) throw new InvalidOperationException("本服不支持权限: " + string.Join(", ", unsupported));
        }
        private static void Nodes(List<string> nodes)
        {
            if (nodes == null || nodes.Count > 4096 || nodes.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > 160 || !Regex.IsMatch(n, @"^[A-Za-z0-9_.:*\-]+$")))
                throw new InvalidOperationException("插件权限节点无效");
        }
    }
    public static class AdminFiles
    {
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        public static string Revision(IEnumerable<string> paths)
        {
            return Hash(string.Join("\n", paths.OrderBy(p => p, StringComparer.Ordinal).Select(p => p + "\n" + (File.Exists(p) ? Convert.ToBase64String(File.ReadAllBytes(p)) : "<absent>"))));
        }
        // Durable recovery journal is written before replacing any participating file.
        public static void Commit(string journalPath, Dictionary<string, string> contents, Action apply)
        {
            if (File.Exists(journalPath)) throw new IOException("存在未恢复的权限事务");
            var old = contents.Keys.Select(path => new BackupFile { Path = path, Data = File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null }).ToList();
            Atomic(journalPath, AdminJson.Write(old));
            foreach (var item in old.Where(x => x.Data != null)) AtomicBytes(item.Path + ".qga.bak", Convert.FromBase64String(item.Data));
            try
            {
                foreach (var file in contents) Atomic(file.Key, file.Value);
                apply();
                File.Delete(journalPath);
            }
            catch
            {
                Restore(old);
                apply();
                File.Delete(journalPath);
                throw;
            }
        }
        public static void Recover(string journalPath)
        {
            if (!File.Exists(journalPath)) return;
            Restore(AdminJson.Read<List<BackupFile>>(File.ReadAllText(journalPath)));
            File.Delete(journalPath);
        }
        private static void Restore(List<BackupFile> files)
        {
            foreach (var file in files)
            {
                if (file.Data == null) { if (File.Exists(file.Path)) File.Delete(file.Path); }
                else AtomicBytes(file.Path, Convert.FromBase64String(file.Data));
            }
        }
        public static void Atomic(string path, string text) { AtomicBytes(path, new UTF8Encoding(false).GetBytes(text)); }
        private static void AtomicBytes(string path, byte[] bytes)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public sealed class BackupFile
    {
        public string Path { get; set; }
        public string Data { get; set; }
    }
}
