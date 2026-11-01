using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MnMCompanion.Loot;
using MnMCompanion.Market;
using MnMCompanion.Update;

namespace MnMCompanion;

/// <summary>
/// The Market page: what things sell for between players, from mnm-db.com's auction feed (prices read off the auction channel of the LiveMNM
/// stream, one server, published every six hours). Three tabs: <b>Selling</b>, every item someone is selling, by kind, with a search box, each
/// opening to its sellers, buyers, the day-by-day prices and every change; <b>Buying</b>, what players are bidding for and asking after;
/// <b>Yours</b>, what the loot you took this session would fetch. The feed holds only each player's current price, so Companion keeps its own book on this PC
/// (<see cref="MarketBook"/>) and the history grows from the snapshots it takes. It is loaded by itself the first time the page is shown and
/// once per publish after that (<see cref="Settings.MarketOnline"/>, on unless the player switched it off in the ⓘ menu). A picture-taking
/// run reads a file instead (<c>--market</c>) and never asks the site.
/// </summary>
internal sealed class MarketPanel : Border, IMiniPage
{
    /// <summary>A list shows this many rows at most; the rest is "narrow the search".</summary>
    private const int MaxRows = 150;

    private const int FirstBatch = 40, NextBatch = 40;

    private static readonly string[] Tabs = ["Selling", "Buying", "Yours"];
    private static readonly (string Key, string Label)[] Sorts = [("Active", "Most active"), ("Name", "Name"), ("PriceDown", "Price, high first"), ("PriceUp", "Price, low first"), ("Newest", "Newest")];

    private readonly Settings _settings;
    private readonly TextBlock _status;
    private readonly string? _file;
    private readonly LootStore _store;
    private readonly SessionScope _session;

    private readonly TextBlock _sub = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _update = new() { Style = (Style)Ui.Theme["IconButton"], Content = "", FontFamily = (FontFamily)Ui.Theme["Icons"], Width = 30, Height = 26, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBox _search = new() { Style = (Style)Ui.Theme["Field"], FontSize = 12.5, Padding = new Thickness(0, 2, 22, 2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { Text = "Search items or sellers", Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly Button _clear = new() { Style = (Style)Ui.Theme["IconButton"], Content = "", FontFamily = (FontFamily)Ui.Theme["Icons"], Width = 24, Height = 22, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0), Visibility = Visibility.Collapsed, ToolTip = "Clear the search" };
    private readonly Button _kindButton = new() { Style = (Style)Ui.Theme["TextButton"], HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 5, 8, 5), FontSize = 12, Focusable = false, Margin = new Thickness(0, 0, 4, 0) };
    private readonly Button _sortButton = new() { Style = (Style)Ui.Theme["TextButton"], HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 5, 8, 5), FontSize = 12, Focusable = false, Margin = new Thickness(0, 0, 6, 0) };
    private readonly FrameworkElement _controls;
    private readonly Border _tabBar;
    private readonly Dictionary<string, RadioButton> _tabButtons = [];
    private readonly StackPanel _body = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _typing;

    private MarketBook? _book;
    private MarketMeta _meta = new();
    private MarketSnapshot _market = MarketSnapshot.Empty;
    private int _bookVersion, _marketVersion = -1;
    private bool _cacheRead;
    private string _tab = "Selling";
    private MarketKind? _kind;
    private string _sort = "Active";
    private string _query = "";
    private bool _fetching;
    private string _failure = "";
    private string _seen = "";

    /// <summary>The items whose breakdown is open, by key.</summary>
    private readonly HashSet<string> _open = [];

    private readonly List<Func<FrameworkElement>> _later = [];
    private int _generation;
    private double? _restoreOffset;

    /// <param name="file">A feed to read instead of the kept book and the site (for a picture or a test); null for the real thing.</param>
    public MarketPanel(LootStore store, SessionScope session, Settings settings, TextBlock status, string? file = null)
    {
        _store = store;
        _session = session;
        _settings = settings;
        _status = status;
        _file = file;
        _kind = Enum.TryParse<MarketKind>(settings.MarketKind, out var k) ? k : null;
        _sort = Sorts.Any(s => s.Key == settings.MarketSort) ? settings.MarketSort! : "Active";
        Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x15));
        Padding = new Thickness(12, 10, 12, 8);

