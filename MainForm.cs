using System.Globalization;

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
    private readonly ToolStripMenuItem _trayExit = new();
    private bool _balloonShown;

    // Window content
    private readonly FlowLayoutPanel _root = new();
    private readonly Label _header = new();
    private readonly Label _weeklySection = new();
    private readonly LimitRow _sessionRow = new(ContentWidth);
    private readonly FlowLayoutPanel _weeklyRows = new();
    private readonly Dictionary<string, LimitRow> _weeklyByKey = new();
    private readonly Label _error = new();
    private readonly Label _status = new();
    private readonly Button _refreshButton = new();

    private UsageSnapshot? _snapshot;
    private DateTime _nextFetch = DateTime.Now;
    private bool _busy;

    public MainForm()
    {
        L.Current = _settings.ResolveLanguage();

        Font = new Font("Segoe UI", 9.75f);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = _settings.TopMost;

        _appIcon = LoadAppIcon();
        if (_appIcon is not null) Icon = _appIcon;

        BuildMenu();
        BuildTray();
        BuildContent();
        ApplyLanguage();

        _fetchTimer.Tick += async (_, _) => await FetchAsync();
        _tickTimer.Tick += (_, _) => OnTick();

        Shown += async (_, _) =>
        {
            _tickTimer.Start();
            await FetchAsync();
            _fetchTimer.Start();
        };
        FormClosed += (_, _) =>
        {
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
        _menu.BackColor = Color.White;
        _menu.RenderMode = ToolStripRenderMode.System;
        _menu.Padding = new Padding(8, 2, 0, 2);

        _langCs.Click += (_, _) => SetLanguage(UiLanguage.Cs);
        _langEn.Click += (_, _) => SetLanguage(UiLanguage.En);
        _languageMenu.DropDownItems.AddRange(_langCs, _langEn);

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
            _languageMenu, _topMostItem, new ToolStripSeparator(), _minimizeItem, _exitItem);
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
        _trayExit.Click += (_, _) => Close();
        _trayMenu.Items.AddRange(
            _trayShow, _trayRefresh, new ToolStripSeparator(), _trayLanguage, new ToolStripSeparator(), _trayExit);

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
        _weeklySection.ForeColor = Color.FromArgb(130, 130, 130);
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
        _error.ForeColor = Color.FromArgb(200, 40, 40);
        _error.Margin = new Padding(0, 0, 0, 10);
        _error.Visible = false;

        // Footer: status text + refresh button
        var footer = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            Width = ContentWidth,
            MinimumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 4, 0, 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status.AutoSize = true;
        _status.ForeColor = Color.FromArgb(120, 120, 120);
        _status.Font = new Font("Segoe UI", 9f);
        _status.Margin = Padding.Empty;
        _status.Anchor = AnchorStyles.Left;

        _refreshButton.AutoSize = true;
        _refreshButton.Padding = new Padding(6, 2, 6, 2);
        _refreshButton.Margin = Padding.Empty;
        _refreshButton.Anchor = AnchorStyles.Right;
        _refreshButton.Click += async (_, _) => await FetchAsync();

        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_refreshButton, 1, 0);

        _root.Controls.Add(_header);
        _root.Controls.Add(_sessionRow);
        _root.Controls.Add(_weeklySection);
        _root.Controls.Add(_weeklyRows);
        _root.Controls.Add(_error);
        _root.Controls.Add(footer);
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
        _topMostItem.Text = L.Get("menu.topMost");
        _minimizeItem.Text = L.Get("menu.minimizeToTray");
        _exitItem.Text = L.Get("menu.exit");

        _trayShow.Text = L.Get("tray.show");
        _trayRefresh.Text = L.Get("tray.refresh");
        _trayLanguage.Text = L.Get("menu.language");
        _trayLangCs.Text = L.Get("menu.lang.cs");
        _trayLangEn.Text = L.Get("menu.lang.en");
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
                row = new LimitRow(ContentWidth);
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
