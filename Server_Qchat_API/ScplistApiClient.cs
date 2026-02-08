using System.Text.Json;

namespace Server_Qchat_API;

public sealed class ScplistApiClient
{
    private readonly HttpClient _httpClient;

    public ScplistApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<int?> GetPlayersAsync(int serverId, CancellationToken cancellationToken)
    {
        using var resp = await _httpClient.GetAsync($"api/servers/{serverId}", cancellationToken);
        if (!resp.IsSuccessStatusCode)
            return null;

        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("players", out var playersProp))
                return null;

            return playersProp.ValueKind switch
            {
                JsonValueKind.Number when playersProp.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(playersProp.GetString(), out var n) => n,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
