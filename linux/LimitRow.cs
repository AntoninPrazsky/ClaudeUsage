using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClaudeUsage;

/// <summary>One row: limit name, percentage, progress bar and reset time.</summary>
public sealed class LimitRow : Grid
{
    private readonly TextBlock _title = new();
    private readonly TextBlock _percent = new();
    private readonly BarControl _bar = new();
    private readonly TextBlock _reset = new();
    private LimitInfo? _info;

    public LimitRow()
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
        Margin = new Thickness(0, 0, 0, 14);

        _title.FontSize = 14;
        _title.FontWeight = FontWeight.SemiBold;
        _title.Margin = new Thickness(0, 0, 0, 4);
        _title.VerticalAlignment = VerticalAlignment.Bottom;

        _percent.FontSize = 14;
        _percent.FontWeight = FontWeight.SemiBold;
        _percent.Margin = _title.Margin;
        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        _percent.VerticalAlignment = VerticalAlignment.Bottom;

        _bar.Height = 12;
        _bar.Margin = new Thickness(0, 0, 0, 5);

        _reset.FontSize = 13;

        SetColumn(_percent, 1);
        SetRow(_bar, 1);
        SetColumnSpan(_bar, 2);
        SetRow(_reset, 2);
        SetColumnSpan(_reset, 2);
        Children.Add(_title);
        Children.Add(_percent);
        Children.Add(_bar);
        Children.Add(_reset);

        ApplyTheme();
        Set(null);
    }

    public void Set(LimitInfo? info)
    {
        _info = info;
        if (info is null)
        {
            _title.Text = "—";
            _percent.Text = "";
            _bar.Percent = 0;
            _reset.Text = "";
            return;
        }

        _title.Text = info.Label;
        _percent.Text = L.F("limit.used", Math.Round(info.Percent));
        _bar.Percent = info.Percent;
        _bar.Fill = ColorFor(info.Percent, info.Severity);
        _percent.Foreground = info.Percent >= 90 ? _bar.Fill : AppTheme.Current.Text;
        Tick();
    }

    /// <summary>Re-applies the colors of <see cref="AppTheme.Current"/>.</summary>
    public void ApplyTheme()
    {
        var t = AppTheme.Current;
        _title.Foreground = t.Text;
        _reset.Foreground = t.SecondaryText;
        _percent.Foreground = _info is { Percent: >= 90 } ? _bar.Fill : t.Text;
        _bar.InvalidateVisual();
    }

    /// <summary>Recomputes the relative time until reset (called every second).</summary>
    public void Tick()
    {
        if (_info is null) return;
        var remaining = Math.Max(0, 100 - _info.Percent);
        var prefix = _info.Percent >= 100 ? L.Get("limit.reached") : L.F("limit.left", Math.Round(remaining));
        _reset.Text = _info.ResetsAt is { } at
            ? L.F("limit.resets", prefix, Relative(at), Absolute(at))
            : L.F("limit.noReset", prefix);
    }

    public static string Relative(DateTimeOffset at)
    {
        var delta = at - DateTimeOffset.Now;
        if (delta <= TimeSpan.Zero) return L.Get("time.moment");
        if (delta.TotalDays >= 1) return L.F("time.dh", (int)delta.TotalDays, delta.Hours);
        if (delta.TotalHours >= 1) return L.F("time.hm", (int)delta.TotalHours, delta.Minutes);
        if (delta.TotalMinutes >= 1) return L.F("time.m", (int)delta.TotalMinutes);
        return L.F("time.s", delta.Seconds);
    }

    public static string Absolute(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        var today = DateTime.Today;
        if (local.Date == today) return L.F("time.today", local.ToString(L.Get("time.clock"), L.Culture));
        if (local.Date == today.AddDays(1)) return L.F("time.tomorrow", local.ToString(L.Get("time.clock"), L.Culture));
        return local.ToString(L.Get("time.date"), L.Culture);
    }

    private static IBrush ColorFor(double percent, string severity)
    {
        var sev = severity.ToLowerInvariant();
        if (percent >= 90 || sev is "critical" or "exceeded" or "locked") return AppTheme.Brush(217, 54, 62);
        if (percent >= 70 || sev is "warning" or "elevated") return AppTheme.Warning;
        return AppTheme.Brush(204, 120, 92);
    }

    /// <summary>Horizontal bar with rounded ends.</summary>
    private sealed class BarControl : Control
    {
        private double _percent;

        public double Percent
        {
            get => _percent;
            set { _percent = Math.Clamp(value, 0, 100); InvalidateVisual(); }
        }

        public IBrush Fill { get; set; } = AppTheme.Brush(204, 120, 92);

        public override void Render(DrawingContext context)
        {
            var rect = new Rect(Bounds.Size);
            var radius = rect.Height / 2;
            context.DrawRectangle(AppTheme.Current.Track, null, rect, radius, radius);

            var fillWidth = rect.Width * _percent / 100.0;
            if (fillWidth <= 0) return;
            using (context.PushClip(new RoundedRect(rect, radius)))
            {
                context.DrawRectangle(Fill, null, new Rect(0, 0, Math.Max(fillWidth, rect.Height), rect.Height));
            }
        }
    }
}
