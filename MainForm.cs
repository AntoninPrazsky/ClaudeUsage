using System.Globalization;
using Microsoft.Win32;

namespace ClaudeUsage;

public sealed class MainForm : Form
{
    private const int ContentWidth = 420;
    private const int RefreshIntervalMs = 60_000;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly UsageClient _client = new();
    private readonly System.Windows.Forms.Timer _fetchTimer = new() { Interval = RefreshIntervalMs };
    private readonly System.Windows.Forms.Timer _tickTimer = new() { Interval = 1_000 };
    private readonly Icon? _appIcon;

    // Window menu
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _settingsMenu = new();
    private readonly ToolStripMenuItem _languageMenu = new();
    private readonly ToolStripMenuItem _langCs = new();
    private readonly ToolStripMenuItem _langEn = new();
    private readonly ToolStripMenuItem _themeMenu = new();
    private readonly ToolStripMenuItem _themeAuto = new();
    private readonly ToolStripMenuItem _themeLight = new();
    private readonly ToolStripMenuItem _themeDark = new();
    private readonly ToolStripMenuItem _scaleMenu = new();
    private static readonly int[] ScaleOptions = { 100, 110, 120, 130, 150 };
    private readonly Dictionary<int, ToolStripMenuItem> _scaleItems = new();
    private readonly ToolStripMenuItem _topMostItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem _minimizeItem = new();
    private readonly ToolStripMenuItem _exitItem = new();

    // System tray icon
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _trayShow = new();
    private readonly ToolStripMenuItem _trayRefresh = new();
    private readonly ToolStripMenuItem _trayLanguage = new();
    private readonly ToolStripMenuItem _trayLangCs = new();
    private readonly ToolStripMenuItem _trayLangEn = new();
    private readonly ToolStripMenuItem _trayTheme = new();
    private readonly ToolStripMenuItem _trayThemeAuto = new();
    private readonly ToolStripMenuItem _trayThemeLight = new();
    private readonly ToolStripMenuItem _trayThemeDark = new();
    private readonly ToolStripMenuItem _trayExit = new();
    private bool _balloonShown;

    // Window content
    private readonly FlowLayoutPanel _root = new();
    private readonly Label _header = new();
    private readonly Label _weeklySection = new();
    private readonly LimitRow _sessionRow = new();
    private readonly FlowLayoutPanel _weeklyRows = new();
    private readonly Dictionary<string, LimitRow> _weeklyByKey = new();
    private readonly TableLayoutPanel _footer = new();
    private readonly Label _error = new();
    private readonly Label _status = new();
    private readonly Button _refreshButton = new();

    private UsageSnapshot? _snapshot;
    private DateTime _nextFetch = DateTime.Now;
    private bool _busy;
    private float _scale = 1f;

    public MainForm()
    {
        L.Current = _settings.ResolveLanguage();
        Theme.Apply(_settings.ResolveThemeMode());
        _scale = _settings.ResolveScale();

        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        // All pixel sizes are computed in ApplyScale from the text-size setting and the monitor DPI,
        // so WinForms auto-scaling is turned off to avoid scaling twice.
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = _settings.TopMost;

        _appIcon = LoadAppIcon();
        if (_appIcon is not null) Icon = _appIcon;

        BuildMenu();
        BuildTray();
        BuildContent();
        ApplyLanguage();
        ApplyTheme();
        ApplyScale();

        _fetchTimer.Tick += async (_, _) => await FetchAsync();
        _tickTimer.Tick += (_, _) => OnTick();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Shown += async (_, _) =>
        {
            _tickTimer.Start();
            await FetchAsync();
            _fetchTimer.Start();
        };
        FormClosed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _fetchTimer.Dispose();
            _tickTimer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        };
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            return Environment.ProcessPath is { } path ? Icon.ExtractAssociatedIcon(path) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- UI construction

