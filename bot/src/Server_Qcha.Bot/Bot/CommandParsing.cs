using System.Text.RegularExpressions;

namespace Server.Qcat.Bot;

public static class CommandParsing
{
    public static bool TryParseHashIndex(string text, out int serverIndex)
    {
        serverIndex = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return false;

        if (text[0] != '#' && text[0] != '＃')
            return false;

        return int.TryParse(text[1..].Trim(), out serverIndex);
    }

    public static bool TryParseBan(string text, out int serverIndex, out string id, out string time, out string reason)
    {
        serverIndex = 0;
        id = "";
        time = "";
        reason = "";

        var m = Regex.Match(text, @"^/ban\s+(\d+)\s+(\S+)\s+(\S+)\s+(.+)$", RegexOptions.IgnoreCase);
        if (!m.Success)
            return false;

        serverIndex = int.Parse(m.Groups[1].Value);
        id = m.Groups[2].Value;
        time = m.Groups[3].Value;
        reason = m.Groups[4].Value.Trim();
        return true;
    }
}

