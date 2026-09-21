using System.Globalization;

namespace ClaudeUsage;

public enum UiLanguage
{
    Cs,
    En,
}

/// <summary>User interface strings in Czech and English.</summary>
public static class L
{
    private static readonly CultureInfo CsCulture = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-GB");

    public static UiLanguage Current { get; set; } = UiLanguage.En;

    public static CultureInfo Culture => Current == UiLanguage.Cs ? CsCulture : EnCulture;

    public static string Get(string key)
    {
        if (!Texts.TryGetValue(key, out var t)) return key;
        return Current == UiLanguage.Cs ? t.Cs : t.En;
    }

    public static string F(string key, params object[] args) => string.Format(Culture, Get(key), args);

    private static readonly Dictionary<string, (string Cs, string En)> Texts = new()
    {
        ["app.title"] = ("Claude – limity účtu", "Claude – account limits"),
        ["section.weekly"] = ("Weekly limits", "Weekly limits"),

        ["limit.session"] = ("Current session", "Current session"),
        ["limit.weeklyAll"] = ("All models", "All models"),
        ["limit.scopedFallback"] = ("Vybraný model", "Selected model"),
        ["limit.used"] = ("{0} % využito", "{0} % used"),
        ["limit.left"] = ("Zbývá {0} %", "{0} % left"),
        ["limit.reached"] = ("Limit vyčerpán", "Limit reached"),
        ["limit.resets"] = ("{0} · obnoví se za {1} ({2})", "{0} · resets in {1} ({2})"),
        ["limit.noReset"] = ("{0} · zatím bez naplánovaného obnovení", "{0} · no reset scheduled yet"),

        ["time.moment"] = ("chvilku", "a moment"),
        ["time.dh"] = ("{0} d {1} h", "{0} d {1} h"),
        ["time.hm"] = ("{0} h {1} min", "{0} h {1} min"),
        ["time.m"] = ("{0} min", "{0} min"),
        ["time.s"] = ("{0} s", "{0} s"),
        ["time.today"] = ("dnes {0}", "today {0}"),
        ["time.tomorrow"] = ("zítra {0}", "tomorrow {0}"),
        ["time.clock"] = ("H:mm", "HH:mm"),
        ["time.date"] = ("ddd d. M. H:mm", "ddd d MMM HH:mm"),

        ["status.loading"] = ("Načítám…", "Loading…"),
        ["status.updated"] = ("Aktualizováno {0}", "Updated {0}"),
        ["status.next"] = ("další za {0} s", "next in {0} s"),
        ["status.refreshing"] = ("obnovuji…", "refreshing…"),
        ["button.refresh"] = ("Obnovit teď", "Refresh now"),

        ["menu.settings"] = ("Nastavení", "Settings"),
        ["menu.language"] = ("Jazyk", "Language"),
        ["menu.lang.cs"] = ("Čeština", "Čeština"),
        ["menu.lang.en"] = ("English", "English"),
        ["menu.topMost"] = ("Vždy navrchu", "Always on top"),
        ["menu.minimizeToTray"] = ("Minimalizovat do lišty", "Minimize to tray"),
        ["menu.exit"] = ("Ukončit", "Exit"),

        ["tray.show"] = ("Zobrazit", "Show"),
        ["tray.refresh"] = ("Obnovit", "Refresh"),
        ["tray.session"] = ("Relace", "Session"),
        ["tray.balloon"] = (
            "Aplikace běží dál v systémové liště. Kliknutím na ikonu ji zobrazíte.",
            "The app keeps running in the system tray. Click the icon to show it."),

        ["err.unexpected"] = ("Neočekávaná chyba: {0}", "Unexpected error: {0}"),
        ["err.noCredsFile"] = (
            "Nenalezen soubor s přihlášením:\n{0}\n\nPřihlaste se v Claude Code (spusťte „claude“ a proveďte /login).",
            "Credentials file not found:\n{0}\n\nSign in with Claude Code (run “claude” and use /login)."),
        ["err.invalidJson"] = ("Soubor s přihlášením není platný JSON.", "The credentials file is not valid JSON."),
        ["err.noOauth"] = (
            "V souboru s přihlášením chybí sekce claudeAiOauth. Přihlaste se v Claude Code (/login).",
            "The credentials file has no claudeAiOauth section. Sign in with Claude Code (/login)."),
        ["err.noTokens"] = (
            "V souboru s přihlášením chybí tokeny. Přihlaste se v Claude Code (/login).",
            "The credentials file contains no tokens. Sign in with Claude Code (/login)."),
        ["err.http"] = ("Server vrátil HTTP {0}: {1}", "Server returned HTTP {0}: {1}"),
        ["err.parse"] = ("Odpověď serveru se nepodařilo zpracovat: {0}", "Could not parse the server response: {0}"),
        ["err.noLimits"] = ("odpověď neobsahuje žádné limity.", "the response contains no limits."),
        ["err.connect"] = ("Nepodařilo se připojit k api.anthropic.com: {0}", "Could not connect to api.anthropic.com: {0}"),
        ["err.timeout"] = ("Server neodpověděl včas (timeout).", "The server did not respond in time (timeout)."),
        ["err.refreshConnect"] = (
            "Přístupový token vypršel a nepodařilo se připojit k platform.claude.com: {0}",
            "The access token expired and platform.claude.com could not be reached: {0}"),
        ["err.refreshFailed"] = (
            "Přístupový token vypršel a obnova selhala (HTTP {0}).\nSpusťte v terminálu „claude“ – Claude Code přihlášení obnoví a tato aplikace ho převezme.\n{1}",
            "The access token expired and refreshing it failed (HTTP {0}).\nRun “claude” in a terminal – Claude Code will sign in again and this app will pick it up.\n{1}"),
        ["err.refreshNoToken"] = (
            "Obnova tokenu vrátila odpověď bez access_token.",
            "The token refresh response contained no access_token."),
    };
}
