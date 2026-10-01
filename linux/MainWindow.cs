using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClaudeUsage;

public sealed class MainWindow : Window
{
    private const double ContentWidth = 420;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly UsageClient _client = new();
    private readonly DispatcherTimer _fetchTimer = new();
    private readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly WindowIcon? _appIcon;

    // Window menu
    private readonly Menu _menu = new();
    private readonly MenuItem _settingsMenu = new();
    private readonly MenuItem _languageMenu = new();
    private readonly MenuItem _langCs = Radio("language");
    private readonly MenuItem _langEn = Radio("language");
    private readonly MenuItem _themeMenu = new();
    private readonly MenuItem _themeAuto = Radio("theme");
    private readonly MenuItem _themeLight = Radio("theme");
    private readonly MenuItem _themeDark = Radio("theme");
    private readonly MenuItem _scaleMenu = new();
    private static readonly int[] ScaleOptions = { 100, 110, 120, 130, 150 };
    private readonly Dictionary<int, MenuItem> _scaleItems = new();
    private readonly MenuItem _intervalMenu = new();
    // The endpoint refills roughly one request per 150 s per token, so nothing below 3 minutes is offered.
    private static readonly int[] IntervalOptions = { 180, 300, 600, 900, 1800 };
    private readonly Dictionary<int, MenuItem> _intervalItems = new();
    private readonly MenuItem _topMostItem = new() { ToggleType = MenuItemToggleType.CheckBox };
    private readonly MenuItem _minimizeItem = new();
    private readonly MenuItem _exitItem = new();

    // System tray icon (StatusNotifierItem; the Raspberry Pi OS panel shows it in its tray)
    private readonly TrayIcon _tray = new();
    private readonly NativeMenu _trayMenu = new();
    private readonly NativeMenuItem _trayShow = new();
    private readonly NativeMenuItem _trayRefresh = new();
    private readonly NativeMenuItem _trayLanguage = new() { Menu = new NativeMenu() };
    private readonly NativeMenuItem _trayLangCs = new() { ToggleType = MenuItemToggleType.Radio };
    private readonly NativeMenuItem _trayLangEn = new() { ToggleType = MenuItemToggleType.Radio };
    private readonly NativeMenuItem _trayTheme = new() { Menu = new NativeMenu() };
    private readonly NativeMenuItem _trayThemeAuto = new() { ToggleType = MenuItemToggleType.Radio };
    private readonly NativeMenuItem _trayThemeLight = new() { ToggleType = MenuItemToggleType.Radio };
    private readonly NativeMenuItem _trayThemeDark = new() { ToggleType = MenuItemToggleType.Radio };
    private readonly NativeMenuItem _trayExit = new();
    private bool _balloonShown;
    private DateTime _restoredAt;

    // Window content; the text size scales everything inside _scaler, Avalonia handles the monitor DPI
    private readonly LayoutTransformControl _scaler = new();
    private readonly StackPanel _root = new();
    private readonly TextBlock _header = new();
    private readonly TextBlock _weeklySection = new();
    private readonly LimitRow _sessionRow = new();
    private readonly StackPanel _weeklyRows = new();
    private readonly Dictionary<string, LimitRow> _weeklyByKey = new();
    private readonly Grid _footer = new();
    private readonly TextBlock _error = new();
    private readonly TextBlock _status = new();
    private readonly Button _refreshButton = new();

    private UsageSnapshot? _snapshot;
    private DateTime _nextFetch = DateTime.Now;
    private bool _busy;
    private bool _rateLimited;
    private int _rateLimitStrikes;

    public MainWindow()
    {
        L.Current = _settings.ResolveLanguage();
        AppTheme.Apply(_settings.ResolveThemeMode());

        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = _settings.TopMost;
        FontSize = 13;

        _appIcon = LoadAppIcon();
        if (_appIcon is not null) Icon = _appIcon;

        BuildMenu();
        BuildTray();
        BuildContent();
        ApplyLanguage();
        ApplyTheme();
        ApplyScale();

        _fetchTimer.Tick += async (_, _) =>
        {
            _fetchTimer.Stop(); // one-shot: FetchAsync schedules the next run
            await FetchAsync();
        };
        _tickTimer.Tick += (_, _) => OnTick();
        Application.Current!.ActualThemeVariantChanged += OnSystemThemeChanged;

        Opened += async (_, _) =>
        {
            _tickTimer.Start();
            await FetchAsync();
        };
        Closed += (_, _) =>
        {
            Application.Current!.ActualThemeVariantChanged -= OnSystemThemeChanged;
            _fetchTimer.Stop();
            _tickTimer.Stop();
            _tray.IsVisible = false;
            _tray.Dispose();
        };
    }

