using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClaudeUsage;

/// <summary>
/// The About window: what the app is and its version, that it is unofficial, where its source lives, and its licence.
/// Drawn in the current theme and at the main window's text size, the same as the Windows app's.
/// </summary>
public sealed class AboutWindow : Window
{
    private const double TextWidth = 360;

    public AboutWindow(WindowIcon? icon, double scale)
    {
        var t = AppTheme.Current;

        Title = L.Get("about.title");
        Icon = icon;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = t.Background;
        FontSize = 13;

        var text = new StackPanel { Width = TextWidth };
        text.Children.Add(Line(AppInfo.Name, t.Text, 17, 0, FontWeight.SemiBold));
        text.Children.Add(Line(L.F("about.version", AppInfo.Version), t.SecondaryText, 13, 10));
        text.Children.Add(Line(L.Get("about.description"), t.Text, 13, 10));
        text.Children.Add(Line(L.Get("about.unofficial"), t.SecondaryText, 13, 12));
        text.Children.Add(Line(L.Get("about.source"), t.SecondaryText, 13, 0));
        text.Children.Add(new HyperlinkButton
        {
            Content = AppInfo.RepositoryUrl,
            NavigateUri = new Uri(AppInfo.RepositoryUrl),
            Padding = new Thickness(0, 2),
            Margin = new Thickness(0, 0, 0, 10),
        });

        var close = new Button
        {
            Content = L.Get("about.close"),
            IsDefault = true,
            IsCancel = true,
            Padding = new Thickness(14, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        close.Click += (_, _) => Close();
        var footer = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(Line($"{AppInfo.Copyright} · {L.Get("about.license")}", t.SecondaryText, 12, 0));
        text.Children.Add(footer);

        var body = new DockPanel { Margin = new Thickness(20, 18, 22, 16) };
        if (LoadImage() is { } image)
        {
            var picture = new Image { Source = image, Width = 48, Height = 48, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 16, 0) };
            DockPanel.SetDock(picture, Dock.Left);
            body.Children.Add(picture);
        }
        body.Children.Add(text);

        Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = body };
    }

    private static TextBlock Line(string text, IBrush color, double size, double spaceBelow, FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 0, spaceBelow),
    };

    private static Bitmap? LoadImage()
    {
        try
        {
            using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("claudeusage.png");
            return stream is null ? null : new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
