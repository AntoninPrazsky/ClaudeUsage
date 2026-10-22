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

        var credits = ParseCredits(root);

        if (session is null && weeklyAll is null && scoped.Count == 0 && credits is null)
        {
            throw new InvalidOperationException(L.Get("err.noLimits"));
        }

        return new UsageSnapshot(session, weeklyAll, scoped, DateTimeOffset.Now, creds.SubscriptionType, credits);
    }

    /// <summary>
    /// The usage credits. "spend" is the self-describing copy (amount, currency, exponent) and is preferred;
    /// "extra_usage" is the older shape and the fallback. Every field is optional, and null means nothing to show.
    /// </summary>
    private static CreditsInfo? ParseCredits(JsonElement root)
    {
        JsonElement spend = default, extra = default;
        var hasSpend = root.TryGetProperty("spend", out spend) && spend.ValueKind == JsonValueKind.Object;
        var hasExtra = root.TryGetProperty("extra_usage", out extra) && extra.ValueKind == JsonValueKind.Object;
        if (!hasSpend && !hasExtra) return null;

        var enabled = (hasSpend ? GetBool(spend, "enabled") : null) ?? (hasExtra ? GetBool(extra, "is_enabled") : null) ?? false;
        if (!enabled) return null;

        var spent = (hasSpend ? ReadMoney(spend, "used") : null) ?? (hasExtra ? LegacyMoney(extra, "used_credits") : null);
        if (spent is null) return null;

        Money? cap = null;
        if (hasSpend)
        {
            if (spend.TryGetProperty("cap", out var capEl) && capEl.ValueKind == JsonValueKind.Object) cap = ReadMoney(capEl, "money");
            cap ??= ReadMoney(spend, "limit");
        }
        if (hasExtra) cap ??= LegacyMoney(extra, "monthly_limit");

        var capReached = hasExtra && GetBool(extra, "spend_limit_reached") == true;
        var reason = (hasSpend ? GetString(spend, "disabled_reason") : null) ?? (hasExtra ? GetString(extra, "disabled_reason") : null);
        return new CreditsInfo(spent, cap, capReached, reason);
    }

    /// <summary>{ "amount_minor": 735, "currency": "EUR", "exponent": 2 } under the given name.</summary>
    private static Money? ReadMoney(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var m) || m.ValueKind != JsonValueKind.Object) return null;
        var amount = GetDouble(m, "amount_minor");
        if (amount is null) return null;
        return new Money((long)Math.Round(amount.Value), GetString(m, "currency") ?? "", Exponent(GetDouble(m, "exponent")));
    }

    /// <summary>An amount in minor units straight on the extra_usage object, whose currency and digits sit beside it.</summary>
    private static Money? LegacyMoney(JsonElement extra, string name)
    {
        var amount = GetDouble(extra, name);
        if (amount is null) return null;
        return new Money((long)Math.Round(amount.Value), GetString(extra, "currency") ?? "", Exponent(GetDouble(extra, "decimal_places")));
    }

    private static int Exponent(double? digits) => (int)Math.Clamp(digits ?? 2, 0, 6);

    private static bool? GetBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

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
