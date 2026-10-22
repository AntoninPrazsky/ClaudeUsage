namespace ClaudeUsage;

public enum CreditWarningLevel
{
    None = 0,
    NearCap = 1,
    CapReached = 2,
}

/// <summary>When the usage credits are worth a notification: once per level, on the way up.</summary>
public static class CreditWarning
{
    /// <summary>Share of the monthly cap at which the first warning comes.</summary>
    public const double NearCapPercent = 80;

    public static CreditWarningLevel LevelOf(CreditsInfo credits) =>
        credits.CapReached || credits.Percent >= 100 ? CreditWarningLevel.CapReached
        : credits.Percent >= NearCapPercent ? CreditWarningLevel.NearCap
        : CreditWarningLevel.None;

    /// <summary>
    /// Compares the level the credits are at now with the one seen last. <c>Announce</c> is the level to tell the user about,
    /// or None when it has not risen; <c>Seen</c> is what to remember. A level that falls (a new period, a higher cap, credits
    /// switched off) arms the warnings again, and remembering <c>Seen</c> across restarts keeps a start-up from announcing
    /// the same level twice.
    /// </summary>
    public static (CreditWarningLevel Announce, CreditWarningLevel Seen) Next(CreditWarningLevel lastSeen, CreditsInfo? current)
    {
        var seen = current is null ? CreditWarningLevel.None : LevelOf(current);
        return (seen > lastSeen ? seen : CreditWarningLevel.None, seen);
    }

    public static string Message(CreditWarningLevel level, CreditsInfo credits) => level switch
    {
        CreditWarningLevel.CapReached => L.Get("warn.capReached"),
        CreditWarningLevel.NearCap => L.F("warn.nearCap", Math.Round(credits.Percent ?? NearCapPercent)),
        _ => "",
    };
}