    private static WindowIcon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("claudeusage.png");
            return stream is null ? null : new WindowIcon(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static MenuItem Radio(string group) => new() { ToggleType = MenuItemToggleType.Radio, GroupName = group };

    // ---------------------------------------------------------------- UI construction

    private void BuildMenu()
    {
        _menu.FontSize = 12;
        _menu.Padding = new Thickness(8, 2, 0, 2);

        // Menu items toggle their own check mark on click; every handler re-syncs the marks from the settings
        // after that has happened, so a mark always shows what is in effect.
        _langCs.Click += (_, _) => SetLanguage(UiLanguage.Cs);
        _langEn.Click += (_, _) => SetLanguage(UiLanguage.En);
        _languageMenu.Items.Add(_langCs);
        _languageMenu.Items.Add(_langEn);

        _themeAuto.Click += (_, _) => SetTheme(ThemeMode.Auto);
        _themeLight.Click += (_, _) => SetTheme(ThemeMode.Light);
        _themeDark.Click += (_, _) => SetTheme(ThemeMode.Dark);
        _themeMenu.Items.Add(_themeAuto);
        _themeMenu.Items.Add(_themeLight);
        _themeMenu.Items.Add(_themeDark);

        foreach (var percent in ScaleOptions)
        {
            var item = Radio("scale");
            item.Header = $"{percent} %";
            item.Click += (_, _) => SetScale(percent);
            _scaleItems[percent] = item;
            _scaleMenu.Items.Add(item);
        }

        foreach (var seconds in IntervalOptions)
        {
            var item = Radio("interval");
            item.Header = $"{seconds / 60} min";
            item.Click += (_, _) => SetRefreshInterval(seconds);
            _intervalItems[seconds] = item;
            _intervalMenu.Items.Add(item);
        }

        _topMostItem.Click += (_, _) =>
        {
            _settings.TopMost = !_settings.TopMost;
            _settings.Save();
            Topmost = _settings.TopMost;
            SyncChecks();
        };
        _minimizeItem.Click += (_, _) => HideToTray();
        _exitItem.Click += (_, _) => Close();

        _settingsMenu.Items.Add(_languageMenu);
        _settingsMenu.Items.Add(_themeMenu);
        _settingsMenu.Items.Add(_scaleMenu);
        _settingsMenu.Items.Add(_intervalMenu);
        _settingsMenu.Items.Add(_topMostItem);
        _settingsMenu.Items.Add(new Separator());
        _settingsMenu.Items.Add(_minimizeItem);
        _settingsMenu.Items.Add(_exitItem);
        _menu.Items.Add(_settingsMenu);
    }

    private void BuildTray()
    {
        _trayShow.Click += (_, _) => ShowFromTray();
        _trayRefresh.Click += async (_, _) => await FetchAsync();
        _trayLangCs.Click += (_, _) => SetLanguage(UiLanguage.Cs);
        _trayLangEn.Click += (_, _) => SetLanguage(UiLanguage.En);
        _trayLanguage.Menu!.Items.Add(_trayLangCs);
        _trayLanguage.Menu.Items.Add(_trayLangEn);
        _trayThemeAuto.Click += (_, _) => SetTheme(ThemeMode.Auto);
        _trayThemeLight.Click += (_, _) => SetTheme(ThemeMode.Light);
        _trayThemeDark.Click += (_, _) => SetTheme(ThemeMode.Dark);
        _trayTheme.Menu!.Items.Add(_trayThemeAuto);
        _trayTheme.Menu.Items.Add(_trayThemeLight);
        _trayTheme.Menu.Items.Add(_trayThemeDark);
        _trayExit.Click += (_, _) => Close();
        _trayMenu.Items.Add(_trayShow);
        _trayMenu.Items.Add(_trayRefresh);
        _trayMenu.Items.Add(new NativeMenuItemSeparator());
        _trayMenu.Items.Add(_trayLanguage);
        _trayMenu.Items.Add(_trayTheme);
        _trayMenu.Items.Add(new NativeMenuItemSeparator());
        _trayMenu.Items.Add(_trayExit);

        _tray.Icon = _appIcon;
        _tray.Menu = _trayMenu;
        _tray.Clicked += (_, _) =>
        {
            if (IsVisible && WindowState != WindowState.Minimized) Activate();
            else ShowFromTray();
        };
        _tray.IsVisible = true;
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
    }

    private void BuildContent()
    {
        _root.Width = ContentWidth;
        _root.Margin = new Thickness(18, 12, 18, 14);

        _header.FontSize = 17;
        _header.FontWeight = FontWeight.SemiBold;
        _header.Margin = new Thickness(0, 0, 0, 12);
        _header.Text = "Claude";

        _weeklySection.FontSize = 11;
        _weeklySection.FontWeight = FontWeight.Bold;
        _weeklySection.Margin = new Thickness(0, 2, 0, 8);

        _error.TextWrapping = TextWrapping.Wrap;
        _error.Margin = new Thickness(0, 0, 0, 10);
        _error.IsVisible = false;

        // Footer: status text + refresh button
        _footer.ColumnDefinitions = new ColumnDefinitions("*,Auto");
        _footer.Margin = new Thickness(0, 4, 0, 0);

        _status.FontSize = 12;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(0, 0, 8, 0);

        _refreshButton.Padding = new Thickness(10, 4);
        _refreshButton.VerticalAlignment = VerticalAlignment.Center;
        _refreshButton.Click += async (_, _) => await FetchAsync();
        Grid.SetColumn(_refreshButton, 1);

        _footer.Children.Add(_status);
        _footer.Children.Add(_refreshButton);

        // Rows stay hidden until the first successful fetch.
        _sessionRow.IsVisible = false;
        _weeklySection.IsVisible = false;

        _root.Children.Add(_header);
        _root.Children.Add(_sessionRow);
        _root.Children.Add(_weeklySection);
        _root.Children.Add(_weeklyRows);
        _root.Children.Add(_error);
        _root.Children.Add(_footer);

        var layout = new DockPanel();
        DockPanel.SetDock(_menu, Dock.Top);
        layout.Children.Add(_menu);
        layout.Children.Add(_root);
        _scaler.Child = layout;
        Content = _scaler;
    }

    /// <summary>Puts every check mark (window menu and tray menu) in line with the settings in effect.</summary>
    private void SyncChecks()
    {
        var cs = L.Current == UiLanguage.Cs;
        _langCs.IsChecked = cs;
        _langEn.IsChecked = !cs;
        _trayLangCs.IsChecked = cs;
        _trayLangEn.IsChecked = !cs;

        var mode = _settings.ResolveThemeMode();
        _themeAuto.IsChecked = mode == ThemeMode.Auto;
        _themeLight.IsChecked = mode == ThemeMode.Light;
        _themeDark.IsChecked = mode == ThemeMode.Dark;
        _trayThemeAuto.IsChecked = mode == ThemeMode.Auto;
        _trayThemeLight.IsChecked = mode == ThemeMode.Light;
        _trayThemeDark.IsChecked = mode == ThemeMode.Dark;

        foreach (var (percent, item) in _scaleItems) item.IsChecked = percent == _settings.Scale;
        foreach (var (seconds, item) in _intervalItems) item.IsChecked = seconds == _settings.RefreshSeconds;
        _topMostItem.IsChecked = _settings.TopMost;
    }

    // ---------------------------------------------------------------- language

    private void SetLanguage(UiLanguage lang)
    {
        if (L.Current != lang)
        {
            L.Current = lang;
            _settings.Language = lang == UiLanguage.Cs ? "cs" : "en";
            _settings.Save();
            ApplyLanguage();
            if (_error.IsVisible) _ = FetchAsync(); // re-fetch so the error message appears in the new language
        }
        Dispatcher.UIThread.Post(SyncChecks);
    }

    private void ApplyLanguage()
    {
        Title = L.Get("app.title");

        _settingsMenu.Header = L.Get("menu.settings");
        _languageMenu.Header = L.Get("menu.language");
        _langCs.Header = L.Get("menu.lang.cs");
        _langEn.Header = L.Get("menu.lang.en");
        _themeMenu.Header = L.Get("menu.theme");
        _themeAuto.Header = L.Get("theme.auto");
        _themeLight.Header = L.Get("theme.light");
        _themeDark.Header = L.Get("theme.dark");
        _scaleMenu.Header = L.Get("menu.scale");
        _intervalMenu.Header = L.Get("menu.refreshInterval");
        _topMostItem.Header = L.Get("menu.topMost");
        _minimizeItem.Header = L.Get("menu.minimizeToTray");
        _exitItem.Header = L.Get("menu.exit");

        _trayShow.Header = L.Get("tray.show");
        _trayRefresh.Header = L.Get("tray.refresh");
        _trayLanguage.Header = L.Get("menu.language");
        _trayLangCs.Header = L.Get("menu.lang.cs");
        _trayLangEn.Header = L.Get("menu.lang.en");
        _trayTheme.Header = L.Get("menu.theme");
        _trayThemeAuto.Header = L.Get("theme.auto");
        _trayThemeLight.Header = L.Get("theme.light");
        _trayThemeDark.Header = L.Get("theme.dark");
        _trayExit.Header = L.Get("menu.exit");

        _weeklySection.Text = L.Get("section.weekly").ToUpper(L.Culture);
        _refreshButton.Content = L.Get("button.refresh");

        SyncChecks();
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
        AppTheme.Apply(mode);
        ApplyTheme();
        Dispatcher.UIThread.Post(SyncChecks);
    }

    private void ApplyTheme()
    {
        var t = AppTheme.Current;
        Background = t.Background;
        _header.Foreground = t.Text;
        _weeklySection.Foreground = t.SectionText;
        _error.Foreground = t.Error;
        _sessionRow.ApplyTheme();
        foreach (var row in _weeklyByKey.Values) row.ApplyTheme();
        SyncChecks();
        UpdateStatus();
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        // The desktop switched between light and dark; follow it when the theme is "auto".
        if (_settings.ResolveThemeMode() != ThemeMode.Auto) return;
        AppTheme.Apply(ThemeMode.Auto);
        ApplyTheme();
    }

    // ---------------------------------------------------------------- text size

    private void SetScale(int percent)
    {
        _settings.Scale = percent;
        _settings.Save();
        ApplyScale();
        Dispatcher.UIThread.Post(SyncChecks);
    }

    private void ApplyScale()
    {
        var scale = _settings.ResolveScale();
        _scaler.LayoutTransform = new ScaleTransform(scale, scale);
        SyncChecks();
    }

    // ---------------------------------------------------------------- system tray

    private void HideToTray()
    {
        Hide();
        if (_balloonShown) return;
        _balloonShown = true;
        Notify(L.Get("app.title"), L.Get("tray.balloon"));
    }

    private void ShowFromTray()
    {
        _restoredAt = DateTime.UtcNow;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty || WindowState != WindowState.Minimized) return;

        // labwc maps a window that was minimized before it was hidden back as minimized, which arrives here
        // just after ShowFromTray; that is the restore still settling, not the user minimizing again.
        if (DateTime.UtcNow - _restoredAt < TimeSpan.FromSeconds(2))
        {
            Dispatcher.UIThread.Post(() =>
            {
                WindowState = WindowState.Normal;
                Activate();
            });
            return;
        }
        HideToTray();
    }

