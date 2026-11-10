namespace Server.Qcat.LocalAdmin;

public sealed record ArchiveFile(string Name, long Bytes);
public sealed record ArchiveDate(string Date, int Count, long Bytes);
public sealed record LogArchiveResult(IReadOnlyList<ArchiveFile> Files, IReadOnlyList<string> Lines, long Next, bool More);
public static class LogArchive
{
    public static string? FileDate(string name)
    {
        string[] parts = name.Replace('\\', '/').Split('/');
        string candidate = parts.Length == 3 ? parts[1] : parts.Length == 1 ? Path.GetFileNameWithoutExtension(parts[0]) : "";
        return DateTime.TryParseExact(candidate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date.ToString("yyyy-MM-dd") : null;
    }
    public static string? ResolveDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Equals("today", StringComparison.OrdinalIgnoreCase)) return DateTime.Now.ToString("yyyy-MM-dd");
        if (DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)) return date.ToString("yyyy-MM-dd");
        throw new ArgumentException("日期必须是 today 或 yyyy-MM-dd");
    }
    public static IReadOnlyList<ArchiveDate> Dates(IEnumerable<ArchiveFile> files) => files
        .Select(file => new { File = file, Date = FileDate(file.Name) }).Where(file => file.Date is not null)
        .GroupBy(file => file.Date!).Select(group => new ArchiveDate(group.Key, group.Count(), group.Sum(item => item.File.Bytes)))
        .OrderByDescending(day => day.Date, StringComparer.Ordinal).ToList();
    public static LogArchiveResult Read(string root, string? file, long offset, CancellationToken ct)
    {
        if (!Directory.Exists(root)) return new(Array.Empty<ArchiveFile>(), Array.Empty<string>(), 0, false);
        var files = new List<ArchiveFile>();
        // Never traverse symlinks/junctions into unrelated folders.
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return new(files, Array.Empty<string>(), 0, false);
        foreach (string path in Directory.EnumerateFiles(root, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (Path.GetExtension(path) is not (".txt" or ".log")) continue;
            files.Add(new(Path.GetRelativePath(root, path).Replace('\\', '/'), new FileInfo(path).Length));
        }
        files.Sort((a, b) => StringComparer.Ordinal.Compare(b.Name, a.Name));
        if (file is null) return new(files, Array.Empty<string>(), 0, false);
        if (!files.Any(f => f.Name == file)) throw new ArgumentException("日志文件不存在");
        using var stream = new FileStream(Path.Combine(root, file), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (offset < 0 || offset > stream.Length) throw new ArgumentException("日志偏移无效");
        stream.Position = offset;
        // Byte pagination preserves a bounded response and permits reading the entire archive.
        var bytes = new List<byte>();
        while (bytes.Count < 256 * 1024)
        {
            ct.ThrowIfCancellationRequested();
            int value = stream.ReadByte();
            if (value < 0) break;
            bytes.Add((byte)value);
        }
        // Do not split UTF-8 sequences at a page boundary.
        if (stream.Position < stream.Length)
        {
            int value;
            while ((value = stream.ReadByte()) >= 0)
            {
                if ((value & 0xC0) != 0x80) { stream.Position--; break; }
                bytes.Add((byte)value);
            }
        }
        return new(Array.Empty<ArchiveFile>(), new[] { System.Text.Encoding.UTF8.GetString(bytes.ToArray()) }, stream.Position, stream.Position < stream.Length);
    }
}
