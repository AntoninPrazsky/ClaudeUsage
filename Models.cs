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
    string? SubscriptionType,
    CreditsInfo? Credits = null);

/// <summary>An amount as the API sends it: whole minor units (cents), the currency and the number of digits the minor unit has.</summary>
public sealed record Money(long AmountMinor, string Currency, int Exponent)
{
    public decimal Value
    {
        get
        {
            var divisor = 1m;
            for (var i = 0; i < Exponent; i++) divisor *= 10;
            return AmountMinor / divisor;
        }
    }

    /// <summary>The number alone, in the UI culture ("2 000,00" in Czech, "2,000.00" in English).</summary>
    public string Plain() => Value.ToString("N" + Exponent, L.Culture);

    /// <summary>The number with its currency code ("7,35 EUR").</summary>
    public string Format() => Currency.Length > 0 ? $"{Plain()} {Currency}" : Plain();
}

/// <summary>
/// The usage credits ("extra usage"): what has been spent against the monthly cap once a plan limit is used up.
/// <see cref="UsageSnapshot.Credits"/> is null when the account has them switched off or the response says nothing about them.
/// </summary>
public sealed record CreditsInfo(Money Spent, Money? Cap, bool CapReached, string? DisabledReason)
{
    public string Label => L.Get("credits.title");

    /// <summary>Share of the cap spent, from the amounts (the response's own percent is a rounded integer); null without a cap.</summary>
    public double? Percent => Cap is { AmountMinor: > 0 } cap ? Spent.AmountMinor * 100.0 / cap.AmountMinor : null;

    public string PercentText()
    {
        if (Percent is not { } p) return "";
        // Below 1 % a rounded integer would read "0 %" beside a non-zero amount.
        var shown = p switch
        {
            <= 0 => "0",
            < 0.1 => "<" + 0.1.ToString("0.0", L.Culture),
            < 1 => p.ToString("0.0", L.Culture),
            _ => Math.Round(p).ToString("0", L.Culture),
        };
        return L.F("limit.used", shown);
    }

    public string DetailText() => Cap is { } cap
        ? L.F("credits.detail", Spent.Plain(), cap.Format())
        : L.F("credits.detailNoCap", Spent.Format());

    /// <summary>The short form for the tray tooltip, which Windows cuts at 127 characters.</summary>
    public string TrayText() => Cap is { } cap
        ? $"{L.Get("tray.credits")} {Spent.Plain()}/{cap.Format()}"
        : $"{L.Get("tray.credits")} {Spent.Format()}";
}

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
