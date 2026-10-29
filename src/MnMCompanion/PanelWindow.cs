using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using MnMCompanion.Ledger;
using MnMCompanion.Loot;

namespace MnMCompanion;

/// <summary>
/// The loot panel's window: borderless, dragged by its title bar, resized from any edge or the grip, on top of the game unless
/// unpinned. The title bar carries the character (a menu of every character with a ledger on this PC), a pin and a close
/// button. It looks at the ledger every couple of seconds and redraws the panel when something changed.
/// </summary>
internal sealed class PanelWindow : Window
{
    private readonly LootStore _store;
    private readonly Settings _settings;
    private readonly LootPanel _panel;
    private readonly MapPanel _map;
    private readonly RecipesPanel _recipes;
    private readonly SessionScope _session;
    private readonly HistoryData? _history;
    private readonly Dictionary<string, IMiniPage> _pages = [];
    private readonly Dictionary<string, RadioButton> _nav = [];
    private readonly ContentControl _host = new();
    private readonly TextBlock _status;
    private string _page = "Loot";
    private static readonly string[] PageNames = ["Map", "Loot", "Vendor", "Trades", "Items", "Harvest", "Recipes"];
    private readonly TextBlock _who;
    private readonly Button _pin;
    private readonly Button _about;
    private readonly Border _banner = new() { Visibility = Visibility.Collapsed };
    private readonly UpdateController? _updates;
    private readonly DispatcherTimer _timer;
    private bool _busy;

    public event Action<DateOnly, string?, LootSession?>? BreakdownRequested;

    /// <summary>The player chose "What does sharing include?" in the ⓘ menu.</summary>
    public event Action? ShareRequested;

    /// <summary>The player pressed the timers button in the title bar.</summary>
    public event Action? TimersRequested;

    /// <summary>A row of the Vendor, Trades, Items or Harvest page was clicked: the History window page ("Vendors", "Trades", "Changes", "Harvest") that shows it in full.</summary>
    public event Action<string>? HistoryPageRequested;

