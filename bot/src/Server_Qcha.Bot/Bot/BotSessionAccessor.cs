using EleCho.GoCqHttpSdk;

namespace Server.Qcat.Bot;

// Bridge to allow other services (if any) to send messages without tightly coupling
// to the bot service lifetime.
public sealed class BotSessionAccessor
{
    public CqWsSession? Session { get; internal set; }
}

