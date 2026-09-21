using System.Text;

namespace Server.Qcat.Bot;

/// <summary>解析后的指令请求。</summary>
public sealed record BotCommandRequest(
    string Name,
    string[] Args,
    string RawText,
    bool ExplicitCommand)
{
    public static readonly BotCommandRequest Empty = new("", Array.Empty<string>(), "", false);

    public bool IsEmpty => string.IsNullOrEmpty(Name);

    public string Arg(int index) => index >= 0 && index < Args.Length ? Args[index] : "";

    /// <summary>从指定下标起把剩余参数拼回一整段（保留用户输入的原始间隔为单空格）。</summary>
    public string ArgTextFrom(int index)
        => index >= Args.Length ? "" : string.Join(' ', Args[index..]);
}

/// <summary>
/// 平台无关的指令解析器。
///
/// 归一化规则贴合 QQ 官方对指令的要求与国内输入法实情：
/// <list type="bullet">
/// <item>全角斜杠 <c>／</c>、全角井号 <c>＃</c>、全角空格统一折算为半角；</item>
/// <item>指令与参数之间以一个或多个空白分隔（官方明确要求带空格，例如 <c>/抽卡 一</c>）；</item>
/// <item>去掉零宽字符与首尾空白，避免从客户端复制粘贴带来的隐形字符导致指令不识别；</item>
/// <item>命令词大小写不敏感，统一小写后与 <see cref="BotCommandCatalog"/> 匹配。</item>
/// </list>
///
/// 兼容两种写法：带前缀（<c>/cx</c>、<c>#1</c>）与裸写（<c>cx</c>），
/// 后者是为 NapCat 既有使用习惯与官方「私聊无需前缀」的建议保留的。
/// </summary>
public static class BotCommandParser
{
    /// <summary>归一化：全角折算、去零宽字符、折叠空白。</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var sb = new StringBuilder(raw.Length);

        foreach (char ch in raw)
        {
            switch (ch)
            {
                case '\u3000': // 全角空格
                case '\t':
                    sb.Append(' ');
                    continue;
                case '\uFF0F': // ／
                    sb.Append('/');
                    continue;
                case '\uFF03': // ＃
                    sb.Append('#');
                    continue;
                case '\uFF20': // ＠
                    sb.Append('@');
                    continue;
                case '\u200B':
                case '\u200C':
                case '\u200D':
                case '\uFEFF':
                    continue;
                default:
                    sb.Append(ch);
                    continue;
            }
        }

        // 折叠连续空白
        var result = new StringBuilder(sb.Length);
        bool lastWasSpace = false;
        foreach (char ch in sb.ToString())
        {
            bool isSpace = ch == ' ';
            if (isSpace && lastWasSpace)
                continue;
            result.Append(ch);
            lastWasSpace = isSpace;
        }

        return result.ToString().Trim();
    }

    public static BotCommandRequest Parse(string? raw)
    {
        string text = Normalize(raw);
        if (text.Length == 0)
            return BotCommandRequest.Empty;

        // 兼容旧的 # 语法：#qcha 查看版本，#<n> 查询第 n 个服务器玩家列表
        if (text[0] == '#')
        {
            string rest = text[1..].Trim();
            if (rest.Length == 0)
                return BotCommandRequest.Empty;

            if (int.TryParse(rest, out _))
                return new BotCommandRequest("list", new[] { rest }, text, true);

            return new BotCommandRequest(rest.ToLowerInvariant(), Array.Empty<string>(), text, true);
        }

        bool explicitCommand = text[0] == '/';
        string body = explicitCommand ? text[1..].Trim() : text;
        if (body.Length == 0)
            return BotCommandRequest.Empty;

        var parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return BotCommandRequest.Empty;

        string name = parts[0].ToLowerInvariant();
        string[] args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        return new BotCommandRequest(name, args, text, explicitCommand);
    }

    /// <summary>
    /// 按平台能力裁剪输出文本：官方平台不支持 CQ 码，且单条消息有长度上限。
    /// 返回需要分片发送的文本段（NapCat 通常只有一段）。
    /// </summary>
    public static IReadOnlyList<string> SplitForPlatform(string text, int maxLength, bool supportsCqCode)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        string payload = supportsCqCode ? text : StripCqCodes(text);

        // 官方平台对文本更敏感：统一为 \n，避免 \r 被当作可见字符渲染出来
        if (!supportsCqCode)
            payload = payload.Replace("\r\n", "\n").Replace('\r', '\n');

        payload = payload.Trim();

        if (payload.Length == 0)
            return Array.Empty<string>();

        int limit = maxLength > 0 ? maxLength : payload.Length;
        if (payload.Length <= limit)
            return new[] { payload };

        var chunks = new List<string>();
        int cursor = 0;

        while (cursor < payload.Length)
        {
            int take = Math.Min(limit, payload.Length - cursor);

            // 尽量在换行处断开，其次在空格处，避免把一行数据劈成两半
            if (cursor + take < payload.Length)
            {
                int newline = payload.LastIndexOf('\n', cursor + take - 1, take);
                if (newline > cursor)
                    take = newline - cursor + 1;
                else
                {
                    int space = payload.LastIndexOf(' ', cursor + take - 1, take);
                    if (space > cursor)
                        take = space - cursor + 1;
                }
            }

            chunks.Add(payload.Substring(cursor, take).Trim());
            cursor += take;
        }

        return chunks.Where(c => c.Length > 0).ToList();
    }

    /// <summary>剥离 OneBot CQ 码，官方平台直接发送 CQ 码会被当作纯文本显示。</summary>
    public static string StripCqCodes(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("[CQ:"))
            return text;

        return System.Text.RegularExpressions.Regex.Replace(text, @"\[CQ:[^\]]*\]", "");
    }
}
