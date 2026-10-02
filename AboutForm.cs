using System.Diagnostics;

namespace ClaudeUsage;

/// <summary>
/// The About window: what the app is and its version, that it is unofficial, where its source lives, and its licence.
/// Drawn in the current theme and at the main window's text size, scaled by hand like the main window.
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
        layout.SetRowSpan(picture, 7);

        layout.Controls.Add(Line(AppInfo.Name, t.Text, new Font("Segoe UI Semibold", 13f * scale), 0), 1, 0);
        layout.Controls.Add(Line(L.F("about.version", AppInfo.Version), t.SecondaryText, Font, Px(10)), 1, 1);
        layout.Controls.Add(Line(L.Get("about.description"), t.Text, Font, Px(10)), 1, 2);
        layout.Controls.Add(Line(L.Get("about.unofficial"), t.SecondaryText, Font, Px(12)), 1, 3);
        layout.Controls.Add(Line(L.Get("about.source"), t.SecondaryText, Font, 0), 1, 4);

        var link = new LinkLabel
        {
            Text = AppInfo.RepositoryUrl,
            AutoSize = true,
            LinkColor = t.IsDark ? Color.FromArgb(110, 170, 255) : Color.FromArgb(0, 95, 184),
            ActiveLinkColor = t.IsDark ? Color.FromArgb(150, 195, 255) : Color.FromArgb(0, 70, 140),
            VisitedLinkColor = t.IsDark ? Color.FromArgb(110, 170, 255) : Color.FromArgb(0, 95, 184),
            Margin = new Padding(0, Px(2), 0, Px(12)),
        };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(AppInfo.RepositoryUrl) { UseShellExecute = true });
        layout.Controls.Add(link, 1, 5);

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
        layout.Controls.Add(footer, 1, 6);

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

    private int Px(int value) => (int)Math.Round(value * _scale * DeviceDpi / 96f);
}
