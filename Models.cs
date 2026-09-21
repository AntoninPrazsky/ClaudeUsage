namespace ClaudeUsage;

/// <summary>One rate limit: the session window, the weekly all-models window, or a weekly per-model window.</summary>
public sealed record LimitInfo(
    string Kind,
    string? ScopeName,
    double Percent,
    string Severity,
    DateTimeOffset? ResetsAt,
    bool IsActive)
{
    /// <summary>Stable key independent of the UI language (used to reuse rows between refreshes).</summary>
    public string Key => ScopeName is null ? Kind : $"{Kind}:{ScopeName}";

    /// <summary>Display name of the limit in the current UI language.</summary>
    public string Label => Kind switch
    {
        "session" => L.Get("limit.session"),
        "weekly_all" => L.Get("limit.weeklyAll"),
        "weekly_scoped" => ScopeName ?? L.Get("limit.scopedFallback"),
        _ => ScopeName ?? Kind,
    };
}

/// <summary>Account usage at one point in time.</summary>
public sealed record UsageSnapshot(
    LimitInfo? Session,
    LimitInfo? WeeklyAll,
    IReadOnlyList<LimitInfo> WeeklyScoped,
    DateTimeOffset FetchedAt,
    string? SubscriptionType);

/// <summary>OAuth credentials taken over from Claude Code.</summary>
public sealed record OAuthCredentials(
    string AccessToken,
    string RefreshToken,
    long ExpiresAtUnixMs,
    string[] Scopes,
    string? SubscriptionType)
{
    public DateTimeOffset ExpiresAt => DateTimeOffset.FromUnixTimeMilliseconds(ExpiresAtUnixMs);

    public bool IsExpired(TimeSpan margin) => ExpiresAt - margin <= DateTimeOffset.UtcNow;
}

public class UsageException : Exception
{
    public UsageException(string message) : base(message) { }
    public UsageException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The usage endpoint answered HTTP 429. <see cref="RetryAfter"/> is the Retry-After header, when the server sent a usable one.</summary>
public sealed class RateLimitedException : UsageException
{
    public TimeSpan? RetryAfter { get; }

    public RateLimitedException(string message, TimeSpan? retryAfter) : base(message)
    {
        RetryAfter = retryAfter;
    }
}