        var title = new TextBlock { Text = "Market", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(title);
        titles.Children.Add(_sub);
        _update.ToolTip = "Ask mnm-db.com whether a newer feed is out. It publishes every six hours, so the page asks by itself once per publish; this button no more than once in ten minutes. When nothing changed the site sends nothing back.";
        _update.Click += (_, _) => _ = FetchAsync(manual: true);
        var head = new DockPanel();
        DockPanel.SetDock(_update, Dock.Right);
        head.Children.Add(_update);
        head.Children.Add(titles);

        var searchBox = new Grid();
        Grid.SetColumn(searchBox, 2);
        searchBox.Children.Add(_search);
        searchBox.Children.Add(_hint);
        searchBox.Children.Add(_clear);
        _search.ToolTip = "Every word has to be in the item's name (a seller's or buyer's name counts too). Any order, any case, part of a word is enough.";
        _typing = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
        _typing.Tick += (_, _) => { _typing.Stop(); Typed(); };
        _search.TextChanged += (_, _) =>
        {
            _hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _clear.Visibility = _search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _typing.Stop();
            _typing.Start();
        };
        _clear.Click += (_, _) => { _search.Text = ""; _search.Focus(); };

        _kindButton.ToolTip = "Which kind of item to show";
        _kindButton.Click += (_, _) => OpenKindMenu();
        _sortButton.ToolTip = "The order of the list";
        _sortButton.Click += (_, _) => OpenSortMenu();
        var controls = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_sortButton, 1);
        controls.Children.Add(_kindButton);
        controls.Children.Add(_sortButton);
        controls.Children.Add(searchBox);
        _controls = controls;

        var tabs = new UniformGrid { Columns = Tabs.Length };
        foreach (var t in Tabs)
        {
            var name = t;
            var rb = new RadioButton { Content = new TextBlock { Text = t }, Style = (Style)Ui.Theme["Segment"], GroupName = "MiniMarket", IsChecked = t == _tab, Focusable = false };
            rb.Checked += (_, _) => { if (_tab != name) { _tab = name; _scroll?.ScrollToTop(); Build(); } };
            _tabButtons[t] = rb;
            tabs.Children.Add(rb);
        }
        _tabBar = new Border { CornerRadius = new CornerRadius(8), Background = (Brush)Ui.Theme["Subtle"], Padding = new Thickness(2), Margin = new Thickness(0, 8, 0, 0), Child = tabs };