    /// <summary>A desktop notification (org.freedesktop.Notifications; the Raspberry Pi OS panel shows them).</summary>
    private static void Notify(string title, string body)
    {
        static string Quote(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        try
        {
            var psi = new ProcessStartInfo("gdbus") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[]
            {
                "call", "--session", "--dest", "org.freedesktop.Notifications",
                "--object-path", "/org/freedesktop/Notifications", "--method", "org.freedesktop.Notifications.Notify",
                Quote("Claude Usage"), "0", Quote("claudeusage"), Quote(title), Quote(body), "@as []", "@a{sv} {}", "5000",
            })
            {
                psi.ArgumentList.Add(arg);
            }
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // No gdbus or no notification service: the tray icon is still there.
        }
    }

    private void UpdateTray()
    {
        var text = _header.Text ?? "Claude";
        if (_snapshot is not null)
        {
            var parts = new List<string>();
            if (_snapshot.Session is { } s) parts.Add($"{L.Get("tray.session")} {Math.Round(s.Percent):0} %");
            if (_snapshot.WeeklyAll is { } w) parts.Add($"{w.Label} {Math.Round(w.Percent):0} %");
            foreach (var sc in _snapshot.WeeklyScoped) parts.Add($"{sc.Label} {Math.Round(sc.Percent):0} %");
            if (parts.Count > 0) text += "\n" + string.Join(" · ", parts);
        }
        _tray.ToolTipText = text;
    }

