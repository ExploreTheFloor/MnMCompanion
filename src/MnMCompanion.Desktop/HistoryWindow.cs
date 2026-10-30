using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MnMCompanion.Data;
using MnMCompanion.History;
using MnMCompanion.Ledger;
using MnMCompanion.Loot;

namespace MnMCompanion;

public sealed record RailItem(string Key, string Title, string Sub);

/// <summary>
/// The History window: a rail down the left with one page per kind of thing that happens (Loot, Trades, Vendors, Drops / Pickups /
/// Destroys, Harvest), and the chosen page on the right. Every page but Loot also gets the bar across the top that sets the period
/// (today, yesterday, a week, a month, everything, or one day) and filters by item, partner or place; the choice is kept as the
/// reader moves between pages. Loot keeps its own list of days. While open, the window asks for the saved history to be brought
/// up to date every few seconds, and a page redraws when something new has arrived.
/// </summary>
public sealed class HistoryWindow : Window
{
    private static readonly (string Key, string Title)[] Pages =
    [
        ("Loot", "Loot"), ("Trades", "Trades"), ("Vendors", "Vendors"), ("Changes", "Drops / Pickups / Destroys"), ("Harvest", "Harvest"),
    ];

    private readonly LootStore _smallWindow;   // the small window's store: who it follows, and the game's data folder
    private readonly HistoryFocus _focus;
    private readonly Settings _settings;
    private readonly HistoryData _history;
    private readonly HistoryView _view = new();
    private readonly Dictionary<string, Control> _pages = [];
    private readonly DispatcherTimer _timer;
    private readonly List<RadioButton> _presets = [];
    private readonly Button _day = new Button { Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(10, 0, 0, 0) }.Styled("text").Tip("Look at one particular day");
    private readonly Button _serverButton = new Button { Padding = new Thickness(8, 5, 8, 5), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left }.Styled("text").Tip("Which server to look at: all of them, or one");
    private readonly Button _characterButton = new Button { Padding = new Thickness(8, 5, 8, 5), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left }.Styled("text")
        .Tip("Everyone on the server (or on the PC), or one character. The small window always follows whoever you play.");
    private readonly ListBox _railList = new ListBox().Styled("nav");
    private readonly Border _periodHost = new() { BorderBrush = Ui.Stroke, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(20, 10, 20, 10), IsVisible = false };
    private readonly ContentControl _host = new();
    private DispatcherTimer? _saveSoon;
    private DispatcherTimer? _typing;
    private TextBox? _filter;
    private string _key = "Loot";
    private int _preset;                 // the quick period chosen, or -1 for a particular day
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private bool _updating;
    private bool _pageRequested;   // the small window asked for a page (a name, a row): don't send the reader back to the page they last left this on
    private HistoryCounts? _counts;
    private int _trades;   // trades, not rows: the ledger writes two rows for one
    private string _rail = "";

