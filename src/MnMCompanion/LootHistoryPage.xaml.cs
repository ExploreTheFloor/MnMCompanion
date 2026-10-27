using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MnMCompanion.Loot;

namespace MnMCompanion;

public sealed record LootDayItem(DateOnly Day, string Title, string Sub, string Coin, string Month, string Search);

public sealed record LootMemberItem(string? Key, string Name, string Value, string Sub, Brush Brush, GridLength Fill, GridLength Rest);

/// <summary>
/// The Loot page of the History window: the days with loot on the left, the day's group in the middle
/// (most valuable first, with a bar), and on the right the totals for everyone or, after a click on a name, that player's
/// items, the mobs they looted and every line. Reads the <see cref="LootStore"/>, which fills as the game writes its ledger;
/// the window looks again every couple of seconds.
/// </summary>
public partial class LootHistoryPage : UserControl
{
    private const int LogRows = 400;

    private readonly LootStore _store;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<(DateOnly, DateTime?), (int Count, ItemValues Values, LootDay Day)> _days = [];

    private List<LootDayItem> _items = [];
    private string _signature = "";
    private DateOnly? _day;
    private DateTime? _scopeStart;   // the session shown, by its start; null = the whole day
    private LootDay? _shown;
    private int _shownRaw = -1;
    private string? _member;   // null = everyone
    private string? _pendingMember;   // a name asked for from the small window, kept until the ledger has been read and it can be found
    private string _tab = "";
    private bool _updatingLists;