    // ---------------------------------------------------------------- data

    private async Task FetchAsync()
    {
        if (_busy) return;
        _busy = true;
        _fetchTimer.Stop();
        _refreshButton.IsEnabled = false;
        _trayRefresh.IsEnabled = false;
        _status.Text = L.Get("status.loading");
        var delay = _settings.ResolveRefreshInterval();
        try
        {
            _snapshot = await _client.FetchAsync(CancellationToken.None);
            _rateLimited = false;
            _rateLimitStrikes = 0;
            _error.IsVisible = false;
            Render(_snapshot);
        }
        catch (RateLimitedException ex)
        {
            // Keep the last values on screen and back off: double the interval per consecutive 429,
            // honor Retry-After when the server sends a real value, cap at MaxBackoff.
            _rateLimited = true;
            _rateLimitStrikes = Math.Min(_rateLimitStrikes + 1, 6);
            delay = TimeSpan.FromSeconds(delay.TotalSeconds * Math.Pow(2, _rateLimitStrikes));
            if (ex.RetryAfter is { } retryAfter && retryAfter > delay) delay = retryAfter;
            if (delay > MaxBackoff) delay = MaxBackoff;
            _error.IsVisible = false;
        }
        catch (UsageException ex)
        {
            // Any answer other than a 429 ends a run of 429s: the next check comes at the normal interval.
            _rateLimited = false;
            _rateLimitStrikes = 0;
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            _rateLimited = false;
            _rateLimitStrikes = 0;
            ShowError(L.F("err.unexpected", ex.Message));
        }
        finally
        {
            _busy = false;
            _refreshButton.IsEnabled = true;
            _trayRefresh.IsEnabled = true;
            ScheduleNextFetch(delay);
            UpdateStatus();
            UpdateTray();
        }
    }