    internal HistoryWindow(LootStore store, Settings settings, HistoryData history)
    {
        Title = "Companion · History";
        Width = 1560;
        Height = 900;
        MinWidth = 1180;
        MinHeight = 600;
        Background = Ui.Window;
        Foreground = Palette.Text;
        FontFamily = Ui.Text;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _smallWindow = store;
        _settings = settings;
        _focus = new HistoryFocus(new LootStore(store.StateRoot, store.LedgerRoot), StartScope(store, settings));
        _history = history;
        if (settings.Breakdown is { } b && b.Width >= MinWidth && b.Height >= MinHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Width, Height) = (b.Width, b.Height);
            Position = KeepOnScreen(new PixelPoint((int)b.Left, (int)b.Top));
        }
        Content = BuildLayout();
        BuildPeriodBar();
        PositionChanged += (_, _) => Remember();
        Resized += (_, _) => Remember();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        Opened += async (_, _) =>
        {
            ShowScope();
            await Task.Run(_focus.Loot.Refresh);   // this window reads the ledgers itself, so its first page already has its days
            if (_pageRequested) { if (_key == "Loot" && Page("Loot") is LootHistoryPage loot) loot.Tick(); }   // its data has just arrived
            else ShowPage(Pages.Any(p => p.Key == settings.HistoryPage) ? settings.HistoryPage! : "Loot");
            _ = history.RefreshAsync();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private Control BuildLayout()
    {
        _railList.ItemTemplate = new FuncDataTemplate<RailItem>((item, _) =>
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = item?.Title, FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text = item?.Sub, FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            return stack;
        });
        _railList.SelectionChanged += (_, _) => OnRail();

        var top = new StackPanel { Margin = new Thickness(6, 0, 4, 14) };
        top.Children.Add(new TextBlock { Text = "History", FontSize = 18, FontWeight = FontWeight.SemiBold });
        top.Children.Add(new TextBlock { Text = "Server", FontSize = 10.5, Foreground = Palette.Muted, Margin = new Thickness(0, 12, 0, 3) });
        top.Children.Add(_serverButton);
        top.Children.Add(new TextBlock { Text = "Character", FontSize = 10.5, Foreground = Palette.Muted, Margin = new Thickness(0, 10, 0, 3) });
        top.Children.Add(_characterButton);
        _serverButton.Click += (_, _) => OnServer();
        _characterButton.Click += (_, _) => OnCharacter();

        var railDock = new DockPanel { Margin = new Thickness(12, 16, 10, 8) };
        DockPanel.SetDock(top, Dock.Top);
        railDock.Children.Add(top);
        railDock.Children.Add(_railList);
        var rail = new Border { Background = Ui.Panel, BorderBrush = Ui.Stroke, BorderThickness = new Thickness(0, 0, 1, 0), Child = railDock };

        var right = new DockPanel();
        DockPanel.SetDock(_periodHost, Dock.Top);
        right.Children.Add(_periodHost);
        right.Children.Add(_host);
        Grid.SetColumn(right, 1);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(rail);
        grid.Children.Add(right);
        return grid;
    }

    // ---- Pages --------------------------------------------------------------------------------------------------