    internal LootHistoryPage(LootStore store)
    {
        Resources.MergedDictionaries.Add(Ui.Theme);
        InitializeComponent();
        _store = store;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            RefreshDays();
            if (_day is null && _items.Count > 0) DayList.SelectedIndex = 0;
            _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ---- Days ---------------------------------------------------------------------------------------------------

    private LootDay Load(DateOnly day, LootSession? session = null)
    {
        var events = _store.Events(day, session);
        var values = _store.Values;
        var key = (day, session?.Start);
        if (_days.TryGetValue(key, out var had) && had.Count == events.Count && ReferenceEquals(had.Values, values)) return had.Day;
        var built = LootDay.Of(day, events, values);
        _days[key] = (events.Count, values, built);
        return built;
    }

    private LootSession? ScopeSession() => _scopeStart is { } s ? _store.Sessions.All.FirstOrDefault(x => x.Start == s) : null;

    public void Tick()
    {
        RefreshDays();
        // The day on screen is still filling: keep it current.
        if (_day is { } day && _shown is not null && _store.Events(day).Count != _shownRaw) RenderDay();
    }

    private void RefreshDays()
    {
        var days = _store.Days();
        var sig = string.Join(',', days.Select(d => d.DayNumber)) + "|" + _store.Version + "|" + _store.SelectionKey + "|" + Today.DayNumber;
        if (sig == _signature) return;
        _signature = sig;
        _days.Clear();

        _items = days.Select(day =>
        {
            var d = Load(day);
            return new LootDayItem(day, DayName(day), $"{Plural(d.Totals.Mobs, "mob")} · {Plural(d.Totals.Items, "item")}",
                LootSummary.Worth(d.Total, d.Approximate, d.Unpriced > 0), day.ToString("MMMM yyyy", CultureInfo.CurrentCulture), d.SearchText);
        }).ToList();

        // The day on show has no loot (today was asked for before the ledger was read, or the character changed): show the newest day that has some
        if (_items.Count > 0 && (_day is not { } shown || _items.All(i => i.Day != shown)))
        {
            _day = _items[0].Day;
            _scopeStart = null;
            _member = null;
            _pendingMember = null;
            _tab = "";
        }

        _updatingLists = true;
        var view = new ListCollectionView(_items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LootDayItem.Month)));
        view.Filter = o => Matches((LootDayItem)o);
        DayList.ItemsSource = view;
        if (_day is { } keep) DayList.SelectedItem = _items.FirstOrDefault(i => i.Day == keep);
        _updatingLists = false;
        DayCount.Text = _items.Count == 0 ? "No loot in the ledger yet" : Plural(_items.Count, "day") + " with loot";
        if (_items.Count == 0) ShowNothing();
        else if (_day is not null) RenderDay();
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n:N0} {word}s";

    /// <summary>"Today", "Yesterday", "Tue, Oct 20" (with the year in another year): short enough for the day list.</summary>
    private static string DayName(DateOnly day) =>
        day == Today ? "Today" : day == Today.AddDays(-1) ? "Yesterday" : day.ToString(day.Year == Today.Year ? "ddd, MMM d" : "MMM d, yyyy", CultureInfo.CurrentCulture);

    private bool Matches(LootDayItem i)
    {
        var q = SearchBox.Text.Trim();
        return q.Length == 0 || i.Search.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        (DayList.ItemsSource as ICollectionView)?.Refresh();
    }

    private void OnDaySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLists || DayList.SelectedItem is not LootDayItem item) return;
        _day = item.Day;
        _member = null;
        _pendingMember = null;
        _scopeStart = null;
        _tab = "";
        RenderDay();
        DetailScroll.ScrollToTop();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_store.LedgerPath is { } path && Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { }
    }

    /// <summary>Shows a day (from the panel), optionally one session of it and one player's breakdown.</summary>
    public void ShowDay(DateOnly day, string? member = null, LootSession? session = null)
    {
        _signature = "";
        RefreshDays();
        // Nothing looted on that day (today, early on): show the newest day that has loot rather than an empty page
        if (_items.Count > 0 && _items.All(i => i.Day != day))
        {
            day = _items[0].Day;
            session = null;
        }
        _scopeStart = session?.Start;
        _updatingLists = true;
        DayList.SelectedItem = _items.FirstOrDefault(i => i.Day == day);
        DayList.ScrollIntoView(DayList.SelectedItem ?? day);
        _updatingLists = false;
        _day = day;
        _member = member;
        _pendingMember = member;
        _tab = "";
        RenderDay();
        DetailScroll.ScrollToTop();
    }

    /// <summary>Opens one monster's drop table on the Drop rates tab (for a picture).</summary>
    public void PickDrop(string mob)
    {
        PickTab("Drop rates");
        DropRatesFor().Open(mob);
    }

    /// <summary>Opens a tab of the detail pane by name ("Looters", "Items", "Mobs", "Loot log").</summary>
    public void PickTab(string tab)
    {
        _tab = tab;
        RenderDetail();
    }

    // ---- The day and its group ----------------------------------------------------------------------------------

    private void RenderDay()
    {
        if (_day is not { } day) return;
        var session = ScopeSession();
        var d = _shown = Load(day, session);
        _shownRaw = _store.Events(day).Count;
        DayTitle.Text = DayName(day) + (session is null ? "" : " · session");
        DayWhen.Text = d.Events.Count == 0 ? "Nothing looted" : $"{d.First:t} to {d.Last:t} · {Plural(d.Members.Count, "looter")}";
        BuildScopeBar(day, session);

        _updatingLists = true;
        long top = Math.Max(1, d.Members.Select(m => m.Copper).DefaultIfEmpty(0).Max());
        var rows = new List<LootMemberItem>
        {
            new(null, "Everyone", LootSummary.Coin(d.Totals.Copper), $"{Plural(d.Totals.Mobs, "mob")} · {Plural(d.Totals.Items, "item")}", Palette.Muted,
                new GridLength(1, GridUnitType.Star), new GridLength(0.0001, GridUnitType.Star)),
        };
        foreach (var m in d.Members)
        {
            double frac = (double)m.Copper / top;
            rows.Add(new LootMemberItem(m.Name, m.Class.Length > 0 ? $"{m.Name} · {m.Class}" : m.Name, LootSummary.Coin(m.Copper),
                $"{Plural(m.Mobs, "mob")} · {Plural(m.Items, "item")}" + (m.Level > 0 ? $" · level {m.Level}" : ""), BrushOf(m),
                new GridLength(Math.Max(frac, 0.0001), GridUnitType.Star), new GridLength(Math.Max(1 - frac, 0.0001), GridUnitType.Star)));
        }
        MemberList.ItemsSource = rows;
        if (_pendingMember is not null)
        {
            // Opened from the small window on a name: this window may not have read the ledger yet (an empty day has no such name), so keep
            // asking until it can be found, and give up only once the day has loot and the name still isn't in it
            if (d.Member(_pendingMember) is not null) { _member = _pendingMember; _pendingMember = null; }
            else if (d.Events.Count > 0) { _member = null; _pendingMember = null; }
            else _member = null;
        }
        else if (_member != null && d.Member(_member) is null) _member = null;
        MemberList.SelectedItem = rows.FirstOrDefault(r => string.Equals(r.Key, _member, StringComparison.OrdinalIgnoreCase));
        _updatingLists = false;
        RenderDetail();
    }

    /// <summary>
    /// The day's sessions (the stretches between "new session" markers) to pick from, beside the whole day. Hidden until a
    /// marker has been made.
    /// </summary>
    private void BuildScopeBar(DateOnly day, LootSession? active)
    {
        ScopeBar.Children.Clear();
        var sessions = _store.Sessions.All;
        ScopeHost.Visibility = sessions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (sessions.Count <= 1) return;
        void Add(string text, LootSession? s)
        {
            var rb = new RadioButton
            {
                Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis }, Style = (Style)FindResource("Segment"),
                GroupName = "LootScope", IsChecked = (s?.Start) == active?.Start, Margin = new Thickness(0, 0, 0, 1),
            };
            rb.Checked += (_, _) =>
            {
                if (_updatingLists) return;
                _scopeStart = s?.Start;
                _member = null;
                _pendingMember = null;
                RenderDay();
            };
            ScopeBar.Children.Add(rb);
        }
        Add("Whole day", null);
        var dayEvents = _store.Events(day);
        foreach (var s in sessions.Reverse())
        {
            var inside = dayEvents.Where(e => s.Contains(e.Time)).ToList();
            if (inside.Count == 0) continue;
            Add((s.Open ? "This session · " : "") + s.Label(inside.Min(e => e.Time)) + $"  ·  {LootSummary.Totals(inside).Mobs} mobs", s);
        }
    }

    private static Brush BrushOf(LootMember m) => Palette.For(m.Name, m.Class.Length > 0 ? m.Class : null);

    private void OnMemberSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLists || MemberList.SelectedItem is not LootMemberItem m) return;
        _member = m.Key;
        _pendingMember = null;
        _tab = "";
        RenderDetail();
    }

    private void SelectMember(string? name)
    {
        _member = name;
        _pendingMember = null;
        _tab = "";
        _updatingLists = true;
        MemberList.SelectedItem = (MemberList.ItemsSource as IEnumerable<LootMemberItem>)?.FirstOrDefault(r => string.Equals(r.Key, name, StringComparison.OrdinalIgnoreCase));
        _updatingLists = false;
        RenderDetail();
        DetailScroll.ScrollToTop();
    }

    private void ShowNothing()
    {
        DayTitle.Text = "No loot yet";
        DayWhen.Text = "";
        MemberList.ItemsSource = null;
        HeadName.Text = "Nothing looted yet";
        HeadSub.Text = "Loot shows up here as the game writes it to its ledger.";
        HeadBig.Text = "";
        HeadBigCaption.Text = "";
        HeadSwatch.Background = Palette.Muted;
        Tiles.Children.Clear();
        TabBar.Children.Clear();
        TabContent.Content = null;
    }

    // ---- Breakdown ----------------------------------------------------------------------------------------------

    private void RenderDetail()
    {
        if (_shown is not { } d) return;
        if (d.Events.Count == 0) { ShowNothing(); return; }
        if (d.Member(_member) is { } m) RenderMember(d, m);
        else RenderEveryone(d);
    }

    private void RenderEveryone(LootDay d)
    {
        var t = d.Totals;
        HeadSwatch.Background = Palette.Muted;
        HeadName.Text = "Everyone · " + DayName(d.Day);
        HeadSub.Text = $"{d.First:t} to {d.Last:t} · {Plural(d.Members.Count, "looter")}";
        HeadBig.Text = LootSummary.Worth(d.Total, d.Approximate, d.Unpriced > 0);
        HeadBigCaption.Text = "coin + items";

        Tiles.Children.Clear();
        AddTile("Mobs looted", $"{t.Mobs:N0}", $"{d.Mobs.Count:N0} kinds");
        AddTile("Coin", LootSummary.Coin(t.Copper), t.Mobs > 0 ? $"{LootSummary.Coin(t.Copper / t.Mobs)} a mob" : "in purses");
        AddTile("Items worth", LootSummary.Worth(d.ItemValue, d.Approximate, d.Unpriced > 0), ItemsNote(d.Priced, d.Estimated, d.Unpriced));
        AddTile("Items", $"{t.Items:N0}", $"{d.Items.Count:N0} kinds");
        AddTile("Looters", $"{d.Members.Count:N0}", d.Members.Count > 0 ? $"most: {d.Members[0].Name}" : "");
        if (d.Members.MaxBy(m => m.Mobs) is { Mobs: > 0 } busiest) AddTile("Most mobs", $"{busiest.Mobs:N0}", busiest.Name);
        if (d.ItemsWorth.FirstOrDefault(i => i.Value.Known) is { } best) AddTile("Best item", best.Item, $"{(best.Value.Estimated ? "~" : "")}{LootSummary.Coin(best.Value.Each)} each · {best.Count:N0} taken");
        if (t.Share > 0) AddTile("Your splits", LootSummary.Coin(t.Share), "your share of the purses", Palette.Heal);

        Tabs(["Looters", "Items", "Mobs", "Drop rates", "Loot log"], tab => tab switch
        {
            "Looters" => LootersTable(d),
            "Items" => ItemsTable(d),
            "Mobs" => MobsTable(d),
            "Drop rates" => DropRatesFor(),
            _ => Log(d.Events),
        }, tab => tab == "Items"
            ? [("Copy summary", () => LootSummary.Breakdown(d.Day, d.Events, values: _store.Values)), ("Copy items", () => LootSummary.ItemsText(d.Day, NameSearch.Filter(d.ItemsWorth, i => i.Item, _itemSearch), ScopeText()))]
            : [("Copy summary", () => LootSummary.Breakdown(d.Day, d.Events, values: _store.Values))]);
    }

    private void RenderMember(LootDay day, LootMember m)
    {
        var t = day.Totals;
        var brush = BrushOf(m);
        HeadSwatch.Background = brush;
        HeadName.Text = m.Name;
        HeadSub.Text = (m.ClassLevel.Length > 0 ? m.ClassLevel + " · " : "") + $"looted {m.First:t} to {m.Last:t}";
        HeadBig.Text = LootSummary.Worth(m.Total, m.Estimated > 0, m.Unpriced > 0);
        HeadBigCaption.Text = "coin + items";

        string Of(double part, double whole) => whole > 0 ? $"{Fmt.Pct(part / whole)} of the group's" : "—";
        Tiles.Children.Clear();
        AddTile("Mobs looted", $"{m.Mobs:N0}", Of(m.Mobs, t.Mobs), brush);
        AddTile("Coin", LootSummary.Coin(m.Copper), Of(m.Copper, t.Copper));
        AddTile("Items worth", LootSummary.Worth(m.ItemValue, m.Estimated > 0, m.Unpriced > 0), ItemsNote(m.Priced, m.Estimated, m.Unpriced));
        AddTile("Items", $"{m.Items:N0}", Of(m.Items, t.Items));
        AddTile("Coin per mob", m.Mobs > 0 ? LootSummary.Coin((long)m.CopperPerMob) : "—", m.Mobs > 0 && t.Mobs > 0 ? $"group {LootSummary.Coin(t.Copper / t.Mobs)}" : "");
        AddTile("Items per mob", m.Mobs > 0 ? m.ItemsPerMob.ToString("0.0", CultureInfo.CurrentCulture) : "—", t.Mobs > 0 ? $"group {(double)t.Items / t.Mobs:0.0}" : "");
        if (m.Taken.OrderByDescending(x => x.Total).FirstOrDefault(x => x.Value.Known) is { } best) AddTile("Best item", best.Item, $"{(best.Value.Estimated ? "~" : "")}{LootSummary.Coin(best.Value.Each)} each · {best.Count:N0} taken");
        if (m.Split > 0) AddTile("Your splits", LootSummary.Coin(m.Split), "from their purses", Palette.Heal);

        Tabs(["Items", "Mobs", "Loot log"], tab => tab switch
        {
            "Items" => MemberItemsTable(m),
            "Mobs" => MemberMobsTable(m),
            _ => Log(day.Events.Where(e => e.Who.Equals(m.Name, StringComparison.OrdinalIgnoreCase))),
        }, _ => [("Copy summary", () => LootSummary.MemberText(m))]);
    }

    // ---- Drop rates ---------------------------------------------------------------------------------------------

    private (long Version, DropRatesView View)? _drops;

    /// <summary>
    /// How often each item comes off each kind of monster, over every day of this character's ledger (a bigger sample than one day).
    /// Worked out once per change of the ledger and kept, with the page the reader was on.
    /// </summary>
    private DropRatesView DropRatesFor()
    {
        if (_drops is not { } cached || cached.Version != _store.Version)
        {
            var all = _store.Days().SelectMany(day => _store.Events(day)).ToList();
            _drops = (_store.Version, new DropRatesView(DropRates.ByMob(all), _store.Values));
        }
        return _drops.Value.View;
    }

    // ---- Pieces -------------------------------------------------------------------------------------------------

    /// <summary>"8 priced · 3 estimated · 2 unpriced": how the value of the items is known.</summary>
    private static string ItemsNote(int priced, int estimated, int unpriced)
    {
        var parts = new[] { priced > 0 ? $"{priced:N0} priced" : "", estimated > 0 ? $"{estimated:N0} estimated" : "", unpriced > 0 ? $"{unpriced:N0} unpriced" : "" }.Where(x => x.Length > 0).ToList();
        return parts.Count == 0 ? "no items" : string.Join(" · ", parts);
    }

    private void AddTile(string label, string value, string sub, Brush? accent = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Palette.Muted });
        stack.Children.Add(new TextBlock
        {
            Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = accent ?? Palette.Text, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), ToolTip = value,
        });
        stack.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = sub });
        Tiles.Children.Add(new Border { Style = (Style)FindResource("Tile"), Child = stack });
    }

    private void Tabs(IReadOnlyList<string> tabs, Func<string, FrameworkElement> content, Func<string, (string Label, Func<string> Text)[]>? copies = null)
    {
        if (!tabs.Contains(_tab)) _tab = tabs[0];
        void FillCopies(string tab)
        {
            CopyBar.Children.Clear();
            foreach (var (label, text) in copies?.Invoke(tab) ?? [])
            {
                var b = new Button { Content = label, Style = (Style)FindResource("TextButton"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), ToolTip = "Copy to the clipboard as text" };
                var shown = label;
                b.Click += (_, _) =>
                {
                    b.Content = ClipboardText.Copy(text()) ? "Copied" : "Clipboard busy";
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                    t.Tick += (_, _) => { t.Stop(); b.Content = shown; };
                    t.Start();
                };
                CopyBar.Children.Add(b);
            }
        }
        TabBar.Children.Clear();
        foreach (var t in tabs)
        {
            var rb = new RadioButton
            {
                Content = new TextBlock { Text = t, Margin = new Thickness(14, 0, 14, 0) },
                Style = (Style)FindResource("Segment"),
                GroupName = "LootTab",
                IsChecked = t == _tab,
            };
            rb.Checked += (_, _) =>
            {
                _tab = t;
                TabContent.Content = content(t);
                FillCopies(t);
            };
            TabBar.Children.Add(rb);
        }
        TabContent.Content = content(_tab);
        FillCopies(_tab);
    }

    /// <summary>The part of the day being shown ("Whole day", "This session ..."), for the heading of copied text.</summary>
    private string ScopeText() => ScopeSession() is { } s ? s.Label(null) : "";

    /// <summary>After a table is re-sorted: redraw, staying where the reader was.</summary>
    private void Rebuild()
    {
        var offset = DetailScroll.VerticalOffset;
        RenderDetail();
        Dispatcher.BeginInvoke(() => DetailScroll.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
    }

    private FrameworkElement LootersTable(LootDay d)
    {
        double total = Math.Max(1, d.Total);
        return Table.Build("loot-members", d.Members, new Col<LootMember>[]
        {
            new("Player", 0, m => m.ClassLevel.Length > 0 ? $"{m.Name} · {m.ClassLevel}" : m.Name, m => m.Name, Right: false),
            new("Mobs", 56, m => $"{m.Mobs:N0}", m => m.Mobs, Tip: "Corpses they took loot from"),
            new("Items", 56, m => $"{m.Items:N0}", m => m.Items),
            new("Coin", 100, m => LootSummary.Coin(m.Copper), m => m.Copper, Tip: "Coin counted for them: the purses they looted that weren't split, whole, plus (for you) your share of split purses"),
            new("Items worth", 106, m => m.ItemValue > 0 || m.Unpriced > 0 ? LootSummary.Worth(m.ItemValue, m.Estimated > 0, m.Unpriced > 0) : "—", m => m.ItemValue,
                Tip: "What a vendor would pay for their items. ~ = some valued by their family, + = some with no known price (not counted)"),
            new("Total", 106, m => LootSummary.Worth(m.Total, m.Estimated > 0, m.Unpriced > 0), m => m.Total, Tip: "Coin plus items worth"),
            new("Share", 60, m => Fmt.Pct(m.Total / total), m => m.Total, Tip: "Of the day's total"),
            new("Coin / mob", 92, m => m.Mobs > 0 ? LootSummary.Coin((long)m.CopperPerMob) : "—", m => m.CopperPerMob),
        }, Rebuild, defaultSort: 5, bar: m => m.Total, color: BrushOf, click: m => SelectMember(m.Name));
    }

    private FrameworkElement ItemsTable(LootDay d) =>
        Searchable(d.ItemsWorth, i => i.Item, rows => Table.Build("loot-items", rows, new Col<ItemWorth>[]
        {
            new("Item", 0, i => i.Item, i => i.Item, Right: false),
            new("Taken", 60, i => $"{i.Count:N0}", i => i.Count),
            new("Each", 84, i => i.Value.Known ? (i.Value.Estimated ? "~" : "") + LootSummary.Coin(i.Value.Each) : "—", i => i.Value.Each,
                Tip: "What a vendor pays for one. ~ = valued by its family; — = no price known yet (sell one to a vendor to learn it)"),
            new("Worth", 100, i => i.Value.Known ? (i.Value.Estimated ? "~" : "") + LootSummary.Coin(i.Total) : "—", i => i.Total),
            new("Looters", 62, i => $"{i.By.Count:N0}", i => i.By.Count),
            new("Taken by", 270, i => string.Join(", ", i.By.Take(4).Select(b => b.Count > 1 ? $"{b.Who} {b.Count}" : b.Who)) + (i.By.Count > 4 ? $" +{i.By.Count - 4}" : ""),
                Right: false),
        }, Rebuild, defaultSort: 3, bar: i => i.Total, color: _ => Palette.You,
        rowTip: i => string.Join("\n", i.By.Select(b => $"{b.Who}: {b.Count}")), copy: LootSummary.ItemLine));

    /// <summary>The search over the item lists of this page (the day's, and a player's): one text, kept as the reader moves between them.</summary>
    private string _itemSearch = "";

    private FrameworkElement Searchable<T>(IReadOnlyList<T> rows, Func<T, string> name, Func<IReadOnlyList<T>, FrameworkElement> table) =>
        Table.Searchable(rows, name, "Search items by name", () => _itemSearch, s => _itemSearch = s, table);

    private FrameworkElement MobsTable(LootDay d) =>
        Table.Build("loot-mobs", d.Mobs, new Col<MobLoot>[]
        {
            new("Mob", 0, m => m.Mob, m => m.Mob, Right: false),
            new("Looted", 70, m => $"{m.Looted:N0}", m => m.Looted),
            new("Coin", 108, m => LootSummary.Coin(m.Copper), m => m.Copper),
            new("Coin each", 100, m => m.Looted > 0 && m.Copper > 0 ? LootSummary.Coin(m.Copper / m.Looted) : "—", m => m.CopperEach),
            new("Items each", 92, m => m.ItemsEach.ToString("0.0", CultureInfo.CurrentCulture), m => m.ItemsEach),
            new("Looted most by", 150, m => m.TopLooter, m => m.TopLooter, Right: false),
        }, Rebuild, bar: m => m.Looted, color: _ => Palette.Taken);

    private FrameworkElement MemberItemsTable(LootMember m) =>
        Searchable(m.Taken, i => i.Item, rows => Table.Build("loot-member-items", rows, new Col<MemberItem>[]
        {
            new("Item", 0, i => i.Item, i => i.Item, Right: false),
            new("Taken", 60, i => $"{i.Count:N0}", i => i.Count),
            new("Each", 84, i => i.Value.Known ? (i.Value.Estimated ? "~" : "") + LootSummary.Coin(i.Value.Each) : "—", i => i.Value.Each,
                Tip: "What a vendor pays for one. ~ = valued by its family; — = no price known yet"),
            new("Worth", 100, i => i.Value.Known ? (i.Value.Estimated ? "~" : "") + LootSummary.Coin(i.Total) : "—", i => i.Total),
            new("Group share", 104, i => i.GroupCount > i.Count ? $"{Fmt.Pct(i.Share)} of {i.GroupCount:N0}" : "all of them", i => i.Share,
                Tip: "How many of that item the group took, and how many of those this player took"),
            new("Mostly from", 200, i => i.MostlyFrom, i => i.MostlyFrom, Right: false),
        }, Rebuild, defaultSort: 3, bar: i => i.Total, color: _ => BrushOf(m), copy: LootSummary.MemberItemLine));

    private FrameworkElement MemberMobsTable(LootMember m) =>
        Table.Build("loot-member-mobs", m.MobList, new Col<MemberMob>[]
        {
            new("Mob", 0, x => x.Mob, x => x.Mob, Right: false),
            new("Looted", 70, x => $"{x.Looted:N0}", x => x.Looted),
            new("Coin", 108, x => LootSummary.Coin(x.Copper), x => x.Copper),
            new("Coin each", 100, x => x.Copper > 0 ? LootSummary.Coin((long)x.CopperEach) : "—", x => x.CopperEach),
            new("Items", 64, x => $"{x.Items:N0}", x => x.Items),
            new("Items each", 92, x => x.ItemsEach.ToString("0.0", CultureInfo.CurrentCulture), x => x.ItemsEach),
        }, Rebuild, bar: x => x.Looted, color: _ => BrushOf(m));

    private sealed record LogRow(DateTime Time, string Who, string What, string From, Brush Brush);

    private FrameworkElement Log(IEnumerable<LootEvent> events)
    {
        var all = events.OrderByDescending(e => e.Time).ToList();
        var rows = all.Take(LogRows).Select(e => new LogRow(e.Time, e.Who,
            e.Kind == LootKind.Item ? $"[{e.Item}]" + (e.Count > 1 ? $" ×{e.Count}" : "") : LootSummary.Coin(e.Copper) + (e.Shared ? (e.Split > 0 ? $"  (split · your share {LootSummary.Coin(e.Split)})" : "  (split)") : ""),
            e.Source, e.Kind == LootKind.Item ? Palette.Text : Palette.Heal)).ToList();
        var table = Table.Build("loot-log", rows, new Col<LogRow>[]
        {
            new("Time", 80, r => r.Time.ToString("HH:mm:ss", CultureInfo.CurrentCulture), r => r.Time, Right: false),
            new("Looter", 150, r => r.Who, r => r.Who, Right: false),
            new("Loot", 0, r => r.What, r => r.What, Right: false),
            new("From", 240, r => r.From, r => r.From, Right: false),
        }, Rebuild, defaultSort: 0);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = all.Count > LogRows ? $"The latest {LogRows:N0} of {all.Count:N0} lines (the rest are in the ledger)" : $"{all.Count:N0} lines",
            FontSize = 11.5, Foreground = Palette.Muted, Margin = new Thickness(8, 0, 0, 6),
        });
        panel.Children.Add(table);
        return panel;
    }
}
