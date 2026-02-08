namespace Server_Qchat_API;

public sealed class AppOptions
{
    public string WsBaseUri { get; set; } = "ws://localhost:6700";

    // Trigger when group message text contains this keyword (case-insensitive).
    public string CommandKeyword { get; set; } = "cx";

    // Used when user doesn't provide servers interactively.
    public int[] DefaultServers { get; set; } = Array.Empty<int>();
}