    /// <summary>Shows a page by its key: "Loot", "Trades", "Vendors", "Changes" or "Harvest".</summary>
    public void ShowPage(string key)
    {
        if (!Pages.Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) key = "Loot";
        key = Pages.First(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Key;
        _key = key;
        _settings.HistoryPage = key;
        _settings.Save();
        _host.Content = Page(key);
        _periodHost.IsVisible = key != "Loot";
        BuildRail();
    }

    private Control Page(string key) => _pages.TryGetValue(key, out var page) ? page : _pages[key] = key switch
    {
        "Trades" => new TradesPage(_focus, _history, _view),
        "Vendors" => new VendorsPage(_focus, _history, _view),
        "Changes" => new ItemChangesPage(_focus, _history, _view),
        "Harvest" => new HarvestPage(_focus, _history, _view),
        _ => new LootHistoryPage(_focus.Loot),
    };

    /// <summary>Reads and draws the current page now, on this thread: for a picture, where nothing is allowed to finish later.</summary>
    internal void DrawNow()
    {
        _focus.Loot.Refresh();
        if (Page(_key) is IHistoryPage page) page.RefreshNow();
        if (_history.Store is { } store) (_counts, _trades) = CountsOf(store, _focus.Scope);
        ShowScope();
        BuildRail();
    }

    /// <summary>Shows a day of loot (from the panel), optionally one session of it and one player's breakdown.</summary>
    public void ShowDay(DateOnly day, string? member = null, LootSession? session = null)
    {
        // A session, and the names in a day, are one character's (the one the small window follows), so that is who to look at
        if ((session is not null || member is not null) && _smallWindow.Selected is { } following)
            SetScope(HistoryScope.Of(following.Server, following.Character));
        _pageRequested = true;
        ShowPage("Loot");
        ((LootHistoryPage)Page("Loot")).ShowDay(day, member, session);
    }

    /// <summary>Opens a page for the character the small window follows (from a row of its Vendor, Trades, Items or Harvest page).</summary>
    public void ShowPageFor(string key)
    {
        if (_smallWindow.Selected is { } following) SetScope(HistoryScope.Of(following.Server, following.Character));
        _pageRequested = true;
        ShowPage(key);
    }

    /// <summary>Opens a tab of the open page by name (on Loot: "Looters", "Items", "Mobs", "Drop rates", "Loot log").</summary>
    public void PickTab(string tab)
    {
        if (Page(_key) is IHistoryPage page) page.PickTab(tab);
        else ((LootHistoryPage)Page("Loot")).PickTab(tab);
    }

    /// <summary>Chooses a quick period by its name ("Today", "Yesterday", "7 days", "30 days", "All time"), as a click on it would.</summary>
    public void SetPeriod(string label)
    {
        var presets = Period.Presets(DateTime.Now);
        var at = presets.ToList().FindIndex(p => p.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && at < _presets.Count) _presets[at].IsChecked = true;
    }

    /// <summary>Types into the search box of the open page, as typing would (for a picture): the Loot page's days and answer, or the filter of every other page.</summary>
    public void Search(string text, string? member = null)
    {
        if (_key == "Loot") { ((LootHistoryPage)Page("Loot")).SearchFor(text, member); return; }
        if (_filter is not null) _filter.Text = text;
        _view.SetFilter(text);
    }

    /// <summary>Opens one monster's drop table on the Loot page (for a picture).</summary>
    public void PickDrop(string mob)
    {
        ShowPage("Loot");
        ((LootHistoryPage)Page("Loot")).PickDrop(mob);
    }

    // ---- The rail -----------------------------------------------------------------------------------------------

    private void BuildRail()
    {
        string Count(int n, string what) => n == 0 ? "nothing yet" : $"{n:N0} {what}";
        var c = _counts;
        var days = _focus.Loot.Days().Count;
        var items = new List<RailItem>
        {
            new("Loot", "Loot", days == 0 ? "nothing yet" : $"{days:N0} {(days == 1 ? "day" : "days")}"),
            new("Trades", "Trades", c is null ? "" : Count(_trades, _trades == 1 ? "trade" : "trades")),
            new("Vendors", "Vendors", c is null ? "" : Count(c.Vendor, "sales and buys")),
            new("Changes", "Drops / Pickups / Destroys", c is null ? "" : Count(c.ItemMoves, "changes")),
            new("Harvest", "Harvest", c is null ? "" : Count(c.Harvest, "harvests")),
        };
        var sig = string.Join('|', items.Select(i => i.Sub)) + _key;
        if (sig == _rail) return;
        _rail = sig;
        _updating = true;
        _railList.ItemsSource = items;
        _railList.SelectedItem = items.First(i => i.Key == _key);
        _updating = false;
    }

    private void OnRail()
    {
        if (_updating || _railList.SelectedItem is not RailItem item || item.Key == _key) return;
        ShowPage(item.Key);
    }

    // ---- The period and the filter ------------------------------------------------------------------------------

    private void BuildPeriodBar()
    {
        var bar = new DockPanel();

        _filter = new TextBox { Width = 300, VerticalAlignment = VerticalAlignment.Center }.Styled("field")
            .Tip("Show only the rows that have these words in them: every word has to be there, in any order and any case, and part of a word is enough");
        _filter.TextChanged += (_, _) =>
        {
            // Wait for a pause in the typing, so each letter doesn't redraw the page
            _typing ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _typing.Stop();
            _typing.Tick -= OnTyped;
            _typing.Tick += OnTyped;
            _typing.Start();
        };
        DockPanel.SetDock(_filter, Dock.Right);
        bar.Children.Add(_filter);

        var seg = new Border { CornerRadius = new CornerRadius(8), Background = Ui.Subtle, Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (period, index) in Period.Presets(DateTime.Now).Select((p, i) => (p, i)))
        {
            int at = index;
            var rb = new RadioButton { Content = new TextBlock { Text = period.Label, Margin = new Thickness(14, 0, 14, 0) }, GroupName = "HistoryPeriod", IsChecked = index == 0 }.Styled("segment");
            rb.IsCheckedChanged += (_, _) =>
            {
                if (_updating || rb.IsChecked != true) return;
                _preset = at;
                _day.Content = "Pick a day  ▾";
                _view.Set(Period.Presets(DateTime.Now)[at]);
            };
            _presets.Add(rb);
            row.Children.Add(rb);
        }
        seg.Child = row;
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(seg);
        _day.Content = "Pick a day  ▾";
        _day.Click += async (_, _) => await OpenDayMenu();
        left.Children.Add(_day);
        bar.Children.Add(left);
        _periodHost.Child = bar;
    }

    private void OnTyped(object? sender, EventArgs e)
    {
        _typing?.Stop();
        _view.SetFilter(_filter?.Text ?? "");
    }

    /// <summary>A menu of the days this character has anything on, newest first.</summary>
    private async Task OpenDayMenu()
    {
        if (_history.Store is not { } store) return;
        var scope = _focus.Scope;
        var days = await Task.Run(() => store.Days(scope.Server, scope.Character));
        var menu = new ContextMenu { PlacementTarget = _day, Placement = PlacementMode.Bottom, MaxHeight = 420 };
        if (days.Count == 0) menu.Items.Add(new MenuItem { Header = "No days yet", IsEnabled = false });
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var day in days)
        {
            var d = day;
            var item = new MenuItem { Header = Period.Of(d, today).Label + (d.Year == today.Year ? "" : ", " + d.Year.ToString(CultureInfo.InvariantCulture)) };
            item.Click += (_, _) =>
            {
                _preset = -1;
                _updating = true;
                foreach (var rb in _presets) rb.IsChecked = false;
                _updating = false;
                var period = Period.Of(d, today);
                _day.Content = period.Label + "  ▾";
                _view.Set(period);
            };
            menu.Items.Add(item);
        }
        menu.Open(_day);
    }

