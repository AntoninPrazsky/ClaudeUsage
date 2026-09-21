using System.Globalization;
using System.Text.Json;

namespace ClaudeUsage;

public static class UsageParser
{
    public static UsageSnapshot Parse(string json, OAuthCredentials creds)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        LimitInfo? session = null;
        LimitInfo? weeklyAll = null;
        var scoped = new List<LimitInfo>();

        // Current format: the "limits" array, the same data Claude Code renders on its /usage screen.
        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in limits.EnumerateArray())
            {
                var kind = GetString(l, "kind") ?? string.Empty;
                var info = new LimitInfo(
                    Kind: kind,
                    ScopeName: kind == "weekly_scoped" ? ScopeName(l) : null,
                    Percent: GetDouble(l, "percent") ?? 0,
                    Severity: GetString(l, "severity") ?? "normal",
                    ResetsAt: GetDate(l, "resets_at"),
                    IsActive: l.TryGetProperty("is_active", out var ia) && ia.ValueKind == JsonValueKind.True);

                switch (kind)
                {
                    case "session": session = info; break;
                    case "weekly_all": weeklyAll = info; break;
                    case "weekly_scoped": scoped.Add(info); break;
                }
            }
        }

        // Older format as a fallback (five_hour / seven_day / seven_day_<model>).
        session ??= Legacy(root, "five_hour", "session", null);
        weeklyAll ??= Legacy(root, "seven_day", "weekly_all", null);
        if (scoped.Count == 0)
        {
            foreach (var (key, name) in new[] { ("seven_day_opus", "Opus"), ("seven_day_sonnet", "Sonnet") })
            {
                var x = Legacy(root, key, "weekly_scoped", name);
                if (x is not null) scoped.Add(x);
            }
        }

        if (session is null && weeklyAll is null && scoped.Count == 0)
        {
            throw new InvalidOperationException(L.Get("err.noLimits"));
        }

        return new UsageSnapshot(session, weeklyAll, scoped, DateTimeOffset.Now, creds.SubscriptionType);
    }

    private static string? ScopeName(JsonElement limit)
    {
        if (limit.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object)
        {
            if (scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
            {
                var name = GetString(model, "display_name") ?? GetString(model, "id");
                if (!string.IsNullOrEmpty(name)) return name;
            }
            var surface = GetString(scope, "surface");
            if (!string.IsNullOrEmpty(surface)) return surface;
        }
        return null;
    }

    private static LimitInfo? Legacy(JsonElement root, string key, string kind, string? scopeName)
    {
        if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
        var pct = GetDouble(el, "utilization");
        if (pct is null) return null;
        return new LimitInfo(kind, scopeName, pct.Value, "normal", GetDate(el, "resets_at"), IsActive: false);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetDouble(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static DateTimeOffset? GetDate(JsonElement el, string name)
    {
        var s = GetString(el, name);
        return s is not null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;
    }
}
