using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
public partial class HistoryWindow : Window
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
    private readonly Dictionary<string, UIElement> _pages = [];
    private readonly DispatcherTimer _timer;
    private readonly List<RadioButton> _presets = [];
    private readonly Button _day = new() { Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(10, 0, 0, 0), ToolTip = "Look at one particular day" };
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
        Resources.MergedDictionaries.Add(Ui.Theme);
        InitializeComponent();
        _smallWindow = store;
        _settings = settings;
        _focus = new HistoryFocus(new LootStore(store.StateRoot, store.LedgerRoot), StartScope(store, settings));
        _history = history;
        if (settings.Breakdown is { } b && b.Width >= MinWidth && b.Height >= MinHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (b.Left, b.Top, b.Width, b.Height);
            KeepOnScreen();
        }
        BuildPeriodBar();
        SourceInitialized += (_, _) => DarkTitleBar();
        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        Loaded += async (_, _) =>
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

    // ---- Pages --------------------------------------------------------------------------------------------------

    /// <summary>Shows a page by its key: "Loot", "Trades", "Vendors", "Changes" or "Harvest".</summary>
    public void ShowPage(string key)
    {
        if (!Pages.Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) key = "Loot";
        key = Pages.First(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Key;
        _key = key;
        _settings.HistoryPage = key;
        _settings.Save();
        Host.Content = Page(key);
        PeriodHost.Visibility = key == "Loot" ? Visibility.Collapsed : Visibility.Visible;
        BuildRail();
    }

    private UIElement Page(string key) => _pages.TryGetValue(key, out var page) ? page : _pages[key] = key switch
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
        Rail.ItemsSource = items;
        Rail.SelectedItem = items.First(i => i.Key == _key);
        _updating = false;
    }

    private void OnRail(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || Rail.SelectedItem is not RailItem item || item.Key == _key) return;
        ShowPage(item.Key);
    }

    // ---- The period and the filter ------------------------------------------------------------------------------

    private void BuildPeriodBar()
    {
        var bar = new DockPanel();

        var box = new Grid { Width = 280, VerticalAlignment = VerticalAlignment.Center };
        _filter = new TextBox { Style = (Style)Ui.Theme["Field"], ToolTip = "Show only rows with this item, trader or place in them" };
        var hint = new TextBlock { Text = "Filter by item, trader or place", Foreground = Palette.Muted, FontSize = 11.5, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        _filter.TextChanged += (_, _) =>
        {
            hint.Visibility = _filter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            // Wait for a pause in the typing, so each letter doesn't redraw the page
            _typing ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _typing.Stop();
            _typing.Tick -= OnTyped;
            _typing.Tick += OnTyped;
            _typing.Start();
        };
        box.Children.Add(_filter);
        box.Children.Add(hint);
        DockPanel.SetDock(box, Dock.Right);
        bar.Children.Add(box);

        var seg = new Border { CornerRadius = new CornerRadius(8), Background = (Brush)Ui.Theme["Subtle"], Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (period, index) in Period.Presets(DateTime.Now).Select((p, i) => (p, i)))
        {
            int at = index;
            var rb = new RadioButton { Content = new TextBlock { Text = period.Label, Margin = new Thickness(14, 0, 14, 0) }, Style = (Style)Ui.Theme["Segment"], GroupName = "HistoryPeriod", IsChecked = index == 0 };
            rb.Checked += (_, _) =>
            {
                if (_updating) return;
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
        PeriodHost.Child = bar;
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
        var menu = new ContextMenu { PlacementTarget = _day, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 420 };
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
        menu.IsOpen = true;
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
        ServerButton.Content = (scope.IsEveryone ? "All servers" : LedgerLocator.Capitalize(scope.Server!)) + "  ▾";
        CharacterButton.Content = (scope.IsOneCharacter ? scope.Character : "Everyone") + "  ▾";
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
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 460 };
        fill(menu, (header, scope, chosen) =>
        {
            var item = new MenuItem { Header = header, IsChecked = chosen };
            item.Click += (_, _) => SetScope(scope);
            menu.Items.Add(item);
        });
        menu.IsOpen = true;
    }

    private void OnServer(object sender, RoutedEventArgs e) => ScopeMenu(ServerButton, (menu, add) =>
    {
        var sources = _focus.Loot.Sources;
        add("All servers", HistoryScope.Everyone, _focus.Scope.IsEveryone);
        foreach (var name in LedgerLocator.Servers(sources))
        {
            var played = LedgerLocator.On(sources, name).Any(s => s.HasLedger);
            add(LedgerLocator.Capitalize(name) + (played ? "" : "  ·  no ledger yet"), HistoryScope.Of(name), string.Equals(_focus.Scope.Server, name, StringComparison.OrdinalIgnoreCase));
        }
    });

    private void OnCharacter(object sender, RoutedEventArgs e) => ScopeMenu(CharacterButton, (menu, add) =>
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

    // ---- Window chrome ------------------------------------------------------------------------------------------

    /// <summary>
    /// Puts the window next to <paramref name="anchor"/> (the panel): to its left if that is on the desktop, else to its right,
    /// at the same height, so it opens on the monitor the panel is on and not wherever the system would centre it (with several
    /// monitors that can be one nobody is looking at). For a first open, when no position was saved.
    /// </summary>
    public void PlaceBeside(Window anchor)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        double left = anchor.Left - Width - 12;
        if (left < SystemParameters.VirtualScreenLeft) left = anchor.Left + anchor.ActualWidth + 12;
        (Left, Top) = (left, anchor.Top);
        KeepOnScreen();
    }

    private void Remember()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.Breakdown = new Bounds(Left, Top, Width, Height);
        _saveSoon ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _saveSoon!.Stop(); _settings.Save(); }, Dispatcher);
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    /// <summary>Saved on another monitor layout: pull it back onto the desktop.</summary>
    private void KeepOnScreen()
    {
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, l, Math.Max(l, r - 200));
        Top = Math.Clamp(Top, t, Math.Max(t, b - 100));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void DarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }
}