    // ---- Keeping up ---------------------------------------------------------------------------------------------

    private async void Tick()
    {
        try { await Keep(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }   // a failed refresh is tried again in three seconds
    }

    private async Task Keep()
    {
        // Past midnight, "Today" is a different day
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _today)
        {
            _today = today;
            if (_preset >= 0) _view.Set(Period.Presets(DateTime.Now)[_preset]);
        }
        // The game's folder may have been changed from the small window, and a new character may have started a ledger
        if (_smallWindow.LedgerRoot != _focus.Loot.LedgerRoot) _focus.Loot.ChangeRoot(_smallWindow.LedgerRoot);
        _focus.Sync();
        await Task.Run(_focus.Loot.Refresh);
        await _history.RefreshAsync();
        if (Page(_key) is IHistoryPage page) page.Tick();
        if (_history.Store is { } store)
        {
            var scope = _focus.Scope;
            var counts = await Task.Run(() => { try { return CountsOf(store, scope); } catch (Exception ex) when (ex is not OutOfMemoryException) { return ((HistoryCounts?)null, 0); } });
            if (counts.Item1 is not null) (_counts, _trades) = (counts.Item1, counts.Item2);
        }
        BuildRail();
    }

    private static (HistoryCounts Counts, int Trades) CountsOf(HistoryStore store, HistoryScope scope) =>
        (store.Counts(scope.Server, scope.Character), TradeSummary.Pair(store.Trades(scope.Server, scope.Character)).Count);

    // ---- Whose history ------------------------------------------------------------------------------------------

    /// <summary>What to show on opening: the player's last choice, or the character the small window follows when they have not chosen.</summary>
    private static HistoryScope StartScope(LootStore small, Settings settings)
    {
        if (settings.HistoryScopeChosen)
        {
            var saved = HistoryScope.Of(settings.HistoryServer, settings.HistoryCharacter);
            if (saved.IsEveryone || saved.Pick(small.Sources).Count > 0) return saved;
        }
        return small.Selected is { } s ? HistoryScope.Of(s.Server, s.Character) : HistoryScope.Everyone;
    }

    private void ShowScope()
    {
        var scope = _focus.Scope;
        // The character's name is searched only when rows can belong to several (one character's own name would match every row)
        if (_filter is not null) _filter.PlaceholderText = scope.IsOneCharacter ? "Search by item, trader or place" : "Search by item, trader, place or character";
        _serverButton.Content = (scope.IsEveryone ? "All servers" : LedgerLocator.Capitalize(scope.Server!)) + "  ▾";
        _characterButton.Content = (scope.IsOneCharacter ? scope.Character : "Everyone") + "  ▾";
    }

