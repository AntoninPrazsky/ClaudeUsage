using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ClaudeUsage;

/// <summary>One row: limit name, percentage, progress bar and reset time.</summary>
public sealed class LimitRow : TableLayoutPanel
{
    private readonly Label _title = new();
    private readonly Label _percent = new();
    private readonly BarPanel _bar = new();
    private readonly Label _reset = new();
    private LimitInfo? _info;

    public LimitRow(int width)
    {
        Width = width;
        MinimumSize = new Size(width, 0);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 2;
        RowCount = 3;
        Margin = new Padding(0, 0, 0, 14);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _title.AutoSize = true;
        _title.Font = new Font("Segoe UI Semibold", 10.5f);
        _title.Margin = new Padding(0, 0, 0, 4);
        _title.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

        _percent.AutoSize = true;
        _percent.Font = new Font("Segoe UI Semibold", 10.5f);
        _percent.Margin = new Padding(0, 0, 0, 4);
        _percent.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _percent.TextAlign = ContentAlignment.MiddleRight;

        _bar.Height = 12;
        _bar.Margin = new Padding(0, 0, 0, 5);
        _bar.Dock = DockStyle.Fill;

        _reset.AutoSize = true;
        _reset.ForeColor = Color.FromArgb(96, 96, 96);
        _reset.Margin = Padding.Empty;

        Controls.Add(_title, 0, 0);
        Controls.Add(_percent, 1, 0);
        Controls.Add(_bar, 0, 1);
        SetColumnSpan(_bar, 2);
        Controls.Add(_reset, 0, 2);
        SetColumnSpan(_reset, 2);

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
        _bar.FillColor = ColorFor(info.Percent, info.Severity);
        _percent.ForeColor = info.Percent >= 90 ? _bar.FillColor : Color.FromArgb(32, 32, 32);
        Tick();
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

    private static Color ColorFor(double percent, string severity)
    {
        var sev = severity.ToLowerInvariant();
        if (percent >= 90 || sev is "critical" or "exceeded" or "locked") return Color.FromArgb(217, 54, 62);
        if (percent >= 70 || sev is "warning" or "elevated") return Color.FromArgb(240, 140, 0);
        return Color.FromArgb(204, 120, 92);
    }

    /// <summary>Horizontal bar with rounded corners.</summary>
    private sealed class BarPanel : Control
    {
        private double _percent;

        public BarPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double Percent
        {
            get => _percent;
            set { _percent = Math.Clamp(value, 0, 100); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor { get; set; } = Color.FromArgb(204, 120, 92);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.White);

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var radius = rect.Height;
            using var track = Rounded(rect, radius);
            using var trackBrush = new SolidBrush(Color.FromArgb(232, 230, 226));
            g.FillPath(trackBrush, track);

            var fillWidth = (int)Math.Round(rect.Width * _percent / 100.0);
            if (fillWidth <= 0) return;
            g.SetClip(track);
            using var fillBrush = new SolidBrush(FillColor);
            g.FillRectangle(fillBrush, new Rectangle(rect.X, rect.Y, Math.Max(fillWidth, radius), rect.Height + 1));
            g.ResetClip();
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = Math.Min(radius, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
