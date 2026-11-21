using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ClaudeUsage;

/// <summary>
/// The About window: what the app is and its version, that it is unofficial, where its source lives, and its licence,
/// then a little animation with a link to the author's game (<see cref="AboutScene"/>). Drawn in the current theme and
/// at the main window's text size, the same as the Windows app's.
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
        text.Children.Add(Link(AppInfo.RepositoryUrl, AppInfo.RepositoryUrl, 12));

        var scene = new SceneView(TextWidth, scale, t.IsDark) { Margin = new Thickness(0, 0, 0, 6) };
        scene.PointerPressed += (_, _) => _ = Launcher.LaunchUriAsync(new Uri(AppInfo.GameUrl));
        text.Children.Add(scene);
        text.Children.Add(Line(L.Get("about.game"), t.SecondaryText, 13, 0));
        text.Children.Add(Link(L.Get("about.gameLink"), AppInfo.GameUrl, 10));

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

    private static HyperlinkButton Link(string text, string url, double spaceBelow) => new()
    {
        Content = text,
        NavigateUri = new Uri(url),
        Padding = new Thickness(0, 2),
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

    /// <summary>
    /// Shows <see cref="AboutScene"/> as large as fits the text column at a whole-number zoom in device pixels, so its
    /// pixels stay square and even, and redraws it about 30 times a second while the window is open. The text size is a
    /// layout transform around the window's content and the monitor adds its own scaling, so the buffer is sized from
    /// both and the control is given the logical size that maps it to whole device pixels.
    /// </summary>
    private sealed class SceneView : Control
    {
        private readonly double _width;
        private readonly double _scale;
        private readonly bool _dark;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly Stopwatch _clock = new();
        private AboutScene? _scene;
        private WriteableBitmap? _bitmap;
        private double _builtFor;
        private TopLevel? _topLevel;

        public SceneView(double width, double scale, bool dark)
        {
            _width = width;
            _scale = scale;
            _dark = dark;
            HorizontalAlignment = HorizontalAlignment.Left;
            Cursor = new Cursor(StandardCursorType.Hand);
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
            _timer.Tick += (_, _) => NextFrame();
            Build(1);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _topLevel = TopLevel.GetTopLevel(this);
            if (_topLevel is not null) _topLevel.ScalingChanged += OnScalingChanged;
            Build(_topLevel?.RenderScaling ?? 1);
            _clock.Start();
            _timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _timer.Stop();
            _clock.Stop();
            if (_topLevel is not null) _topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _scene = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnScalingChanged(object? sender, EventArgs e) => Build(_topLevel?.RenderScaling ?? 1);

        private void Build(double renderScaling)
        {
            if (_scene is not null && renderScaling == _builtFor) return;
            _builtFor = renderScaling;
            var deviceScale = _scale * renderScaling;
            var (zoom, w, h) = AboutScene.Fit(_width * deviceScale);
            _scene = new AboutScene(w, h, _dark);
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(_scene.Width, _scene.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            Width = _scene.Width * zoom / deviceScale;
            Height = _scene.Height * zoom / deviceScale;
            NextFrame();
        }

        private void NextFrame()
        {
            if (_scene is null || _bitmap is null) return;
            _scene.Render(_clock.Elapsed.TotalSeconds);
            using (var frame = _bitmap.Lock())
            {
                for (var y = 0; y < _scene.Height; y++)
                {
                    Marshal.Copy(_scene.Pixels, y * _scene.Width, frame.Address + y * frame.RowBytes, _scene.Width);
                }
            }
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (_bitmap is null) return;
            context.DrawImage(_bitmap, new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height), new Rect(Bounds.Size));
        }
    }
}