    private void ScheduleNextFetch(TimeSpan delay)
    {
        _nextFetch = DateTime.Now + delay;
        _fetchTimer.Interval = delay > TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1);
        _fetchTimer.Start();
    }

    private void SetRefreshInterval(int seconds)
    {
        _settings.RefreshSeconds = seconds;
        _settings.Save();
        Dispatcher.UIThread.Post(SyncChecks);
        if (_busy) return;

        // Re-plan the next check relative to the last successful one, but never sooner than in a few seconds.
        var last = _snapshot?.FetchedAt.LocalDateTime ?? DateTime.Now;
        var wait = last + _settings.ResolveRefreshInterval() - DateTime.Now;
        ScheduleNextFetch(wait > TimeSpan.FromSeconds(3) ? wait : TimeSpan.FromSeconds(3));
        UpdateStatus();
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    private void Render(UsageSnapshot snapshot)
    {
        var plan = snapshot.SubscriptionType is { Length: > 0 } s
            ? char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..]
            : null;
        _header.Text = plan is null ? "Claude" : $"Claude {plan}";

        _sessionRow.IsVisible = snapshot.Session is not null;
        _weeklySection.IsVisible = true;
        _sessionRow.Set(snapshot.Session);

        var weekly = new List<LimitInfo>();
        if (snapshot.WeeklyAll is not null) weekly.Add(snapshot.WeeklyAll);
        weekly.AddRange(snapshot.WeeklyScoped);

        // Rows are reused by key and re-added in the order of the response.
        _weeklyRows.Children.Clear();
        var seen = new HashSet<string>();
        foreach (var info in weekly)
        {
            seen.Add(info.Key);
            if (!_weeklyByKey.TryGetValue(info.Key, out var row))
            {
                row = new LimitRow();
                _weeklyByKey[info.Key] = row;
            }
            _weeklyRows.Children.Add(row);
            row.Set(info);
        }
        foreach (var stale in _weeklyByKey.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _weeklyByKey.Remove(stale);
        }
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
            ? L.F(_rateLimited ? "status.rateLimited" : "status.next", FormatWait(wait))
            : L.Get("status.refreshing"));
        _status.Text = string.Join(" · ", parts);
        _status.Foreground = _rateLimited ? AppTheme.Warning : AppTheme.Current.SecondaryText;
    }

    private static string FormatWait(TimeSpan wait)
    {
        var total = (int)Math.Ceiling(wait.TotalSeconds);
        return total >= 60 ? L.F("time.ms", total / 60, total % 60) : L.F("time.s", total);
    }
}
