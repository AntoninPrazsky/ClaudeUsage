using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace ClaudeUsage;

public enum ThemeMode
{
    Auto,
    Light,
    Dark,
}

/// <summary>
/// Color palette of one theme, the same colors as the Windows app. Menus and the button come from Avalonia's
/// Fluent theme, which follows the same light/dark variant.
/// </summary>
public sealed record AppTheme(
    bool IsDark,
    IBrush Background,
    IBrush Text,
    IBrush SecondaryText,
    IBrush SectionText,
    IBrush Track,
    IBrush Error)
{
    public static readonly AppTheme Light = new(
        IsDark: false,
        Background: Brush(255, 255, 255),
        Text: Brush(32, 32, 32),
        SecondaryText: Brush(96, 96, 96),
        SectionText: Brush(130, 130, 130),
        Track: Brush(232, 230, 226),
        Error: Brush(200, 40, 40));

    public static readonly AppTheme Dark = new(
        IsDark: true,
        Background: Brush(30, 30, 30),
        Text: Brush(240, 240, 240),
        SecondaryText: Brush(170, 170, 170),
        SectionText: Brush(140, 140, 140),
        Track: Brush(58, 58, 58),
        Error: Brush(255, 110, 110));

    public static readonly IBrush Warning = Brush(240, 140, 0);

    /// <summary>The theme currently in use by all controls.</summary>
    public static AppTheme Current { get; private set; } = Light;

    /// <summary>
    /// Sets the Avalonia theme variant. "Auto" leaves it to the platform, which on Linux reads the freedesktop
    /// portal's color-scheme (Raspberry Pi OS: Appearance Settings) and follows it live.
    /// </summary>
    public static void Apply(ThemeMode mode)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        Current = app.ActualThemeVariant == ThemeVariant.Dark ? Dark : Light;
    }

    public static IBrush Brush(byte r, byte g, byte b) => new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));
}
