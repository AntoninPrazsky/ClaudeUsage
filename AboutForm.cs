using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClaudeUsage;

/// <summary>
/// The About window: what the app is and its version, that it is unofficial, where its source lives, and its licence,
/// then a little animation with a link to the author's game (<see cref="AboutScene"/>). Drawn in the current theme and
/// at the main window's text size, scaled by hand like the main window.
/// </summary>
public sealed class AboutForm : Form
{
    private const int TextWidth = 360;

    private readonly float _scale;

    public AboutForm(Icon? icon, float scale)
    {
        _scale = scale;
        var t = Theme.Current;

        Text = L.Get("about.title");
        if (icon is not null) Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = t.Background;
        ForeColor = t.Text;
        Font = new Font("Segoe UI", 9.75f * scale);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(Px(20), Px(18), Px(22), Px(16)),
            BackColor = t.Background,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var picture = new PictureBox
        {
            Image = icon is null ? null : new Icon(icon, Px(48), Px(48)).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(Px(48), Px(48)),
            Margin = new Padding(0, Px(2), Px(16), 0),
        };
        layout.Controls.Add(picture, 0, 0);
        layout.SetRowSpan(picture, 10);

        layout.Controls.Add(Line(AppInfo.Name, t.Text, new Font("Segoe UI Semibold", 13f * scale), 0), 1, 0);
        layout.Controls.Add(Line(L.F("about.version", AppInfo.Version), t.SecondaryText, Font, Px(10)), 1, 1);
        layout.Controls.Add(Line(L.Get("about.description"), t.Text, Font, Px(10)), 1, 2);
        layout.Controls.Add(Line(L.Get("about.unofficial"), t.SecondaryText, Font, Px(12)), 1, 3);
        layout.Controls.Add(Line(L.Get("about.source"), t.SecondaryText, Font, 0), 1, 4);
        layout.Controls.Add(Link(AppInfo.RepositoryUrl, AppInfo.RepositoryUrl, Px(14)), 1, 5);

        var scene = new SceneView(Px(TextWidth), t.IsDark) { Margin = new Padding(0, 0, 0, Px(6)) };
        scene.Click += (_, _) => Open(AppInfo.GameUrl);
        layout.Controls.Add(scene, 1, 6);
        layout.Controls.Add(Line(L.Get("about.game"), t.SecondaryText, Font, 0), 1, 7);
        layout.Controls.Add(Link(L.Get("about.gameLink"), AppInfo.GameUrl, Px(12)), 1, 8);

        var footer = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(Line($"{AppInfo.Copyright} · {L.Get("about.license")}", t.SecondaryText, Font, 0), 0, 0);

        var close = new Button
        {
            Text = L.Get("about.close"),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = t.ButtonBackground,
            ForeColor = t.Text,
            Padding = new Padding(Px(10), Px(2), Px(10), Px(2)),
            Margin = new Padding(Px(16), 0, 0, 0),
            Anchor = AnchorStyles.Right,
            DialogResult = DialogResult.OK,
        };
        close.FlatAppearance.BorderColor = t.ButtonBorder;
        close.FlatAppearance.MouseOverBackColor = t.MenuHover;
        close.FlatAppearance.MouseDownBackColor = t.MenuBorder;
        footer.Controls.Add(close, 1, 0);
        layout.Controls.Add(footer, 1, 9);

        AcceptButton = close;
        CancelButton = close;
        Controls.Add(layout);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this, Theme.Current.IsDark);
    }

    private Label Line(string text, Color color, Font font, int spaceBelow) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(Px(TextWidth), 0),
        ForeColor = color,
        Font = font,
        Margin = new Padding(0, 0, 0, spaceBelow),
    };

    private LinkLabel Link(string text, string url, int spaceBelow)
    {
        var dark = Theme.Current.IsDark;
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = dark ? Color.FromArgb(110, 170, 255) : Color.FromArgb(0, 95, 184),
            ActiveLinkColor = dark ? Color.FromArgb(150, 195, 255) : Color.FromArgb(0, 70, 140),
            VisitedLinkColor = dark ? Color.FromArgb(110, 170, 255) : Color.FromArgb(0, 95, 184),
            Margin = new Padding(0, Px(2), 0, spaceBelow),
        };
        link.LinkClicked += (_, _) => Open(url);
        return link;
    }

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private int Px(int value) => (int)Math.Round(value * _scale * DeviceDpi / 96f);

    /// <summary>
    /// Shows <see cref="AboutScene"/> as large as fits <paramref name="width"/> at a whole-number zoom, so its pixels stay
    /// square and even, and redraws it about 30 times a second while the window is open.
    /// </summary>
    private sealed class SceneView : Control
    {
        private readonly AboutScene _scene;
        private readonly int _zoom;
        private readonly Bitmap _bitmap;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
        private readonly Stopwatch _clock = new();

        public SceneView(int width, bool dark)
        {
            var (zoom, w, h) = AboutScene.Fit(width);
            _zoom = zoom;
            _scene = new AboutScene(w, h, dark);
            _bitmap = new Bitmap(_scene.Width, _scene.Height, PixelFormat.Format32bppRgb);
            Size = new Size(_scene.Width * zoom, _scene.Height * zoom);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Animation;
            AccessibleName = L.Get("about.gameLink");
            _timer.Tick += (_, _) => NextFrame();
            NextFrame();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _clock.Start();
            _timer.Start();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _timer.Stop();
            _clock.Stop();
            base.OnHandleDestroyed(e);
        }

        private void NextFrame()
        {
            _scene.Render(_clock.Elapsed.TotalSeconds);
            var data = _bitmap.LockBits(new Rectangle(0, 0, _bitmap.Width, _bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (var y = 0; y < _scene.Height; y++)
                {
                    Marshal.Copy(_scene.Pixels, y * _scene.Width, data.Scan0 + y * data.Stride, _scene.Width);
                }
            }
            finally
            {
                _bitmap.UnlockBits(data);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half; // without it the first row and column come out half a pixel short
            g.DrawImage(_bitmap, new Rectangle(0, 0, _bitmap.Width * _zoom, _bitmap.Height * _zoom));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _bitmap.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
