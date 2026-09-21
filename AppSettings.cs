using System.Globalization;
using System.Text.Json;

namespace ClaudeUsage;

/// <summary>User preferences stored in %APPDATA%\ClaudeUsage\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsage");

    public static string FilePath { get; } = Path.Combine(Dir, "settings.json");

    /// <summary>"cs", "en" or "auto" (follow the Windows display language).</summary>
    public string Language { get; set; } = "auto";

    public bool TopMost { get; set; }

    /// <summary>"light", "dark" or "auto" (follow the Windows app mode).</summary>
    public string Theme { get; set; } = "auto";

    /// <summary>Text size in percent (100 = default). The menu offers 100–150; any value 50–300 is accepted.</summary>
    public int Scale { get; set; } = 100;

    public float ResolveScale() => Math.Clamp(Scale, 50, 300) / 100f;

    public ThemeMode ResolveThemeMode() => Theme switch
    {
        "light" => ThemeMode.Light,
        "dark" => ThemeMode.Dark,
        _ => ThemeMode.Auto,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings could not be saved; the app keeps running.
        }
    }

    public UiLanguage ResolveLanguage() => Language switch
    {
        "cs" => UiLanguage.Cs,
        "en" => UiLanguage.En,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("cs", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Cs
            : UiLanguage.En,
    };
}
