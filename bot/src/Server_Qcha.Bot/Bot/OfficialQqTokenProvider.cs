using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Qcat.Configuration;

namespace Server.Qcat.Bot;

/// <summary>
/// QQ 开放平台 AccessToken 管理器。
///
/// 官方接口 <c>POST {ApiBase}/app/getAppAccessToken</c>：
/// 请求体 <c>{"appId":"...","clientSecret":"..."}</c>，响应 <c>{"access_token":"...","expires_in":"7200"}</c>。
/// 注意 <c>expires_in</c> 官方示例为字符串，但实际可能返回数字，因此这里两者都兼容。
///
/// 官方说明：token 默认有效期 7200 秒，重复请求不会刷新；仅在临近过期 60 秒内请求会签发新 token。
/// 因此这里提前 60 秒续期，并用信号量保证并发下只刷一次。
/// </summary>
public sealed class OfficialQqTokenProvider
{
    private static readonly TimeSpan RefreshAhead = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<OfficialQqOptions> _opts;
    private readonly ILogger<OfficialQqTokenProvider> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public OfficialQqTokenProvider(
        HttpClient http,
        IOptionsMonitor<OfficialQqOptions> opts,
        ILogger<OfficialQqTokenProvider> log)
    {
        _http = http;
        _opts = opts;
        _log = log;
    }

    public DateTimeOffset ExpiresAt => _expiresAt;

    public bool HasValidToken => IsFresh();

    /// <summary>获取可用的 AccessToken；必要时（首次 / 临近过期）先续期。</summary>
    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (IsFresh())
            return _token!;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 双重检查：可能已被其他等待者刷新
            if (IsFresh())
                return _token!;

            await RefreshAsync(ct).ConfigureAwait(false);
            return _token!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>作废当前 token（鉴权失败时调用，下一次取用会重新申请）。</summary>
    public void Invalidate()
    {
        _token = null;
        _expiresAt = DateTimeOffset.MinValue;
    }

    private bool IsFresh()
        => !string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow < _expiresAt - RefreshAhead;

    private async Task RefreshAsync(CancellationToken ct)
    {
        var o = _opts.CurrentValue;
        if (!o.IsConfigured)
            throw new InvalidOperationException("官方 QQ 模式未配置 AppId / ClientSecret，请先在面板或 bot-settings.json 中填写。");

        string url = $"{o.ResolveApiBase()}/app/getAppAccessToken";
        string payload = JsonSerializer.Serialize(new { appId = o.AppId, clientSecret = o.ClientSecret });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"申请 AccessToken 失败：HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}，响应：{Truncate(body)}");

        string? token = null;
        int expiresIn = 7200;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // 腾讯开放平台业务错误判定：即使 HTTP 200，若包含非零 code 则为凭证或业务错误
            if (root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out int code) && code != 0)
            {
                string? msg = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (code == 100016)
                    throw new InvalidOperationException($"QQ 开放平台凭证无效（AppID 或 AppSecret 不正确，code=100016）。请在 QQ 开放平台核对并重新填写。");
                throw new InvalidOperationException($"QQ 开放平台返回错误：{msg ?? "未知错误"}（code={code}）");
            }

            if (root.TryGetProperty("access_token", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.String)
                token = tokenEl.GetString();

            if (root.TryGetProperty("expires_in", out var expEl))
            {
                expiresIn = expEl.ValueKind switch
                {
                    JsonValueKind.Number when expEl.TryGetInt32(out int n) => n,
                    JsonValueKind.String when int.TryParse(expEl.GetString(), out int n2) => n2,
                    _ => 7200,
                };
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"解析 AccessToken 响应失败：{ex.Message}，响应：{Truncate(body)}", ex);
        }

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"AccessToken 响应缺少 access_token 字段，响应：{Truncate(body)}");

        _token = token;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn, 60));

        _log.LogInformation("已获取 QQ 官方 AccessToken（有效期 {Seconds} 秒，将于 {ExpiresAt:HH:mm:ss} 前后自动续期）",
            expiresIn, _expiresAt.ToLocalTime());

        // 官方建议：上一次 token 在临期 60 秒内仍有效，此处无需主动处理
    }

    private static string Truncate(string value)
        => string.IsNullOrEmpty(value) ? "(空)" : (value.Length > 300 ? value[..300] + "..." : value);
}