        var credit = new TextBlock
        {
            Text = "Prices: mnm-db.com, read from players' /auction posts on the LiveMNM stream (one server). Published every six hours; a post is what a player asked, not what was paid.",
            FontSize = 10, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        };

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _body };
        var top = new StackPanel();
        top.Children.Add(head);
        top.Children.Add(_tabBar);
        top.Children.Add(_controls);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(credit, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(credit);
        root.Children.Add(_scroll);
        Child = root;
        session.Changed += () => { if (IsVisible && _tab == "Yours") Build(); };
    }

    public FrameworkElement Element => this;

    public bool NeedsHistory => false;

    public void Say(string text) => _status.Text = text;

    public void Refresh()
    {
        ReadKept();
        if (_body.Children.Count == 0 || Signature() != _seen) Build();
        AutoFetch();
    }

    public void RefreshNow()
    {
        ReadKept();
        Build();
        Flush();
    }

    public void Tick()
    {
        if (!IsVisible) return;
        if (Signature() != _seen) Build();
        AutoFetch();
    }

    /// <summary>For a picture: a tab and a search without clicking or typing.</summary>
    public void Show(string? tab, string? search)
    {
        ReadKept();
        if (tab is { Length: > 0 } && Tabs.FirstOrDefault(t => t.Equals(tab, StringComparison.OrdinalIgnoreCase)) is { } found)
        {
            _tab = found;
            _tabButtons[found].IsChecked = true;
        }
        _query = search ?? "";
        _search.Text = _query;
        _typing.Stop();
        Build();
        Flush();
    }

    private string Signature() =>
        $"{_settings.MarketOnline}|{_bookVersion}|{_fetching}|{_failure}|{_tab}|{_kind}|{_sort}|{(_tab == "Yours" ? _session.Key + "|" + _store.Version + "|" + _store.SelectionKey : "")}";

    // ------------------------------------------------------------------ the book

    private void ReadKept()
    {
        if (_cacheRead) return;
        _cacheRead = true;
        if (_file is not null)
        {
            try
            {
                if (MarketFeed.TryParse(File.ReadAllText(_file)) is { } feed)
                {
                    _book = new MarketBook();
                    _book.Merge(feed);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        else
        {
            _book = MarketBook.Load();
            _meta = MarketMeta.Load();
        }
        _bookVersion++;
    }

    private MarketSnapshot Market()
    {
        if (_marketVersion != _bookVersion)
        {
            _market = _book is null ? MarketSnapshot.Empty : MarketSnapshot.From(_book, null, Now);
            _marketVersion = _bookVersion;
        }
        return _market;
    }

    /// <summary>The moment prices are judged from: the clock, or the snapshot's own time for a picture (so the picture is the same whenever it is drawn).</summary>
    private DateTime Now => _file is not null && _book?.GeneratedAt is { } made ? made : DateTime.UtcNow;

    private void AutoFetch()
    {
        if (_file is not null || !_settings.MarketOnline || _fetching) return;
        if (MarketRefresh.Decide(DateTime.UtcNow, _book?.GeneratedAt, _meta, manual: false).Step != MarketStep.Ask) return;
        _ = FetchAsync(manual: false);
    }

    private void SaveMeta()
    {
        if (!_settings.ReadOnly) _meta.Save();
    }

    private async Task FetchAsync(bool manual)
    {
        if (_fetching || _file is not null) return;
        var plan = MarketRefresh.Decide(DateTime.UtcNow, _book?.GeneratedAt, _meta, manual);
        if (plan.Step != MarketStep.Ask)
        {
            if (manual) _status.Text = plan.Step == MarketStep.Fresh ? "The feed on this PC is the current one; the site publishes every six hours." : plan.Why;
            return;
        }
        _fetching = true;
        _failure = "";
        _meta.TriedUtc = DateTime.UtcNow;   // remembered before asking, so a crash or a restart can't ask twice
        SaveMeta();
        Build();
        try
        {
            using var client = new MarketFeedClient(AppVersion.Current());
            var result = await client.FetchAsync(_book is not null ? _meta.ETag : null);
            var now = DateTime.UtcNow;
            if (result.Ok && result.NotModified)
            {
                _meta.CheckedUtc = now;
                _meta.NotBeforeUtc = null;
                if (manual) _status.Text = "mnm-db.com hasn't published a newer feed yet.";
            }
            else if (result.Ok)
            {
                var json = result.Json;
                var feed = await Task.Run(() => MarketFeed.TryParse(json));
                if (feed is null) _failure = "The site's answer wasn't the auction feed.";
                else
                {
                    var book = _book ?? new MarketBook();
                    var merged = await Task.Run(() => { var r = book.Merge(feed); if (!_settings.ReadOnly) book.Save(); return r; });
                    _book = book;
                    _bookVersion++;
                    _meta.ETag = result.ETag;
                    _meta.CheckedUtc = now;
                    _meta.NotBeforeUtc = null;
                    _status.Text = merged.Touched == 0 ? "The feed is the one already on this PC."
                        : $"Loaded the feed: {merged.Added:N0} new {(merged.Added == 1 ? "price" : "prices")}, {merged.Changed:N0} changed.";
                }
            }
            else
            {
                _failure = result.Message;
                if (result.RetryAfter is { } wait) _meta.BackOff(wait, now);
            }
            SaveMeta();
        }
        finally { _fetching = false; }
        Build();
    }

    // ------------------------------------------------------------------ drawing

    private void Build()
    {
        _generation++;
        _later.Clear();
        Draw();
        if (_later.Count > 0) ScheduleMore();
        else RestoreScroll();
    }

    private void Rebuild()
    {
        _restoreOffset = _scroll.VerticalOffset;
        Build();
    }

    private void RestoreScroll()
    {
        if (_restoreOffset is not { } offset) return;
        _restoreOffset = null;
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(offset);
    }

    private void Emit(Func<FrameworkElement> make)
    {
        if (_later.Count == 0 && _body.Children.Count < FirstBatch) _body.Children.Add(make());
        else _later.Add(make);
    }

    private void ScheduleMore()
    {
        int generation = _generation;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => AddMore(generation));
    }

    private void AddMore(int generation)
    {
        if (generation != _generation || _later.Count == 0) return;
        int take = Math.Min(NextBatch, _later.Count);
        for (int i = 0; i < take; i++) _body.Children.Add(_later[i]());
        _later.RemoveRange(0, take);
        if (_later.Count > 0) ScheduleMore();
        else RestoreScroll();
    }

    private void Flush()
    {
        _generation++;
        foreach (var make in _later) _body.Children.Add(make());
        _later.Clear();
        RestoreScroll();
    }

    private void Draw()
    {
        _seen = Signature();
        _body.Children.Clear();
        _update.Visibility = _file is null && _settings.MarketOnline ? Visibility.Visible : Visibility.Collapsed;
        _update.IsEnabled = !_fetching;
        bool have = _book is { GeneratedAt: not null };
        _tabBar.Visibility = have ? Visibility.Visible : Visibility.Collapsed;
        _controls.Visibility = have && _tab != "Yours" ? Visibility.Visible : Visibility.Collapsed;
        _kindButton.Visibility = _tab == "Selling" ? Visibility.Visible : Visibility.Collapsed;
        _sortButton.Visibility = _tab == "Selling" ? Visibility.Visible : Visibility.Collapsed;

        if (!have)
        {
            _sub.Text = _fetching ? "loading from mnm-db.com…" : "";
            ShowNoBook();
            return;
        }
        var market = Market();
        int priced = market.Items.Count(i => i.Active);
        _sub.Text = $"{market.Server} · {priced:N0} items priced now · "
                    + (_fetching ? "updating…" : _file is null ? $"feed {MarketRefresh.Age(_book!.GeneratedAt!.Value, Now)}" : "from a file");
        _kindButton.Content = Picker(_kind is { } k ? KindName(k) : "All kinds");
        _sortButton.Content = Picker(Sorts.First(s => s.Key == _sort).Label);
        if (_failure.Length > 0) _body.Children.Add(Note("Couldn't update the feed: " + _failure + " The copy kept on this PC is shown.", Palette.Taken));
        if (_book!.Capture is { BlindNowMin: > 60 } blind)
            _body.Children.Add(Note($"The site's reader has read no posts for {blind.BlindNowMin / 60} h: the stream's chat box may have moved or the stream be off. Prices are as of its last reading.", Palette.Muted));
        if (_store.Selected is { } who && !who.ServerName.Equals(market.Server, StringComparison.OrdinalIgnoreCase))
            _body.Children.Add(Note($"These are {market.Server} prices (the server the stream shows). You play on {who.ServerName}, where things may price differently.", Palette.Muted));

        switch (_tab)
        {
            case "Yours": ShowYours(market); break;
            case "Buying": ShowBuying(market); break;
            default: ShowSelling(market); break;
        }
    }

    /// <summary>No feed yet: it is being loaded, or loading failed, or the player switched loading off (the way back is here and in the ⓘ menu).</summary>
    private void ShowNoBook()
    {
        var box = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 24, 8, 8) };
        if (_fetching)
        {
            box.Children.Add(LootPanel.Line("Loading the auction feed from mnm-db.com…", Palette.Text));
        }
        else if (!_settings.MarketOnline)
        {
            box.Children.Add(LootPanel.Line("Loading the market is switched off.", Palette.Text));
            box.Children.Add(LootPanel.Line(
                "Turning it on loads mnm-db.com's public auction feed (what players ask for items on the server the LiveMNM stream shows) and keeps it on this PC, refreshing it once per publish, every six hours. That site sees your internet address, as if you opened it in a browser; nothing from your ledger or your game is sent.", Palette.Muted));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(LootPanel.TextAction("Turn it on", () => { _settings.MarketOnline = true; _settings.Save(); _ = FetchAsync(manual: true); }));
            buttons.Children.Add(LootPanel.TextAction("Open the site instead", () => OpenSite(MarketSite.PageUrl)));
            box.Children.Add(buttons);
        }
        else
        {
            box.Children.Add(LootPanel.Line(_failure.Length > 0 ? "Couldn't load the feed: " + _failure : "The auction feed isn't loaded yet.", _failure.Length > 0 ? Palette.Taken : Palette.Text));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(LootPanel.TextAction("Try again", () => _ = FetchAsync(manual: true)));
            buttons.Children.Add(LootPanel.TextAction("Open the site instead", () => OpenSite(MarketSite.PageUrl)));
            box.Children.Add(buttons);
        }
        _body.Children.Add(box);
    }

    // ------------------------------------------------------------------ Selling

    private bool Matches(MarketItem item, bool sellers) =>
        _query.Trim().Length == 0 || NameSearch.Matches(item.Name, _query) || (sellers && item.Now.Any(l => NameSearch.Matches(l.Player, _query)));

    /// <summary>Every item someone is selling: a standing ask, or the latest ask when none stands now.</summary>
    private static bool Selling(MarketItem i) => i.Asks.Count > 0 || i.LatestAsk is not null;

    private void ShowSelling(MarketSnapshot market)
    {
        bool searching = NameSearch.Words(_query).Length > 0;
        var items = market.Items.Where(Selling).Where(i => _kind is null || i.Kind == _kind).Where(i => Matches(i, true)).ToList();
        items = Sorted(items);
        if (items.Count == 0)
        {
            _body.Children.Add(Note(searching ? $"Nobody is selling anything matching “{_query.Trim()}”." : "Nobody is selling anything" + (_kind is { } k ? " among " + KindName(k).ToLowerInvariant() : "") + " yet.", Palette.Muted));
            if (_kind is not null) _body.Children.Add(Centre(LootPanel.TextAction("Show every kind", () => ChooseKind(null))));
            return;
        }
        int standing = items.Count(i => i.Asks.Count > 0);
        _body.Children.Add(Note($"{items.Count:N0} {(items.Count == 1 ? "item" : "items")} for sale, {standing:N0} with a seller these 3 days" + (items.Count > MaxRows ? $"; the first {MaxRows} are shown. Narrow the search or choose a kind." : ".") + " Click one for its sellers and history.", Palette.Muted));
        double most = Math.Max(1, items.Take(MaxRows).Max(i => i.Sightings));
        int rank = 0;
        foreach (var item in items.Take(MaxRows))
        {
            var it = item;
            int n = ++rank;
            Emit(() => ItemRow(n, it, Math.Sqrt(it.Sightings / most)));
            if (_open.Contains(it.Key)) Emit(() => Details(it));
        }
    }

    private List<MarketItem> Sorted(List<MarketItem> items) => _sort switch
    {
        "Name" => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        "PriceDown" => items.OrderByDescending(i => i.Headline ?? i.LatestAsk?.Copper ?? -1).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        "PriceUp" => items.OrderBy(i => i.Headline ?? i.LatestAsk?.Copper ?? long.MaxValue).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        "Newest" => items.OrderByDescending(i => i.LastSeen).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        _ => items.OrderByDescending(i => i.Asks.Count > 0).ThenByDescending(i => i.Sightings).ThenByDescending(i => i.Sellers + i.Buyers).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
    };

    /// <summary>An item: its name, who is selling and buying, the middle ask (a stack's price for a bulk material). A click opens its breakdown.</summary>
    private FrameworkElement ItemRow(int rank, MarketItem item, double fraction)
    {
        string sub, value;
        Brush brush;
        if (item.Asks.Count > 0)
        {
            sub = Plural(item.Sellers, "seller") + (item.Buyers > 0 ? $" · {Plural(item.Buyers, "buyer")}" : "");
            value = MarketMath.Shown(item.Median!.Value, item.Stack);
            brush = Palette.Gold;
        }
        else
        {
            var last = item.LatestAsk!;
            sub = "last asked " + MarketRefresh.Age(last.Seen, Now);
            value = MarketMath.Shown(last.Copper, item.Stack);
            brush = Palette.Muted;
        }
        var tip = item.Card?.Summary() ?? "";
        if (item.Stack is { } stack) tip = (tip.Length > 0 ? tip + "\n" : "") + $"Sold by the stack of {stack}: the price is a stack's.";
        return LootPanel.Row(rank, item.Name, sub, value, brush, fraction, tip, () =>
        {
            if (!_open.Remove(item.Key)) _open.Add(item.Key);
            Rebuild();
        });
    }

    /// <summary>What a click on an item opens: the wiki's line about it, the headline prices, who asks and bids, the days and the changes.</summary>
    private FrameworkElement Details(MarketItem item)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        var border = new Border { CornerRadius = new CornerRadius(7), Background = (Brush)Ui.Theme["Subtle"], Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 4), Child = box };
        var now = Now;

        if (item.Card?.Summary() is { Length: > 0 } summary) box.Children.Add(new TextBlock { Text = summary, FontSize = 11, Foreground = RecipeColors.Label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-3, 0, -3, 4) };
        string Price(long? c) => c is { } v ? MarketMath.Shown(v, item.Stack) : "–";
        var low = item.Asks.FirstOrDefault();
        var high = item.Asks.LastOrDefault();
        var bid = item.Bids.FirstOrDefault();
        tiles.Children.Add(Tile("Lowest ask", Price(item.Low), low is null ? "nobody asking" : $"{low.Player} · {MarketRefresh.Age(low.Seen, now)}", Palette.Gold));
        tiles.Children.Add(Tile("Middle ask", Price(item.Median), item.Asks.Count > 0 ? Plural(item.Sellers, "seller") + " these 3 days" : "no standing ask", Palette.Text));
        tiles.Children.Add(Tile("Highest ask", Price(item.High), high is null ? "" : $"{high.Player} · {MarketRefresh.Age(high.Seen, now)}", Palette.Text));
        tiles.Children.Add(Tile("Best bid", Price(item.BestBid), bid is null ? "nobody bidding" : $"{bid.Player} · {MarketRefresh.Age(bid.Seen, now)}", bid is null ? Palette.Muted : Palette.Heal));
        if (item.Low is { } lo && item.High is { } hi && item.Asks.Count > 1)
            tiles.Children.Add(Tile("Spread", Price(hi - lo), lo > 0 ? $"{(hi - lo) / (double)lo * 100:0}% above the lowest" : "", Palette.Text));
        else tiles.Children.Add(Tile("Spread", "–", "one ask or none", Palette.Muted));
        var trend = MarketMath.Trend(item, now, TimeSpan.FromDays(7));
        if (trend is { } t && t.Since < now - TimeSpan.FromHours(12))
            tiles.Children.Add(Tile("7-day trend", $"{t.Fraction * 100:+0;-0;0}%", $"from {Price(t.Then)}, {MarketRefresh.Age(t.Since, now)}", t.Fraction < -0.005 ? Palette.Heal : t.Fraction > 0.005 ? Palette.Taken : Palette.Text));
        else tiles.Children.Add(Tile("7-day trend", "–", item.Versions.Count > 0 ? $"tracked since {item.FirstSeen.ToLocalTime():d MMM}" : "", Palette.Muted));
        box.Children.Add(tiles);

        if (item.Asks.Count > 0)
        {
            box.Children.Add(Heading("Asking"));
            foreach (var a in item.Asks.Take(8)) box.Children.Add(PersonLine(a, now));
            if (item.Asks.Count > 8) box.Children.Add(Small($"and {item.Asks.Count - 8} more"));
        }
        if (item.Bids.Count > 0)
        {
            box.Children.Add(Heading("Bidding"));
            foreach (var b in item.Bids.Take(5)) box.Children.Add(PersonLine(b, now));
        }
        var days = MarketMath.Days(item, DateOnly.FromDateTime(now));
        if (days.Count > 1)
        {
            box.Children.Add(Heading("By day"));
            foreach (var d in days.AsEnumerable().Reverse().Take(10))
            {
                var line = new TextBlock { FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                line.Inlines.Add(new Run(d.Day.ToString("ddd d MMM") + "  ") { Foreground = Palette.Muted });
                if (d.Median is { } m)
                {
                    line.Inlines.Add(new Run(Price(m)) { Foreground = Palette.Text, FontWeight = FontWeights.SemiBold });
                    if (d.Low != d.High) line.Inlines.Add(new Run($"  ({Price(d.Low)} – {Price(d.High)})") { Foreground = Palette.Muted });
                    line.Inlines.Add(new Run($"  · {Plural(d.Sellers, "seller")}") { Foreground = Palette.Muted });
                }
                if (d.BestBid is { } bb) line.Inlines.Add(new Run($"  · bid {Price(bb)}") { Foreground = Palette.Heal });
                box.Children.Add(line);
            }
        }
        var changes = MarketMath.Changes(item).Where(c => c.Previous is not null).Take(8).ToList();
        if (changes.Count > 0)
        {
            box.Children.Add(Heading("Price changes"));
            foreach (var c in changes)
            {
                var line = new TextBlock { FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                bool up = c.Copper > c.Previous!.Value;
                line.Inlines.Add(new Run(c.Who + "  ") { Foreground = Palette.Text });
                line.Inlines.Add(new Run(Price(c.Previous) + " → ") { Foreground = Palette.Muted });
                line.Inlines.Add(new Run(Price(c.Copper)) { Foreground = up ? Palette.Taken : Palette.Heal, FontWeight = FontWeights.SemiBold });
                line.Inlines.Add(new Run($"  {MarketRefresh.Age(c.Time, now)}" + (c.Intent == MarketIntent.Buy ? " · bid" : "")) { Foreground = Palette.Muted });
                box.Children.Add(line);
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-3, 8, 0, 0) };
        var copy = SmallButton("Copy price check", "Copy the item's prices as a line of text, to paste into chat");
        copy.Click += (_, e) => { e.Handled = true; _status.Text = ClipboardText.Copy(PriceCheck(item)) ? $"Copied the price check for {item.Name}." : "Couldn't use the clipboard just now."; };
        buttons.Children.Add(copy);
        var open = SmallButton("Open on mnm-db.com", "The site's own page for this item's listings, in your browser");
        open.Click += (_, e) => { e.Handled = true; OpenSite(MarketSite.ItemUrl(item.Name)); };
        buttons.Children.Add(open);
        box.Children.Add(buttons);
        return border;
    }

    /// <summary>"Copper Ore: 3 selling 4sp–6sp a stack (middle 5sp), best bid 3sp. Trem, mnm-db.com, 2 h ago."</summary>
    private string PriceCheck(MarketItem item)
    {
        string Price(long c) => MarketMath.Shown(c, item.Stack).Replace("/stack", " a stack");
        var parts = new List<string>();
        if (item.Asks.Count > 0)
            parts.Add(Plural(item.Sellers, "seller") + " at " + (item.Low == item.High ? Price(item.Low!.Value) : $"{Price(item.Low!.Value)} to {Price(item.High!.Value)}, middle {Price(item.Median!.Value)}"));
        if (item.BestBid is { } bb) parts.Add("best bid " + Price(bb));
        if (parts.Count == 0 && item.LatestAsk is { } last) parts.Add("last asked " + Price(last.Copper) + " " + MarketRefresh.Age(last.Seen, Now));
        return $"{item.Name}: {string.Join(", ", parts)}. {item.Server} auctions via mnm-db.com, feed {MarketRefresh.Age(_book!.GeneratedAt!.Value, Now)}.";
    }

    private FrameworkElement PersonLine(MarketListing l, DateTime now)
    {
        var line = new TextBlock { FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        line.Inlines.Add(new Run(l.Player + "  ") { Foreground = Palette.Text });
        line.Inlines.Add(new Run(MarketMath.Shown(l.Copper, l.Stack)) { Foreground = l.Intent == MarketIntent.Buy ? Palette.Heal : Palette.Gold, FontWeight = FontWeights.SemiBold });
        if (l.Quantity is { } q) line.Inlines.Add(new Run($" each, ×{q}") { Foreground = Palette.Muted });
        if (l.Assumed) line.Inlines.Add(new Run(" (no WTS said)") { Foreground = Palette.Muted });
        line.Inlines.Add(new Run("  " + MarketRefresh.Age(l.Seen, now) + (l.Count > 1 ? $" · read {l.Count}×" : "")) { Foreground = Palette.Muted });
        return line;
    }

    // ------------------------------------------------------------------ Yours

    /// <summary>What the loot you took (this session, or the day) would fetch at the middle ask, item by item, and the items the market has no price for.</summary>
    private void ShowYours(MarketSnapshot market)
    {
        if (_store.Selected is null)
        {
            _body.Children.Add(Note("No Monsters and Memories ledger found, so there is no loot to price.", Palette.Muted));
            return;
        }
        var events = _store.Events(_session.Day, _session.ActiveSession()).Where(e => e.Kind == LootKind.Item && e.You && e.Item.Length > 0).ToList();
        var scope = $"{SessionHeader.DayTitle(_session.Day)} · {_session.ScopeLabel()}";
        if (events.Count == 0)
        {
            _body.Children.Add(Note($"{scope}: you looted no items.", Palette.Muted));
            return;
        }
        var counted = events.GroupBy(e => e.Item, StringComparer.OrdinalIgnoreCase).Select(g => (Name: g.Key, Count: g.Sum(e => Math.Max(1, e.Count)), Item: market.Named(g.Key))).ToList();
        var priced = counted.Where(c => c.Item is { Headline: not null }).Select(c => (c.Name, c.Count, Item: c.Item!, Each: c.Item!.Headline!.Value, Worth: c.Item!.Headline!.Value * c.Count)).OrderByDescending(c => c.Worth).ThenBy(c => c.Name).ToList();
        var unpriced = counted.Where(c => c.Item is not { Headline: not null }).OrderByDescending(c => c.Count).ThenBy(c => c.Name).ToList();
        long total = priced.Sum(p => p.Worth);

        var head = new DockPanel { Margin = new Thickness(2, 2, 2, 4) };
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(right, Dock.Right);
        right.Children.Add(new TextBlock { Text = MarketMath.Coin(total), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Palette.Gold, HorizontalAlignment = HorizontalAlignment.Right, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
        right.Children.Add(new TextBlock { Text = $"at {market.Server}'s middle asks", FontSize = 10, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right });
        head.Children.Add(right);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = "Your loot, if sold to players", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text });
        left.Children.Add(new TextBlock { Text = $"{scope} · {priced.Count} of {counted.Count} items have a price", FontSize = 11, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis });
        head.Children.Add(left);
        _body.Children.Add(head);

        if (priced.Count > 0)
        {
            double most = Math.Max(1, priced[0].Worth);
            int rank = 0;
            foreach (var p in priced)
            {
                var row = p;
                int n = ++rank;
                Emit(() => LootPanel.Row(n, row.Name, $"×{row.Count} · {MarketMath.Shown(row.Each, row.Item.Stack)}" + (row.Item.Stack is not null ? $" ({MarketMath.Coin(row.Each)} each)" : ""), MarketMath.Coin(row.Worth), Palette.Gold, row.Worth / most,
                    row.Item.Asks.Count > 0 ? $"{Plural(row.Item.Sellers, "seller")} asking; the middle ask is used." : "No seller these 3 days: the best bid is used.", () =>
                    {
                        if (!_open.Remove(row.Item.Key)) _open.Add(row.Item.Key);
                        Rebuild();
                    }));
                if (_open.Contains(row.Item.Key)) Emit(() => Details(row.Item));
            }
        }
        if (unpriced.Count > 0)
            Emit(() => Note("No price on the market yet: " + string.Join(", ", unpriced.Select(u => u.Count > 1 ? $"{u.Name} ×{u.Count}" : u.Name)) + ".", Palette.Muted));
        Emit(() => Note("Worth is the middle standing ask times how many you looted; a stack's worth counts each piece at the stack price over the stack size. Posts are asks, not sales.", Palette.Muted));
    }

    // ------------------------------------------------------------------ Buying

    /// <summary>What players are bidding for (ranked by how many want it) and the gear they ask after.</summary>
    private void ShowBuying(MarketSnapshot market)
    {
        var wanted = market.Items.Where(i => i.Bids.Count > 0).Where(i => Matches(i, true)).OrderByDescending(i => i.Buyers).ThenByDescending(i => i.BestBid).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        bool searching = NameSearch.Words(_query).Length > 0;
        if (wanted.Count == 0) _body.Children.Add(Note(searching ? $"Nobody is bidding for anything matching “{_query.Trim()}”." : "Nobody is bidding for anything just now.", Palette.Muted));
        else
        {
            _body.Children.Add(Note($"{Plural(wanted.Count, "item")} with a standing bid. A row's price is the best bid; click it for who is bidding and what sellers ask.", Palette.Muted));
            double most = Math.Max(1, wanted.Take(MaxRows).Max(i => i.Buyers));
            int rank = 0;
            foreach (var item in wanted.Take(MaxRows))
            {
                var it = item;
                int n = ++rank;
                Emit(() => LootPanel.Row(n, it.Name, Plural(it.Buyers, "buyer") + (it.Sellers > 0 ? $" · {Plural(it.Sellers, "seller")}" : " · no seller"), MarketMath.Shown(it.BestBid!.Value, it.Stack), Palette.Heal, it.Buyers / most,
                    it.Card?.Summary() ?? "", () => { if (!_open.Remove(it.Key)) _open.Add(it.Key); Rebuild(); }));
                if (_open.Contains(it.Key)) Emit(() => Details(it));
            }
        }

        // Gear requests: "+3 Enchants |Head| Int/Wis", grouped by what is asked, ranked by how many ask
        var week = Now - TimeSpan.FromDays(7);
        var requests = (_book?.Requests ?? []).Where(r => r.Server.Equals(market.Server, StringComparison.OrdinalIgnoreCase) && r.LastSeen >= week)
            .Where(r => !searching || NameSearch.Matches(RequestLabel(r), _query) || NameSearch.Matches(r.Text, _query)).ToList();
        if (requests.Count == 0) return;
        var groups = requests.GroupBy(r => RequestLabel(r), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Label: g.Key, Players: g.Select(r => r.Player).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Latest: g.Max(r => r.LastSeen), Text: g.OrderByDescending(r => r.LastSeen).First().Text))
            .OrderByDescending(g => g.Players).ThenByDescending(g => g.Latest).Take(30).ToList();
        Emit(() => Heading("Gear asked after · last 7 days"));
        foreach (var g in groups)
        {
            var group = g;
            Emit(() =>
            {
                var line = new TextBlock { FontSize = 11.5, Margin = new Thickness(2, 2, 2, 2), TextWrapping = TextWrapping.Wrap, ToolTip = group.Text };
                line.Inlines.Add(new Run(group.Label) { Foreground = Palette.Text, FontWeight = FontWeights.SemiBold });
                line.Inlines.Add(new Run($"  {Plural(group.Players, "player")} asking · {MarketRefresh.Age(group.Latest, Now)}") { Foreground = Palette.Muted });
                return line;
            });
        }
    }

    private static string RequestLabel(FeedRequest r)
    {
        var parts = new List<string>();
        if (r.Plus.Length > 0) parts.Add("+" + string.Join("/", r.Plus));
        if (r.Stats.Length > 0) parts.Add(string.Join("/", r.Stats.Select(s => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s)));
        if (r.Category is { Length: > 0 } c) parts.Add(c);
        var label = string.Join(" ", parts);
        return label.Length > 0 ? label : r.Text.Trim();
    }

    // ------------------------------------------------------------------ pieces

    private static TextBlock Note(string text, Brush brush) =>
        new() { Text = text, Foreground = brush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 4) };

    private static TextBlock Small(string text) => new() { Text = text, Foreground = Palette.Muted, FontSize = 10.5, Margin = new Thickness(0, 1, 0, 0) };

    private static TextBlock Heading(string text) =>
        new() { Text = text.ToUpperInvariant(), Foreground = RecipeColors.Label, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };

    private static FrameworkElement Centre(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        return element;
    }

    private static string Plural(int n, string word) => LootPanel.Plural(n, word);

    private static Button SmallButton(string label, string tip) =>
        new() { Content = label, Style = (Style)Ui.Theme["TextButton"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(9, 3, 9, 3), FontSize = 11, Focusable = false, ToolTip = tip };

    /// <summary>A headline figure: a small label, the figure, a word on where it came from.</summary>
    private static FrameworkElement Tile(string label, string value, string caption, Brush brush)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 9.5, Foreground = Palette.Muted });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = brush, TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = caption, FontSize = 9.5, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border { Padding = new Thickness(3, 2, 3, 4), Child = stack };
    }

    private static FrameworkElement Picker(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = "▾", FontSize = 10, Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Muted });
        return row;
    }

    private static string KindName(MarketKind kind) => kind.ToString();

    // ------------------------------------------------------------------ choosing

    private void Typed()
    {
        if (_search.Text == _query) return;
        _query = _search.Text;
        _scroll.ScrollToTop();
        Build();
    }

    private void ChooseKind(MarketKind? kind)
    {
        _kind = kind;
        _settings.MarketKind = kind?.ToString();
        _settings.Save();
        _scroll.ScrollToTop();
        Build();
    }

    private void OpenKindMenu()
    {
        var market = Market();
        var menu = new ContextMenu { PlacementTarget = _kindButton, Placement = PlacementMode.Bottom };
        var listed = market.Items.Where(Selling).ToList();
        var all = new MenuItem { Header = $"All kinds ({listed.Count:N0})", IsCheckable = true, IsChecked = _kind is null };
        all.Click += (_, _) => ChooseKind(null);
        menu.Items.Add(all);
        menu.Items.Add(new Separator());
        foreach (var kind in Enum.GetValues<MarketKind>())
        {
            var k = kind;
            int n = listed.Count(i => i.Kind == k);
            var item = new MenuItem { Header = $"{KindName(k)} ({n:N0})", IsCheckable = true, IsChecked = _kind == k, IsEnabled = n > 0 };
            item.Click += (_, _) => ChooseKind(k);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OpenSortMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _sortButton, Placement = PlacementMode.Bottom };
        foreach (var (key, label) in Sorts)
        {
            var k = key;
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _sort == k };
            item.Click += (_, _) => { _sort = k; _settings.MarketSort = k; _settings.Save(); _scroll.ScrollToTop(); Build(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private static void OpenSite(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