    private void SetScope(HistoryScope scope)
    {
        _focus.Set(scope);
        _settings.HistoryServer = scope.Server;
        _settings.HistoryCharacter = scope.Character;
        _settings.HistoryScopeChosen = true;
        _settings.Save();
        ShowScope();
        Tick();
    }

    private void ScopeMenu(Button anchor, Action<ContextMenu, Action<string, HistoryScope, bool>> fill)
    {
        _focus.Loot.RescanSources();
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MaxHeight = 460 };
        fill(menu, (header, scope, chosen) =>
        {
            var item = new MenuItem { Header = header, ToggleType = chosen ? MenuItemToggleType.CheckBox : MenuItemToggleType.None, IsChecked = chosen };
            item.Click += (_, _) => SetScope(scope);
            menu.Items.Add(item);
        });
        menu.Open(anchor);
    }

    private void OnServer() => ScopeMenu(_serverButton, (menu, add) =>
    {
        var sources = _focus.Loot.Sources;
        add("All servers", HistoryScope.Everyone, _focus.Scope.IsEveryone);
        foreach (var name in LedgerLocator.Servers(sources))
        {
            var played = LedgerLocator.On(sources, name).Any(s => s.HasLedger);
            add(LedgerLocator.Capitalize(name) + (played ? "" : "  ·  no ledger yet"), HistoryScope.Of(name), string.Equals(_focus.Scope.Server, name, StringComparison.OrdinalIgnoreCase));
        }
    });

    private void OnCharacter() => ScopeMenu(_characterButton, (menu, add) =>
    {
        var scope = _focus.Scope;
        var sources = scope.IsEveryone ? _focus.Loot.Sources : LedgerLocator.On(_focus.Loot.Sources, scope.Server!);
        add(scope.IsEveryone ? "Everyone" : "Everyone on " + LedgerLocator.Capitalize(scope.Server!), HistoryScope.Of(scope.Server), !scope.IsOneCharacter);
        foreach (var s in sources)
        {
            // With every server in view a character needs its server beside it: two servers can have one of the same name
            var name = (scope.IsEveryone ? s.Label : s.Character) + (s.HasLedger ? "" : "  ·  no ledger yet");
            add(name, HistoryScope.Of(s.Server, s.Character), scope.IsOneCharacter && string.Equals(scope.Server, s.Server, StringComparison.OrdinalIgnoreCase) && string.Equals(scope.Character, s.Character, StringComparison.OrdinalIgnoreCase));
        }
    });

    // ---- Window placement ---------------------------------------------------------------------------------------

    /// <summary>
    /// Puts the window next to <paramref name="anchor"/> (the panel): to its left if that is on the desktop, else to its right,
    /// at the same height, so it opens on the monitor the panel is on and not wherever the system would centre it. For a first open, when no position was saved.
    /// </summary>
    public void PlaceBeside(Window anchor)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        var scale = anchor.DesktopScaling;
        int left = anchor.Position.X - (int)((Width + 12) * scale);
        var screens = anchor.Screens.All;
        if (screens.Count > 0 && left < screens.Min(s => s.Bounds.X)) left = anchor.Position.X + (int)((anchor.Bounds.Width + 12) * scale);
        Position = KeepOnScreen(new PixelPoint(left, anchor.Position.Y));
    }

    private void Remember()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.Breakdown = new Bounds(Position.X, Position.Y, Width, Height);
        _saveSoon ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _saveSoon!.Stop(); _settings.Save(); });
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    /// <summary>Saved on another monitor layout: pull it back onto the desktop.</summary>
    private PixelPoint KeepOnScreen(PixelPoint at)
    {
        var all = Screens.All;
        if (all.Count == 0) return at;
        int l = all.Min(s => s.Bounds.X), t = all.Min(s => s.Bounds.Y), r = all.Max(s => s.Bounds.Right), b = all.Max(s => s.Bounds.Bottom);
        return new PixelPoint(Math.Clamp(at.X, l, Math.Max(l, r - 200)), Math.Clamp(at.Y, t, Math.Max(t, b - 100)));
    }
}
