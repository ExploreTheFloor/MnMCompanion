using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MnMCompanion.Ledger;
using MnMCompanion.Loot;

namespace MnMCompanion;

/// <summary>
/// The loot panel's window: borderless, dragged by its title bar, resized from any edge, on top of the game unless
/// unpinned. The title bar carries the character (a menu of every character with a ledger on this PC), a pin and a close
/// button. It looks at the ledger every couple of seconds and redraws the panel when something changed.
/// </summary>
internal sealed class PanelWindow : ChromeWindow
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
    private readonly Border _banner = new() { IsVisible = false };
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
        Topmost = settings.OnTop;
        MinWidth = 300;
        MinHeight = 240;
        Place(settings.Panel, 480, 600, work => new PixelPoint(work.Right - 480 - 24, work.Y + 80));

        var caption = new TextBlock { Text = "Companion", Foreground = Palette.Freeze(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        _who = new TextBlock
        {
            Foreground = Palette.Text, FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        }.Tip("The character the game is writing a ledger for. This window follows whoever you play; the History window can look at anyone.");
        _pin = TitleButton(Glyphs.Pin, "Keep on top of other windows", () => SetOnTop(!Topmost));
        var close = TitleButton(Glyphs.Close, "Close", Close);
        _about = TitleButton(Glyphs.Info, "About Companion, and updates", OpenAboutMenu);
        var historyButton = LabelledButton(Glyphs.History, "History", "History: every day and every page, for this character or for everyone", OpenHistory);
        var timersButton = LabelledButton(Glyphs.Timer, "Timers", "Timers: your own timers, started by a key, and a timer for every corpse", () => TimersRequested?.Invoke());
        var title = new DockPanel { Background = Palette.Freeze(Color.FromRgb(0x10, 0x14, 0x18)), Height = 32, Cursor = new Cursor(StandardCursorType.SizeAll), LastChildFill = false };
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
        DragBy(title);
        Resized += (_, _) => ShowToolLabels();

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
        Body = root;
        ShowPage(PageNames.Contains(settings.MiniPage) ? settings.MiniPage : "Loot", save: false);
        ShowPin();
        ShowSelection();
        if (updates is not null)
        {
            updates.Changed += ShowBanner;
            ShowBanner();
        }

        RememberIn(settings, b => settings.Panel = b);
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
        Opened += async (_, _) =>
        {
            ShowToolLabels();
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
    private Control BuildBottomBar()
    {
        var grid = new UniformGrid { Columns = PageNames.Length };
        foreach (var name in PageNames)
        {
            var key = name;
            var rb = new RadioButton { Content = new TextBlock { Text = key }, GroupName = "MiniPage", IsChecked = key == _page }.Styled("segment");
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true && key != _page) ShowPage(key); };
            _nav[key] = rb;
            grid.Children.Add(rb);
        }
        var pages = new Border { CornerRadius = new CornerRadius(8), Background = Ui.Subtle, Padding = new Thickness(2), Child = grid };

        _status.Margin = new Thickness(2, 0, 2, 6);
        var stack = new StackPanel();
        stack.Children.Add(_status);
        stack.Children.Add(pages);
        return new Border { Background = Ui.Panel, BorderBrush = Ui.Stroke, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 8, 12, 8), Child = stack };
    }

    /// <summary>A button of the title bar: a picture, big enough to see, with the theme's hover highlight.</summary>
    private static Button TitleButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Content = glyph, Width = 30, Height = 26, Margin = new Thickness(1, 0, 1, 0), FontSize = 15,
            Foreground = Palette.Freeze(Color.FromRgb(0xC8, 0xD4, 0xE0)), VerticalAlignment = VerticalAlignment.Center,
        }.Styled("icon").Tip(tip);
        b.Click += (_, _) => click();
        return b;
    }

    private readonly List<TextBlock> _toolLabels = [];

    /// <summary>A title bar button that opens a window: its picture and its name, so it says what it is. The name is left off when the window is narrow.</summary>
    private Button LabelledButton(string glyph, string label, string tip, Action click)
    {
        var words = new TextBlock { Text = label, FontSize = 12.5, FontFamily = Ui.Text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _toolLabels.Add(words);
        var picture = new TextBlock { Text = glyph, FontFamily = Ui.Icons, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 10, 0) };
        content.Children.Add(picture);
        content.Children.Add(words);
        var b = new Button
        {
            Content = content, Width = double.NaN, MinWidth = 30, Height = 26, Margin = new Thickness(1, 0, 1, 0),
            Foreground = Palette.Freeze(Color.FromRgb(0xC8, 0xD4, 0xE0)), VerticalAlignment = VerticalAlignment.Center,
        }.Styled("icon").Tip(tip);
        b.Click += (_, _) => click();
        return b;
    }

    private void ShowToolLabels()
    {
        bool wide = Bounds.Width >= 420 || Bounds.Width == 0 && Width >= 420;
        foreach (var label in _toolLabels) label.IsVisible = wide;
    }

    private void SetOnTop(bool on)
    {
        Topmost = on;
        _settings.OnTop = on;
        _settings.Save();
        ShowPin();
    }

    private void ShowPin() => _pin.Foreground = Topmost ? Palette.You : Palette.Freeze(Color.FromRgb(0x70, 0x7A, 0x88));

    private void ShowSelection() => _who.Text = _store.Selected is { } s ? $"{s.Character} · {s.ServerName}" : "No ledger";

    /// <summary>Reads the ledger on a worker thread and redraws if it changed (or <paramref name="force"/>).</summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            bool changed = await Task.Run(_store.Refresh);
            changed |= StartSessionOnJoin();
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

    /// <summary>
    /// A player joining the group starts a new session (unless that is switched off under ⓘ): the loot before the join is archived, as New session does, and the
    /// session starts at the join. A burst of joins starts one: the next finds nothing looted since. The ledger is asked every time, switch or not, so turning it
    /// on later only reacts to joins that happen after.
    /// </summary>
    private bool StartSessionOnJoin()
    {
        var joins = _store.NewJoins();
        if (!_settings.NewSessionOnJoin || joins.Count == 0) return false;
        bool started = false;
        foreach (var join in joins)
        {
            if (_store.NewSession("party", join.Time) is null) continue;
            started = true;
            _session.AutoStarted((join.You ? "You joined a group" : $"{join.Who} joined the group") + ": a new session has started (the last one is archived).");
        }
        return started;
    }

    private static MenuItem Check(string header, bool on, string tip, Action<bool> changed, bool enabled = true)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on, IsEnabled = enabled }.Tip(tip, 600);
        item.Click += (_, _) => changed(item.IsChecked);
        return item;
    }

    private static MenuItem Item(string header, Action click, string? tip = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (tip is not null) item.Tip(tip, 600);
        item.Click += (_, _) => click();
        return item;
    }

    private void OpenAboutMenu()
    {
        if (_updates is null) return;
        var menu = new ContextMenu { PlacementTarget = _about, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = $"Companion {Update.AppVersion.Show(_updates.Current)}", IsEnabled = false });
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Check for updates now", () => _ = _updates.CheckAsync(manual: true), "Asks GitHub whether a newer version has been released."));
        MenuItem? install = null;
        menu.Items.Add(Check("Check for updates automatically", _settings.CheckForUpdates, "When the app starts and every few hours. Untick it and the app never asks GitHub by itself.",
            on => { _settings.CheckForUpdates = on; _settings.Save(); if (install is not null) install.IsEnabled = on; }));
        if (_updates.CanInstall)
        {
            install = Check("Install updates automatically", _settings.InstallUpdates,
                "A newer version is downloaded, checked against the checksum published with it, and installed; Companion restarts into it once none of your timers is running. Untick it and a newer version is only offered, and you press Update now.",
                on => { _settings.InstallUpdates = on; _settings.Save(); }, enabled: _settings.CheckForUpdates);
            menu.Items.Add(install);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Check("Start a new session when a player joins the group", _settings.NewSessionOnJoin,
            "When another player joins your group, or you join one, the loot so far is archived and the pages start again from that moment (the same as New session). Untick it and a session starts only when you press New session.",
            on => { _settings.NewSessionOnJoin = on; _settings.Save(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Check("Share my data with the community", _settings.Sharing,
            "Yes: your totals (including weekly counts of corpses looted, coin, items, gathering and sales) are shared for you, automatically, and you can use the community data. No (the default): nothing is shared and the community data isn't used.",
            on => { _settings.SetSharing(on); _settings.Save(); }));
        menu.Items.Add(Item("What does sharing include?…", () => ShareRequested?.Invoke(), "Shows exactly what is shared, and what never is"));
        menu.Items.Add(Check("Show the live map of my zone (uses mnmatlas.com)", _settings.MapsOnline,
            "The Map page shows the community atlas's map of the zone you are in. That loads the atlas's website, so it sees your zone and your internet address. Off by default.",
            on => { _settings.MapsOnline = on; _settings.Save(); _map.Refresh(); }));
        menu.Items.Add(Check("Load the recipe list from the community wiki (uses monstersandmemories.wiki)", _settings.RecipesOnline,
            "The Recipes page shows the community wiki's tradeskill recipes. It loads them by itself the first time you open the page and then at most once a day when you open it again, so that site sees your internet address (nothing from your game or ledger is sent). The list is kept on this PC. Untick it and the wiki is never asked.",
            on => { _settings.RecipesOnline = on; _settings.Save(); _recipes.Refresh(); }));
        menu.Items.Add(Item("Open the ledger folder", _panel.OpenFolder, "Opens the folder where the game keeps this character's ledger files (one a day for the character, one for the party)."));
        menu.Items.Add(Item("Choose the game's data folder…", () => _ = ChooseRoot(), $"Where the game keeps its data. Now: {_store.LedgerRoot}"));
        menu.Items.Add(Item("Project page", _updates.OpenProjectPage));
        menu.Open(_about);
    }

    /// <summary>The strip under the title bar: an update is ready, being fetched, failed, or the answer to "check now".</summary>
    private void ShowBanner()
    {
        if (_updates is null) return;
        _about.Foreground = _updates.State == UpdateState.Available ? Palette.You : Palette.Freeze(Color.FromRgb(0xB0, 0xC0, 0xD0));
        if (!_updates.Visible)
        {
            _banner.IsVisible = false;
            return;
        }
        var offer = _updates.Offer;
        var text = new TextBlock
        {
            Text = _updates.State == UpdateState.Available && offer is not null
                ? $"Version {Update.AppVersion.Show(offer.Version)} is ready (you have {Update.AppVersion.Show(_updates.Current)})."
                : _updates.Message,
            Foreground = Palette.Freeze(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5),
        };
        if (_updates.State == UpdateState.Available && offer is { Notes.Length: > 0 })
            text.Tip(offer.Notes.Length > 700 ? offer.Notes[..700] + "…" : offer.Notes);
        var stack = new StackPanel { Margin = new Thickness(10, 7, 10, 7) };
        stack.Children.Add(text);
        if (_updates.State == UpdateState.Downloading)
            stack.Children.Add(new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, Value = _updates.Progress, Foreground = Palette.You, Background = Palette.Freeze(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
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
        _banner.Background = Palette.Freeze(Color.FromRgb(0x18, 0x23, 0x33));
        _banner.BorderBrush = Palette.You;
        _banner.BorderThickness = new Thickness(0, 0, 0, 1);
        _banner.IsVisible = true;
    }

    private static Button BannerButton(string text, Action click, string tip)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), FontSize = 11 }.Styled("text");
        if (tip.Length > 0) b.Tip(tip);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
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
        var start = await StorageProvider.TryGetFolderFromPathAsync(_store.LedgerRoot);
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "The folder that holds the server folders (…/LocalLow/Niche Worlds Cult/Monsters and Memories)", SuggestedStartLocation = start,
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } folder) return;
        _store.ChangeRoot(folder);
        _settings.LedgerRoot = folder;
        _settings.Save();
        if (_store.Sources.Count > 0) await Follow(_store.Sources.FirstOrDefault(s => s.HasLedger) ?? _store.Sources[0]);
        else
        {
            ShowSelection();
            _panel.Refresh();
        }
    }
}