    /// <param name="updates">The update checker; null (a picture-taking run) means no update button or banner.</param>
    /// <param name="recipesFile">A recipe list to read instead of the kept copy and the wiki (a picture-taking run); null for the real thing.</param>
    public PanelWindow(LootStore store, Settings settings, UpdateController? updates = null, HistoryData? history = null, string? recipesFile = null)
    {
        _store = store;
        _settings = settings;
        _updates = updates;
        _history = history;
        Title = "Companion";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        // A borderless window still gets a 7 px resize frame from Windows, and at the top Windows paints it white above our title bar. Taking the
        // frame over (no system caption, no glass) lets the title bar run to the very top; the edges still resize.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1C));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x90, 0xA0, 0xB0));
        BorderThickness = new Thickness(1);
        Topmost = settings.OnTop;
        MinWidth = 300;
        MinHeight = 240;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var b = settings.Panel ?? new Bounds(double.NaN, double.NaN, 480, 600);
        Width = b.Width;
        Height = b.Height;
        var work = SystemParameters.WorkArea;
        Left = double.IsNaN(b.Left) ? work.Right - Width - 24 : b.Left;
        Top = double.IsNaN(b.Top) ? work.Top + 80 : b.Top;
        KeepOnScreen();

        var caption = new TextBlock { Text = "Companion", Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        _who = new TextBlock
        {
            Foreground = Palette.Text, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = "The character the game is writing a ledger for. This window follows whoever you play; the History window can look at anyone.",
        };
        _pin = TitleButton("", "Keep on top of other windows", () => SetOnTop(!Topmost));
        var close = TitleButton("", "Close", Close);
        _about = TitleButton("", "About Companion, and updates", OpenAboutMenu);
        var historyButton = LabelledButton("\uE81C", "History", "History: every day and every page, for this character or for everyone", OpenHistory);
        var timersButton = LabelledButton("\uE916", "Timers", "Timers: your own timers, started by a key, and a timer for every corpse", () => TimersRequested?.Invoke());
        var title = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)), Height = 32, Cursor = Cursors.SizeAll, LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_pin, Dock.Right);
        DockPanel.SetDock(_about, Dock.Right);
        DockPanel.SetDock(historyButton, Dock.Right);
        DockPanel.SetDock(timersButton, Dock.Right);
        DockPanel.SetDock(caption, Dock.Left);
        DockPanel.SetDock(_who, Dock.Left);
        title.Children.Add(close);
        title.Children.Add(_pin);
        if (updates is not null) title.Children.Add(_about);
        title.Children.Add(historyButton);
        title.Children.Add(timersButton);
        title.Children.Add(caption);
        title.Children.Add(_who);
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); };
        SizeChanged += (_, _) => ShowToolLabels();

        _session = new SessionScope(store);
        _panel = new LootPanel(store, _session, settings.View, view => { settings.View = view; settings.Save(); },
            (day, member, session) => BreakdownRequested?.Invoke(day, member, session));
        _status = _panel.StatusLine;
        void OpenInHistory(string key) => HistoryPageRequested?.Invoke(key);
        _pages["Map"] = _map = new MapPanel(store, _session, settings, _status);
        _pages["Loot"] = _panel;
        _pages["Vendor"] = new VendorMini(store, history, _session, _status, OpenInHistory);
        _pages["Trades"] = new TradesMini(store, history, _session, _status, OpenInHistory);
        _pages["Items"] = new ItemsMini(store, history, _session, _status, OpenInHistory);
        _pages["Harvest"] = new HarvestMini(store, history, _session, _status, OpenInHistory);
        _pages["Recipes"] = _recipes = new RecipesPanel(settings, _status, recipesFile);
        _session.Said += text => Active.Say(text);
        var bottom = BuildBottomBar();
        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(_banner, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(title);
        root.Children.Add(_banner);
        root.Children.Add(bottom);
        root.Children.Add(_host);
        Content = root;
        ShowPage(PageNames.Contains(settings.MiniPage) ? settings.MiniPage : "Loot", save: false);
        ShowPin();
        ShowSelection();
        if (updates is not null)
        {
            updates.Changed += ShowBanner;
            ShowBanner();
        }

        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
        Closed += (_, _) =>
        {
            _timer?.Stop();
            _map.Dispose();
            if (_updates is not null) _updates.Changed -= ShowBanner;
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) =>
        {
            await FollowAsync();
            await RefreshAsync();
            Active.Tick();
            if (Active.NeedsHistory && _history is not null) _ = _history.RefreshAsync();
        };
        Loaded += async (_, _) =>
        {
            _panel.Refresh();
            await RefreshAsync(force: true);
            Active.Refresh();
            _timer.Start();
        };
    }

    public LootPanel Panel => _panel;

    private IMiniPage Active => _pages[_page];

    /// <summary>Opens the History window on the day and session on show.</summary>
    private void OpenHistory() => BreakdownRequested?.Invoke(_session.Day, null, _session.ActiveSession());

    /// <summary>Draws the page on show now, on this thread (for a picture).</summary>
    public void DrawPageNow() => Active.RefreshNow();

    /// <summary>For a picture: the Recipes page on a skill, with a search typed in.</summary>
    public void ShowRecipes(string? skill, string? search) => _recipes.Show(skill, search);

    private void ShowPage(string key, bool save = true)
    {
        if (!_pages.TryGetValue(key, out var page)) return;
        _page = key;
        _host.Content = page.Element;
        _status.Text = "";
        if (_nav.TryGetValue(key, out var rb) && rb.IsChecked != true) rb.IsChecked = true;
        if (save) { _settings.MiniPage = key; _settings.Save(); }
        page.Refresh();
        if (page.NeedsHistory && _history is not null) _ = _history.RefreshAsync();
    }

    /// <summary>
    /// The bottom of the window: a button for each page (Map, Loot, Vendor, Trades, Items, Harvest, Recipes) with the status line above them. Every page shows
    /// the same day and session; History is a button in the title bar, New session is in the session menu and the ledger folder in the ⓘ menu.
    /// </summary>
    private FrameworkElement BuildBottomBar()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = PageNames.Length };
        foreach (var name in PageNames)
        {
            var key = name;
            var rb = new RadioButton { Content = new TextBlock { Text = key }, Style = (Style)Ui.Theme["Segment"], GroupName = "MiniPage", IsChecked = key == _page, Focusable = false };
            rb.Checked += (_, _) => { if (key != _page) ShowPage(key); };
            _nav[key] = rb;
            grid.Children.Add(rb);
        }
        var pages = new Border { CornerRadius = new CornerRadius(8), Background = (Brush)Ui.Theme["Subtle"], Padding = new Thickness(2), Child = grid };

        _status.Margin = new Thickness(2, 0, 2, 6);
        var stack = new StackPanel();
        stack.Children.Add(_status);
        stack.Children.Add(pages);
        return new Border { Background = (Brush)Ui.Theme["Panel"], BorderBrush = (Brush)Ui.Theme["Stroke"], BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 8, 12, 8), Child = stack };
    }

    /// <summary>A button of the title bar: a picture, big enough to see, with the theme's hover highlight.</summary>
    private static Button TitleButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Content = glyph, Style = (Style)Ui.Theme["IconButton"], Width = 30, Height = 26, Margin = new Thickness(1, 0, 1, 0), FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xE0)), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center,
        };
        b.Click += (_, _) => click();
        return b;
    }

    private readonly List<TextBlock> _toolLabels = [];

    /// <summary>A title bar button that opens a window: its picture and its name, so it says what it is. The name is left off when the window is narrow.</summary>
    private Button LabelledButton(string glyph, string label, string tip, Action click)
    {
        var words = new TextBlock { Text = label, FontSize = 12.5, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _toolLabels.Add(words);
        var picture = new TextBlock { Text = glyph, FontFamily = (FontFamily)Ui.Theme["Icons"], FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 10, 0) };
        content.Children.Add(picture);
        content.Children.Add(words);
        var b = new Button
        {
            Content = content, Style = (Style)Ui.Theme["IconButton"], Width = double.NaN, MinWidth = 30, Height = 26, Margin = new Thickness(1, 0, 1, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xE0)), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center,
        };
        b.Click += (_, _) => click();
        return b;
    }

    private void ShowToolLabels()
    {
        var visibility = ActualWidth >= 420 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var label in _toolLabels) label.Visibility = visibility;
    }

    private void SetOnTop(bool on)
    {
        Topmost = on;
        _settings.OnTop = on;
        _settings.Save();
        ShowPin();
    }

    private void ShowPin() => _pin.Foreground = Topmost ? new SolidColorBrush(Palette.AccentColor) : new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x88));

    private void ShowSelection() => _who.Text = _store.Selected is { } s ? $"{s.Character} · {s.ServerName}" : "No ledger";

    /// <summary>Reads the ledger on a worker thread and redraws if it changed (or <paramref name="force"/>).</summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            bool changed = await Task.Run(_store.Refresh);
            if (changed || force)
            {
                _panel.Refresh();
                if (_page != "Loot") Active.Refresh();
            }
        }
        catch (Exception ex)
        {
            _panel.Say("Can't read the ledger: " + ex.Message);
        }
        finally { _busy = false; }
    }

    private void OpenAboutMenu()
    {
        if (_updates is null) return;
        var menu = new ContextMenu { PlacementTarget = _about, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = $"Companion {Update.AppVersion.Show(_updates.Current)}", IsEnabled = false });
        menu.Items.Add(new Separator());
        var check = new MenuItem { Header = "Check for updates now", ToolTip = "Asks GitHub whether a newer version has been released. Nothing is installed unless you say so." };
        check.Click += async (_, _) => await _updates.CheckAsync(manual: true);
        menu.Items.Add(check);
        var auto = new MenuItem { Header = "Check for updates automatically", IsCheckable = true, IsChecked = _settings.CheckForUpdates, ToolTip = "When the app starts and every few hours. Untick it and the app never asks GitHub by itself." };
        var install = new MenuItem
        {
            Header = "Install updates automatically", IsCheckable = true, IsChecked = _settings.InstallUpdates, IsEnabled = _settings.CheckForUpdates,
            ToolTip = "A newer version is downloaded, checked against the checksum published with it, and installed; Companion restarts into it once none of your timers is running. Untick it and a newer version is only offered, and you press Update now.",
        };
        auto.Click += (_, _) => { _settings.CheckForUpdates = auto.IsChecked; _settings.Save(); install.IsEnabled = auto.IsChecked; };
        install.Click += (_, _) => { _settings.InstallUpdates = install.IsChecked; _settings.Save(); };
        menu.Items.Add(auto);
        menu.Items.Add(install);
        menu.Items.Add(new Separator());
        var share = new MenuItem
        {
            Header = "Share my data with the community", IsCheckable = true, IsChecked = _settings.Sharing,
            ToolTip = "Yes: your totals (including weekly counts of corpses looted, coin, items, gathering and sales) are shared for you, automatically, and you can use the community data. No (the default): nothing is shared and the community data isn't used.",
        };
        share.Click += (_, _) => { _settings.SetSharing(share.IsChecked); _settings.Save(); };
        menu.Items.Add(share);
        var what = new MenuItem { Header = "What does sharing include?…", ToolTip = "Shows exactly what is shared, and what never is" };
        what.Click += (_, _) => ShareRequested?.Invoke();
        menu.Items.Add(what);
        var maps = new MenuItem
        {
            Header = "Show the live map of my zone (uses mnmatlas.com)", IsCheckable = true, IsChecked = _settings.MapsOnline,
            ToolTip = "The Map page shows the community atlas's map of the zone you are in. That loads the atlas's website, so it sees your zone and your internet address. Off by default.",
        };
        maps.Click += (_, _) => { _settings.MapsOnline = maps.IsChecked; _settings.Save(); _map.Refresh(); };
        menu.Items.Add(maps);
        var recipes = new MenuItem
        {
            Header = "Load the recipe list from the community wiki (uses monstersandmemories.wiki)", IsCheckable = true, IsChecked = _settings.RecipesOnline,
            ToolTip = "The Recipes page shows the community wiki's tradeskill recipes. It loads them by itself the first time you open the page and about once a week after that, so that site sees your internet address (nothing from your game or ledger is sent). The list is kept on this PC. Untick it and the wiki is never asked.",
        };
        recipes.Click += (_, _) => { _settings.RecipesOnline = recipes.IsChecked; _settings.Save(); _recipes.Refresh(); };
        menu.Items.Add(recipes);
        var ledgerFolder = new MenuItem { Header = "Open the ledger folder", ToolTip = "Opens the folder where the game keeps this character's ledger files (one a day for the character, one for the party)." };
        ledgerFolder.Click += (_, _) => _panel.OpenFolder();
        menu.Items.Add(ledgerFolder);
        var folder = new MenuItem { Header = "Choose the game's data folder…", ToolTip = $"Where the game keeps its data. Now: {_store.LedgerRoot}" };
        folder.Click += async (_, _) => await ChooseRoot();
        menu.Items.Add(folder);
        var page = new MenuItem { Header = "Project page" };
        page.Click += (_, _) => _updates.OpenProjectPage();
        menu.Items.Add(page);
        menu.IsOpen = true;
    }

    /// <summary>The strip under the title bar: an update is ready, being fetched, failed, or the answer to "check now".</summary>
    private void ShowBanner()
    {
        if (_updates is null) return;
        _about.Foreground = new SolidColorBrush(_updates.State == UpdateState.Available ? Palette.AccentColor : Color.FromRgb(0xB0, 0xC0, 0xD0));
        if (!_updates.Visible)
        {
            _banner.Visibility = Visibility.Collapsed;
            return;
        }
        var offer = _updates.Offer;
        var text = new TextBlock
        {
            Text = _updates.State == UpdateState.Available && offer is not null
                ? $"Version {Update.AppVersion.Show(offer.Version)} is ready (you have {Update.AppVersion.Show(_updates.Current)})."
                : _updates.Message,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5),
        };
        if (_updates.State == UpdateState.Available && offer is { Notes.Length: > 0 })
            text.ToolTip = new ToolTip { Style = (Style)Ui.Theme["Tip"], Content = offer.Notes.Length > 700 ? offer.Notes[..700] + "…" : offer.Notes };
        var stack = new StackPanel { Margin = new Thickness(10, 7, 10, 7) };
        stack.Children.Add(text);
        if (_updates.State == UpdateState.Downloading)
            stack.Children.Add(new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, Value = _updates.Progress, Foreground = new SolidColorBrush(Palette.AccentColor), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(0) });
        var buttons = new WrapPanel();
        switch (_updates.State)
        {
            case UpdateState.Available:
                buttons.Children.Add(BannerButton(_updates.CanInstall ? "Update now" : "Open the download page", () => _ = _updates.InstallAsync(),
                    _updates.CanInstall ? "Downloads the new version, checks it, and restarts into it." : "This copy can't replace itself; opens the page to download it."));
                buttons.Children.Add(BannerButton("Later", _updates.Dismiss, "Ask again next time."));
                buttons.Children.Add(BannerButton("Skip this version", _updates.Skip, "Don't offer this version again (a newer one still will be)."));
                break;
            case UpdateState.Waiting:
                buttons.Children.Add(BannerButton("Restart now", _updates.RestartNow, "Restart into the new version now, without waiting for your timers."));
                buttons.Children.Add(BannerButton("Not now", _updates.NotNow, "Don't install by itself for the rest of this run. It is offered with the ordinary buttons at the next check."));
                break;
            case UpdateState.Failed:
                if (offer is not null) buttons.Children.Add(BannerButton("Open the download page", _updates.OpenDownloadPage, "Download it by hand."));
                buttons.Children.Add(BannerButton("Close", _updates.Dismiss, ""));
                break;
            case UpdateState.Notice:
                buttons.Children.Add(BannerButton("OK", _updates.Dismiss, ""));
                break;
        }
        if (buttons.Children.Count > 0) stack.Children.Add(buttons);
        _banner.Child = stack;
        _banner.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x23, 0x33));
        _banner.BorderBrush = new SolidColorBrush(Palette.AccentColor);
        _banner.BorderThickness = new Thickness(0, 0, 0, 1);
        _banner.Visibility = Visibility.Visible;
    }

    private static Button BannerButton(string text, Action click, string tip)
    {
        var b = new Button { Content = text, Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), FontSize = 11 };
        if (tip.Length > 0) b.ToolTip = tip;
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>
    /// Looks at which character the game is writing a ledger for and switches to it. This window always follows the character being played;
    /// the History window is where to look at another one, or at everyone.
    /// </summary>
    private async Task FollowAsync()
    {
        if (_busy) return;
        var showing = _store.Selected?.Directory;
        LedgerSource? next;
        try { next = await Task.Run(() => _store.DetectPlayed(showing)); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return; }
        if (next is null || string.Equals(next.Directory, _store.Selected?.Directory, StringComparison.OrdinalIgnoreCase)) return;
        await Follow(next);
        _panel.Say($"Now following {next.Character} on {next.ServerName}: the game is writing their ledger");
    }

    private async Task Follow(LedgerSource source)
    {
        _store.Select(source);
        _session.Reset();
        _settings.Server = source.Server;
        _settings.Character = source.Character;
        _settings.Save();
        ShowSelection();
        _panel.Refresh();
        await RefreshAsync(force: true);
    }

    private async Task ChooseRoot()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "The folder that holds the server folders (…\\LocalLow\\Niche Worlds Cult\\Monsters and Memories)", InitialDirectory = _store.LedgerRoot };
        if (dialog.ShowDialog(this) != true) return;
        _store.ChangeRoot(dialog.FolderName);
        _settings.LedgerRoot = dialog.FolderName;
        _settings.Save();
        if (_store.Sources.Count > 0) await Follow(_store.Sources.FirstOrDefault(s => s.HasLedger) ?? _store.Sources[0]);
        else
        {
            ShowSelection();
            _panel.Refresh();
        }
    }

    private void Remember()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.Panel = new Bounds(Left, Top, Width, Height);
        _saveSoon ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _saveSoon!.Stop(); _settings.Save(); }, Dispatcher);
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    private DispatcherTimer? _saveSoon;

    /// <summary>Saved on another monitor layout: pull it back onto the desktop.</summary>
    private void KeepOnScreen()
    {
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, l, Math.Max(l, r - 120));
        Top = Math.Clamp(Top, t, Math.Max(t, b - 60));
    }
}
