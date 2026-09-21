using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClaudeUsage;

public enum ThemeMode
{
    Auto,
    Light,
    Dark,
}

/// <summary>Color palette of one theme plus helpers to resolve and apply the active one.</summary>
public sealed record Theme(
    bool IsDark,
    Color Background,
    Color Text,
    Color SecondaryText,
    Color SectionText,
    Color Track,
    Color Error,
    Color MenuBackground,
    Color MenuText,
    Color MenuHover,
    Color MenuBorder,
    Color ButtonBackground,
    Color ButtonBorder)
{
    public static readonly Theme Light = new(
        IsDark: false,
        Background: Color.White,
        Text: Color.FromArgb(32, 32, 32),
        SecondaryText: Color.FromArgb(96, 96, 96),
        SectionText: Color.FromArgb(130, 130, 130),
        Track: Color.FromArgb(232, 230, 226),
        Error: Color.FromArgb(200, 40, 40),
        MenuBackground: Color.White,
        MenuText: Color.FromArgb(32, 32, 32),
        MenuHover: Color.FromArgb(232, 232, 232),
        MenuBorder: Color.FromArgb(208, 208, 208),
        ButtonBackground: Color.FromArgb(245, 245, 245),
        ButtonBorder: Color.FromArgb(200, 200, 200));

    public static readonly Theme Dark = new(
        IsDark: true,
        Background: Color.FromArgb(30, 30, 30),
        Text: Color.FromArgb(240, 240, 240),
        SecondaryText: Color.FromArgb(170, 170, 170),
        SectionText: Color.FromArgb(140, 140, 140),
        Track: Color.FromArgb(58, 58, 58),
        Error: Color.FromArgb(255, 110, 110),
        MenuBackground: Color.FromArgb(43, 43, 43),
        MenuText: Color.FromArgb(240, 240, 240),
        MenuHover: Color.FromArgb(62, 62, 62),
        MenuBorder: Color.FromArgb(70, 70, 70),
        ButtonBackground: Color.FromArgb(51, 51, 51),
        ButtonBorder: Color.FromArgb(90, 90, 90));

    /// <summary>The theme currently in use by all controls.</summary>
    public static Theme Current { get; private set; } = Light;

    public static void Apply(ThemeMode mode) => Current = Resolve(mode);

    public static Theme Resolve(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => Light,
        ThemeMode.Dark => Dark,
        _ => SystemUsesDarkMode() ? Dark : Light,
    };

    /// <summary>True when Windows is set to "Choose your default app mode: Dark".</summary>
    public static bool SystemUsesDarkMode()
    {
        try
        {
            const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            return Registry.GetValue(key, "AppsUseLightTheme", 1) is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Switches the native title bar between light and dark (Windows 10 20H1 and later).</summary>
    public static void ApplyTitleBar(IWin32Window window, bool dark)
    {
        var value = dark ? 1 : 0;
        try
        {
            _ = DwmSetWindowAttribute(window.Handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows without the attribute: keep the default title bar.
        }
    }
}

/// <summary>Draws menus (menu bar, drop-downs, tray context menu) with the theme colors.</summary>
public sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly Theme _theme;

    public ThemedMenuRenderer(Theme theme) : base(new ThemedColorTable(theme))
    {
        _theme = theme;
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _theme.MenuText : _theme.SectionText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _theme.MenuText;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var background = new SolidBrush(_theme.MenuHover);
        g.FillRectangle(background, r);

        using var pen = new Pen(_theme.MenuText, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[]
        {
            new PointF(r.Left + r.Width * 0.25f, r.Top + r.Height * 0.55f),
            new PointF(r.Left + r.Width * 0.43f, r.Top + r.Height * 0.73f),
            new PointF(r.Left + r.Width * 0.77f, r.Top + r.Height * 0.32f),
        });
    }

    private sealed class ThemedColorTable : ProfessionalColorTable
    {
        private readonly Theme _t;

        public ThemedColorTable(Theme theme)
        {
            _t = theme;
            UseSystemColors = false;
        }

        public override Color MenuStripGradientBegin => _t.Background;
        public override Color MenuStripGradientEnd => _t.Background;
        public override Color ToolStripDropDownBackground => _t.MenuBackground;
        public override Color ImageMarginGradientBegin => _t.MenuBackground;
        public override Color ImageMarginGradientMiddle => _t.MenuBackground;
        public override Color ImageMarginGradientEnd => _t.MenuBackground;
        public override Color MenuBorder => _t.MenuBorder;
        public override Color MenuItemBorder => _t.MenuHover;
        public override Color MenuItemSelected => _t.MenuHover;
        public override Color MenuItemSelectedGradientBegin => _t.MenuHover;
        public override Color MenuItemSelectedGradientEnd => _t.MenuHover;
        public override Color MenuItemPressedGradientBegin => _t.MenuHover;
        public override Color MenuItemPressedGradientMiddle => _t.MenuHover;
        public override Color MenuItemPressedGradientEnd => _t.MenuHover;
        public override Color SeparatorDark => _t.MenuBorder;
        public override Color SeparatorLight => _t.MenuBackground;
        public override Color CheckBackground => _t.MenuHover;
        public override Color CheckSelectedBackground => _t.MenuHover;
        public override Color CheckPressedBackground => _t.MenuHover;
        public override Color ToolStripBorder => _t.MenuBorder;
    }
}
