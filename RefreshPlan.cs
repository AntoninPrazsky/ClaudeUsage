namespace ClaudeUsage;

/// <summary>When the next automatic check is due.</summary>
public static class RefreshPlan
{
    /// <summary>How long after a reset the check comes, so the server has surely rolled the window over.</summary>
    public static readonly TimeSpan ResetMargin = TimeSpan.FromSeconds(60);

    /// <summary>The usage endpoint lets one request per 150 s through once it throttles (README, "Rate limit"); a check never comes sooner after the last.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(150);

    /// <summary>
    /// The time of the next automatic check, and whether it is tied to a reset instead of the interval.
    /// While the session or the all-models weekly limit is used up and no usage credits carry the work, the answer cannot
    /// change before that limit resets, so the check waits for the earliest such reset (when the session window rolls over
    /// the row changes even if the weekly limit still blocks) plus <see cref="ResetMargin"/>. Credits being spent move with every
    /// prompt, so with credits on the interval stays. So does a scoped weekly limit alone, a limit with no reset time
    /// or one whose reset has already passed (the server has not rolled it over yet): no tight loop.
    /// </summary>
    public static (DateTimeOffset At, bool ForReset) NextAt(UsageSnapshot snapshot, TimeSpan interval)
    {
        var regular = snapshot.FetchedAt + interval;
        if (snapshot.Credits is not null) return (regular, false);

        DateTimeOffset? reset = null;
        foreach (var limit in new[] { snapshot.Session, snapshot.WeeklyAll })
        {
            if (limit is { Percent: >= 100, ResetsAt: { } at } && at > snapshot.FetchedAt && (reset is null || at < reset))
            {
                reset = at;
            }
        }
        if (reset is null) return (regular, false);

        var target = reset.Value + ResetMargin;
        var earliest = snapshot.FetchedAt + MinGap;
        return (target > earliest ? target : earliest, true);
    }
}