    private void BuildMenu()
    {
        _menu.Dock = DockStyle.Top;
        _menu.Padding = new Padding(8, 2, 0, 2);

        _langCs.Click += (_, _) => SetLanguage(UiLanguage.Cs);
        _langEn.Click += (_, _) => SetLanguage(UiLanguage.En);
        _languageMenu.DropDownItems.AddRange(_langCs, _langEn);

        _themeAuto.Click += (_, _) => SetTheme(ThemeMode.Auto);
        _themeLight.Click += (_, _) => SetTheme(ThemeMode.Light);
        _themeDark.Click += (_, _) => SetTheme(ThemeMode.Dark);
        _themeMenu.DropDownItems.AddRange(_themeAuto, _themeLight, _themeDark);

        foreach (var percent in ScaleOptions)
        {
            var item = new ToolStripMenuItem($"{percent} %");
            item.Click += (_, _) => SetScale(percent);
            _scaleItems[percent] = item;
            _scaleMenu.DropDownItems.Add(item);
        }

        _topMostItem.Checked = _settings.TopMost;
        _topMostItem.CheckedChanged += (_, _) =>
        {
            TopMost = _topMostItem.Checked;
            _settings.TopMost = _topMostItem.Checked;
            _settings.Save();
        };
        _minimizeItem.Click += (_, _) => HideToTray();
        _exitItem.Click += (_, _) => Close();

        _settingsMenu.DropDownItems.AddRange(
            _languageMenu, _themeMenu, _scaleMenu, _topMostItem, new ToolStripSeparator(), _minimizeItem, _exitItem);
        _menu.Items.Add(_settingsMenu);
        MainMenuStrip = _menu;
        Controls.Add(_menu);
    }

