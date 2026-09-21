using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClaudeUsage;

/// <summary>
/// Calls the same endpoint Claude Code's /usage command reads from and, when needed,
/// refreshes the access token with the refresh token (same OAuth client as Claude Code).
/// </summary>
public sealed class UsageClient
{
    // CLAUDEUSAGE_USAGE_URL lets developers point the app at a local mock server (the real endpoint is rate limited).
    private static readonly string UsageUrl =
        Environment.GetEnvironmentVariable("CLAUDEUSAGE_USAGE_URL") is { Length: > 0 } url
            ? url
            : "https://api.anthropic.com/api/oauth/usage";
    private const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string OAuthBeta = "oauth-2025-04-20";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = typeof(UsageClient).Assembly.GetName().Version ?? new Version(0, 0);
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeUsage/{version.Major}.{version.Minor} (Windows)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        var creds = CredentialStore.Load();

        if (creds.IsExpired(TimeSpan.FromMinutes(1)))
        {
            creds = await RefreshAsync(creds, ct);
        }

        var (status, body, retryAfter) = await GetUsageRawAsync(creds.AccessToken, ct);

        if (status == HttpStatusCode.Unauthorized)
        {
            // The token may have been invalidated meanwhile. Re-read the file (Claude Code may have refreshed it) and retry once.
            creds = await RefreshAsync(CredentialStore.Load(), ct);
            (status, body, retryAfter) = await GetUsageRawAsync(creds.AccessToken, ct);
        }

        if (status == HttpStatusCode.TooManyRequests)
        {
            throw new RateLimitedException(L.Get("err.rateLimited"), retryAfter);
        }

        if (status != HttpStatusCode.OK)
        {
            throw new UsageException(L.F("err.http", (int)status, Truncate(body, 300)));
        }

        try
        {
            return UsageParser.Parse(body, creds);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new UsageException(L.F("err.parse", ex.Message), ex);
        }
    }

    private static async Task<(HttpStatusCode Status, string Body, TimeSpan? RetryAfter)> GetUsageRawAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", OAuthBeta);

        try
        {
            using var response = await Http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return (response.StatusCode, body, ParseRetryAfter(response.Headers.RetryAfter));
        }
        catch (HttpRequestException ex)
        {
            throw new UsageException(L.F("err.connect", ex.Message), ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new UsageException(L.Get("err.timeout"), ex);
        }
    }

    private static async Task<OAuthCredentials> RefreshAsync(OAuthCredentials creds, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = creds.RefreshToken,
            ["client_id"] = ClientId,
        };
        if (creds.Scopes.Length > 0)
        {
            payload["scope"] = string.Join(" ", creds.Scopes);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        HttpStatusCode status;
        string body;
        try
        {
            using var response = await Http.SendAsync(request, ct);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UsageException(L.F("err.refreshConnect", ex.Message), ex);
        }

        if (status != HttpStatusCode.OK)
        {
            throw new UsageException(L.F("err.refreshFailed", (int)status, Truncate(body, 200)));
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(access))
        {
            throw new UsageException(L.Get("err.refreshNoToken"));
        }
        var refresh = root.TryGetProperty("refresh_token", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()!
            : creds.RefreshToken;
        long expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
            ? e.GetInt64()
            : 3600;
        long expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds();

        CredentialStore.Save(access, refresh, expiresAt);
        return creds with { AccessToken = access, RefreshToken = refresh, ExpiresAtUnixMs = expiresAt };
    }

    /// <summary>Returns the Retry-After delay, or null when the header is missing or zero (the usage endpoint sends "0").</summary>
    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? header)
    {
        if (header?.Delta is { } delta) return delta > TimeSpan.Zero ? delta : null;
        if (header?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : null;
        }
        return null;
    }

    private static string Truncate(string s, int max)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }
}