    private void BuildTray()
    {
        _trayShow.Font = new Font(_trayShow.Font, FontStyle.Bold);
        _trayShow.Click += (_, _) => ShowFromTray();
        _trayRefresh.Click += async (_, _) => await FetchAsync();
        _trayLangCs.Click += (_, _) => SetLanguage(UiLanguage.Cs);
        _trayLangEn.Click += (_, _) => SetLanguage(UiLanguage.En);
        _trayLanguage.DropDownItems.AddRange(_trayLangCs, _trayLangEn);
        _trayThemeAuto.Click += (_, _) => SetTheme(ThemeMode.Auto);
        _trayThemeLight.Click += (_, _) => SetTheme(ThemeMode.Light);
        _trayThemeDark.Click += (_, _) => SetTheme(ThemeMode.Dark);
        _trayTheme.DropDownItems.AddRange(_trayThemeAuto, _trayThemeLight, _trayThemeDark);
        _trayExit.Click += (_, _) => Close();
        _trayMenu.Items.AddRange(
            _trayShow, _trayRefresh, new ToolStripSeparator(), _trayLanguage, _trayTheme, new ToolStripSeparator(), _trayExit);

        _tray.Icon = _appIcon ?? SystemIcons.Application;
        _tray.ContextMenuStrip = _trayMenu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (Visible) Activate();
            else ShowFromTray();
        };
        _tray.Visible = true;
    }

    private void BuildContent()
    {
        _root.FlowDirection = FlowDirection.TopDown;
        _root.WrapContents = false;
        _root.AutoSize = true;
        _root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _root.Margin = Padding.Empty;
        _root.Padding = new Padding(18, 12, 18, 14);

        _header.Font = new Font("Segoe UI Semibold", 13f);
        _header.AutoSize = true;
        _header.Margin = new Padding(0, 0, 0, 12);
        _header.Text = "Claude";

        _weeklySection.AutoSize = true;
        _weeklySection.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _weeklySection.Margin = new Padding(0, 2, 0, 8);

        // Weekly limits: rows are created from the server response (All models, Fable, ...)
        _weeklyRows.FlowDirection = FlowDirection.TopDown;
        _weeklyRows.WrapContents = false;
        _weeklyRows.AutoSize = true;
        _weeklyRows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _weeklyRows.Margin = Padding.Empty;
        _weeklyRows.Width = ContentWidth;
        _weeklyRows.MinimumSize = new Size(ContentWidth, 0);

        _error.AutoSize = true;
        _error.MaximumSize = new Size(ContentWidth, 0);
        _error.Margin = new Padding(0, 0, 0, 10);
        _error.Visible = false;

        // Footer: status text + refresh button
        _footer.ColumnCount = 2;
        _footer.AutoSize = true;
        _footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status.AutoSize = true;
        _status.Font = new Font("Segoe UI", 9f);
        _status.Margin = Padding.Empty;
        _status.Anchor = AnchorStyles.Left;

        _refreshButton.AutoSize = true;
        _refreshButton.FlatStyle = FlatStyle.Flat;
        _refreshButton.FlatAppearance.BorderSize = 1;
        _refreshButton.UseVisualStyleBackColor = false;
        _refreshButton.Padding = new Padding(6, 2, 6, 2);
        _refreshButton.Margin = Padding.Empty;
        _refreshButton.Anchor = AnchorStyles.Right;
        _refreshButton.Click += async (_, _) => await FetchAsync();

        _footer.Controls.Add(_status, 0, 0);
        _footer.Controls.Add(_refreshButton, 1, 0);

        _root.Controls.Add(_header);
        _root.Controls.Add(_sessionRow);
        _root.Controls.Add(_weeklySection);
        _root.Controls.Add(_weeklyRows);
        _root.Controls.Add(_error);
        _root.Controls.Add(_footer);
        Controls.Add(_root);

        PositionRoot();
        _menu.SizeChanged += (_, _) => PositionRoot();
    }

    private void PositionRoot() => _root.Location = new Point(0, _menu.Bottom);

    // ---------------------------------------------------------------- language

    private void SetLanguage(UiLanguage lang)
    {
        if (L.Current == lang) return;
        L.Current = lang;
        _settings.Language = lang == UiLanguage.Cs ? "cs" : "en";
        _settings.Save();
        ApplyLanguage();
        if (_error.Visible) _ = FetchAsync(); // re-fetch so the error message appears in the new language
    }

    private void ApplyLanguage()
    {
        Text = L.Get("app.title");

        _settingsMenu.Text = L.Get("menu.settings");
        _languageMenu.Text = L.Get("menu.language");
        _langCs.Text = L.Get("menu.lang.cs");
        _langEn.Text = L.Get("menu.lang.en");
        _themeMenu.Text = L.Get("menu.theme");
        _themeAuto.Text = L.Get("theme.auto");
        _themeLight.Text = L.Get("theme.light");
        _themeDark.Text = L.Get("theme.dark");
        _scaleMenu.Text = L.Get("menu.scale");
        _topMostItem.Text = L.Get("menu.topMost");
        _minimizeItem.Text = L.Get("menu.minimizeToTray");
        _exitItem.Text = L.Get("menu.exit");

        _trayShow.Text = L.Get("tray.show");
        _trayRefresh.Text = L.Get("tray.refresh");
        _trayLanguage.Text = L.Get("menu.language");
        _trayLangCs.Text = L.Get("menu.lang.cs");
        _trayLangEn.Text = L.Get("menu.lang.en");
        _trayTheme.Text = L.Get("menu.theme");
        _trayThemeAuto.Text = L.Get("theme.auto");
        _trayThemeLight.Text = L.Get("theme.light");
        _trayThemeDark.Text = L.Get("theme.dark");
        _trayExit.Text = L.Get("menu.exit");

        var cs = L.Current == UiLanguage.Cs;
        _langCs.Checked = cs;
        _langEn.Checked = !cs;
        _trayLangCs.Checked = cs;
        _trayLangEn.Checked = !cs;

        _weeklySection.Text = L.Get("section.weekly").ToUpper(L.Culture);
        _refreshButton.Text = L.Get("button.refresh");

        if (_snapshot is not null) Render(_snapshot);
        UpdateStatus();
        UpdateTray();
    }

    // ---------------------------------------------------------------- theme

    private void SetTheme(ThemeMode mode)
    {
        _settings.Theme = mode switch
        {
            ThemeMode.Light => "light",
            ThemeMode.Dark => "dark",
            _ => "auto",
        };
        _settings.Save();
        Theme.Apply(mode);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var t = Theme.Current;
        var mode = _settings.ResolveThemeMode();

        _themeAuto.Checked = mode == ThemeMode.Auto;
        _themeLight.Checked = mode == ThemeMode.Light;
        _themeDark.Checked = mode == ThemeMode.Dark;
        _trayThemeAuto.Checked = _themeAuto.Checked;
        _trayThemeLight.Checked = _themeLight.Checked;
        _trayThemeDark.Checked = _themeDark.Checked;

        BackColor = t.Background;
        ForeColor = t.Text;
        _root.BackColor = t.Background;
        _header.ForeColor = t.Text;
        _weeklySection.ForeColor = t.SectionText;
        _error.ForeColor = t.Error;
        _status.ForeColor = t.SecondaryText;

        _refreshButton.BackColor = t.ButtonBackground;
        _refreshButton.ForeColor = t.Text;
        _refreshButton.FlatAppearance.BorderColor = t.ButtonBorder;
        _refreshButton.FlatAppearance.MouseOverBackColor = t.MenuHover;
        _refreshButton.FlatAppearance.MouseDownBackColor = t.MenuBorder;

        _menu.BackColor = t.Background;
        _menu.ForeColor = t.Text;
        _menu.Renderer = new ThemedMenuRenderer(t);
        _trayMenu.Renderer = new ThemedMenuRenderer(t);

        _sessionRow.ApplyTheme();
        foreach (var row in _weeklyByKey.Values) row.ApplyTheme();

        if (IsHandleCreated) Theme.ApplyTitleBar(this, t.IsDark);
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this, Theme.Current.IsDark);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Windows switched between light and dark app mode; follow it when the theme is "auto".
        if (e.Category != UserPreferenceCategory.General || _settings.ResolveThemeMode() != ThemeMode.Auto) return;
        if (Theme.Resolve(ThemeMode.Auto) == Theme.Current) return;
        BeginInvoke(() =>
        {
            Theme.Apply(ThemeMode.Auto);
            ApplyTheme();
        });
    }

    // ---------------------------------------------------------------- text size / DPI

    /// <summary>Converts a design-time pixel value (96 DPI, 100 %) to the current text size and monitor DPI.</summary>
    private int Px(int value) => (int)Math.Round(value * _scale * DeviceDpi / 96f);

    private int ContentPx => Px(ContentWidth);

    private void SetScale(int percent)
    {
        _settings.Scale = percent;
        _settings.Save();
        _scale = _settings.ResolveScale();
        ApplyScale();
    }

    private void ApplyScale()
    {
        foreach (var (percent, item) in _scaleItems)
        {
            item.Checked = percent == _settings.Scale;
        }

        SuspendLayout();
        Font = new Font("Segoe UI", 9.75f * _scale);
        _menu.Font = new Font("Segoe UI", 9f * _scale);
        _menu.Padding = new Padding(Px(8), Px(2), 0, Px(2));
        _header.Font = new Font("Segoe UI Semibold", 13f * _scale);
        _header.Margin = new Padding(0, 0, 0, Px(12));
        _weeklySection.Font = new Font("Segoe UI", 8.5f * _scale, FontStyle.Bold);
        _weeklySection.Margin = new Padding(0, Px(2), 0, Px(8));
        _status.Font = new Font("Segoe UI", 9f * _scale);
        _root.Padding = new Padding(Px(18), Px(12), Px(18), Px(14));
        _refreshButton.Padding = new Padding(Px(6), Px(2), Px(6), Px(2));

        var width = ContentPx;
        _weeklyRows.Width = width;
        _weeklyRows.MinimumSize = new Size(width, 0);
        _footer.Width = width;
        _footer.MinimumSize = new Size(width, 0);
        _footer.Margin = new Padding(0, Px(4), 0, 0);
        _error.MaximumSize = new Size(width, 0);
        _error.Margin = new Padding(0, 0, 0, Px(10));

        _sessionRow.ApplyScale(width, _scale, DeviceDpi);
        foreach (var row in _weeklyByKey.Values) row.ApplyScale(width, _scale, DeviceDpi);

        ResumeLayout(true);
        PositionRoot();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale(); // the window moved to a monitor with a different DPI
    }

    // ---------------------------------------------------------------- system tray

    private void HideToTray()
    {
        Hide();
        if (_balloonShown) return;
        _balloonShown = true;
        _tray.ShowBalloonTip(3000, L.Get("app.title"), L.Get("tray.balloon"), ToolTipIcon.None);
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized) HideToTray();
    }

    private void UpdateTray()
    {
        var text = _header.Text;
        if (_snapshot is not null)
        {
            var parts = new List<string>();
            if (_snapshot.Session is { } s) parts.Add($"{L.Get("tray.session")} {Math.Round(s.Percent):0} %");
            if (_snapshot.WeeklyAll is { } w) parts.Add($"{w.Label} {Math.Round(w.Percent):0} %");
            foreach (var sc in _snapshot.WeeklyScoped) parts.Add($"{sc.Label} {Math.Round(sc.Percent):0} %");
            if (parts.Count > 0) text += "\n" + string.Join(" · ", parts);
        }
        if (text.Length > 127) text = text[..126] + "…";
        _tray.Text = text;
    }

    // ---------------------------------------------------------------- data

    private async Task FetchAsync()
    {
        if (_busy) return;
        _busy = true;
        _refreshButton.Enabled = false;
        _trayRefresh.Enabled = false;
        _status.Text = L.Get("status.loading");
        try
        {
            _snapshot = await _client.FetchAsync(CancellationToken.None);
            _error.Visible = false;
            Render(_snapshot);
        }
        catch (UsageException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(L.F("err.unexpected", ex.Message));
        }
        finally
        {
            _busy = false;
            _refreshButton.Enabled = true;
            _trayRefresh.Enabled = true;
            _nextFetch = DateTime.Now.AddMilliseconds(RefreshIntervalMs);
            UpdateStatus();
            UpdateTray();
        }
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
    }

    private void Render(UsageSnapshot snapshot)
    {
        var plan = snapshot.SubscriptionType is { Length: > 0 } s
            ? char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..]
            : null;
        _header.Text = plan is null ? "Claude" : $"Claude {plan}";

        _sessionRow.Set(snapshot.Session);

        var weekly = new List<LimitInfo>();
        if (snapshot.WeeklyAll is not null) weekly.Add(snapshot.WeeklyAll);
        weekly.AddRange(snapshot.WeeklyScoped);

        _weeklyRows.SuspendLayout();
        var seen = new HashSet<string>();
        for (var i = 0; i < weekly.Count; i++)
        {
            var info = weekly[i];
            seen.Add(info.Key);
            if (!_weeklyByKey.TryGetValue(info.Key, out var row))
            {
                row = new LimitRow();
                row.ApplyScale(ContentPx, _scale, DeviceDpi);
                _weeklyByKey[info.Key] = row;
                _weeklyRows.Controls.Add(row);
            }
            _weeklyRows.Controls.SetChildIndex(row, i);
            row.Set(info);
        }
        foreach (var stale in _weeklyByKey.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _weeklyRows.Controls.Remove(_weeklyByKey[stale]);
            _weeklyByKey[stale].Dispose();
            _weeklyByKey.Remove(stale);
        }
        _weeklyRows.ResumeLayout();
    }

    private void OnTick()
    {
        _sessionRow.Tick();
        foreach (var row in _weeklyByKey.Values) row.Tick();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_busy) return;
        var parts = new List<string>();
        if (_snapshot is not null)
        {
            parts.Add(L.F("status.updated", _snapshot.FetchedAt.ToString("H:mm:ss", L.Culture)));
        }
        var wait = _nextFetch - DateTime.Now;
        parts.Add(wait > TimeSpan.Zero
            ? L.F("status.next", (int)Math.Ceiling(wait.TotalSeconds))
            : L.Get("status.refreshing"));
        _status.Text = string.Join(" · ", parts);
    }
}
