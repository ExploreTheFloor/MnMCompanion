using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MnMCompanion.Loot;

namespace MnMCompanion;

public sealed record LootDayItem(DateOnly Day, string Title, string Sub, string Coin, string Month, string Search, string Caption = "coin + items");

public sealed record LootMemberItem(string? Key, string Name, string Value, string Sub, IBrush Brush, GridLength Fill, GridLength Rest);

/// <summary>A month's heading in the list of days (it can't be picked).</summary>
public sealed record MonthHeading(string Name);

/// <summary>
/// The Loot page of the History window: the days with loot on the left, the day's group in the middle
/// (most valuable first, with a bar), and on the right the totals for everyone or, after a click on a name, that player's
/// items, the mobs they looted and every line. Reads the <see cref="LootStore"/>, which fills as the game writes its ledger;
/// the window looks again every couple of seconds.
/// </summary>
public sealed class LootHistoryPage : UserControl
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

    // The search box: it narrows the days, and what it finds across all of them is the first row of the list ("All days"), shown on the right
    // while that row is the one picked
    private bool _answer;
    private (string Query, long Version, ItemValues Values, LootDay? Day)? _found;   // the answer for the text in the box, kept until the ledger or the text changes
    private LootDayItem? _answerItem;
    private DispatcherTimer? _typing;
    private readonly Dictionary<string, string> _searches = [];   // what is typed into the search above each kind of table (items, monsters, players, log)

    // What the Windows build lays out in LootHistoryPage.xaml
    private readonly TextBlock DayCount = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBox SearchBox = new TextBox { PlaceholderText = "Search every day: player, item or mob" }.Styled("field")
        .Tip("Narrows the days to the ones where a player, item or monster has every word typed (any order, any case, part of a word is enough); the first row, All days, adds up what was found across every day");
    private readonly ListBox DayList = new ListBox().Styled("nav");
    private readonly TextBlock DayTitle = new() { Text = "No day selected", FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock DayWhen = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel ScopeBar = new();
    private readonly Border ScopeHost;
    private readonly ListBox MemberList = new ListBox().Styled("nav");
    private readonly ScrollViewer DetailScroll = new() { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly Border HeadSwatch = new() { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock HeadName = new() { FontSize = 26, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock HeadSub = new() { FontSize = 12, Foreground = Palette.Muted, Margin = new Thickness(22, 2, 0, 0) };
    private readonly StackPanel HeadFigures = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    // What reached the ledger's owner; shown only beside the group's total (a day, not one player or a search). Then the pair
    // moves under the name (SetHead), where two figures and their coin + items lines have the room
    private readonly StackPanel HeadOursBox = new() { Margin = new Thickness(0, 0, 30, 0), IsVisible = false };
    private readonly TextBlock HeadOursLabel = new() { Text = "OURS", FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock HeadOurs = new() { FontSize = 30, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock HeadOursCaption = new() { FontSize = 11, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -4, 0, 0) };
    private readonly TextBlock HeadBigLabel = new() { Text = "TOTAL", FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, IsVisible = false };
    private readonly TextBlock HeadBig = new() { FontSize = 30, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock HeadBigCaption = new() { FontSize = 11, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -4, 0, 0) };
    private readonly WrapPanel Tiles = new() { Margin = new Thickness(0, 0, -10, 6) };
    private readonly StackPanel CopyBar = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel TabBar = new() { Orientation = Orientation.Horizontal };
    private readonly Border TabHost;
    private readonly ContentControl TabContent = new();

    internal LootHistoryPage(LootStore store)
    {
        _store = store;
        Background = Ui.Window;
        Foreground = Palette.Text;
        ScopeHost = new Border { CornerRadius = new CornerRadius(8), Background = Ui.Subtle, Padding = new Thickness(2), Margin = new Thickness(0, 10, 0, 0), IsVisible = false, Child = ScopeBar };
        TabHost = new Border { CornerRadius = new CornerRadius(8), Background = Ui.Subtle, Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Child = TabBar };
        Content = BuildLayout();
        SearchBox.TextChanged += (_, _) => OnSearch();
        DayList.SelectionChanged += (_, _) => OnDaySelected();
        MemberList.SelectionChanged += (_, _) => OnMemberSelected();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
        AttachedToVisualTree += (_, _) =>
        {
            RefreshDays();
            if (_day is null && _items.Count > 0) DayList.SelectedItem = _items[0];
            _timer.Start();
        };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private Control BuildLayout()
    {
        DayList.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            MonthHeading month => new TextBlock { Text = month.Name, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Muted, Margin = new Thickness(0, 6, 0, 0) },
            LootDayItem day => DayRow(day),
            _ => new TextBlock(),
        });
        // A month's heading is a row of the list that can't be picked or pointed at
        DayList.ContainerPrepared += (_, e) =>
        {
            bool heading = e.Container.DataContext is MonthHeading || DayList.ItemsSource is IList<object> list && e.Index < list.Count && list[e.Index] is MonthHeading;
            e.Container.IsHitTestVisible = !heading;
            e.Container.Focusable = !heading;
        };
        MemberList.ItemTemplate = new FuncDataTemplate<LootMemberItem>((item, _) => item is null ? new TextBlock() : MemberRow(item));

        // Days
        var openFolder = new Button { Content = Glyphs.External, VerticalAlignment = VerticalAlignment.Top }.Styled("icon").Tip("Open the ledger folder");
        openFolder.Click += (_, _) => OnOpenFolder();
        var daysTitle = new StackPanel();
        daysTitle.Children.Add(new TextBlock { Text = "Loot", FontSize = 18, FontWeight = FontWeight.SemiBold });
        daysTitle.Children.Add(DayCount);
        var daysHead = new DockPanel { Margin = new Thickness(6, 0, 4, 10) };
        DockPanel.SetDock(openFolder, Dock.Right);
        daysHead.Children.Add(openFolder);
        daysHead.Children.Add(daysTitle);
        SearchBox.Margin = new Thickness(4, 0, 4, 10);
        var daysDock = new DockPanel { Margin = new Thickness(12, 16, 8, 8) };
        DockPanel.SetDock(daysHead, Dock.Top);
        DockPanel.SetDock(SearchBox, Dock.Top);
        daysDock.Children.Add(daysHead);
        daysDock.Children.Add(SearchBox);
        daysDock.Children.Add(DayList);
        var days = new Border { Background = Ui.Panel, BorderBrush = Ui.Stroke, BorderThickness = new Thickness(0, 0, 1, 0), Child = daysDock };

        // Group members
        var groupTop = new StackPanel { Margin = new Thickness(8, 0, 4, 12) };
        groupTop.Children.Add(DayTitle);
        groupTop.Children.Add(DayWhen);
        groupTop.Children.Add(ScopeHost);
        var groupDock = new DockPanel { Margin = new Thickness(10, 16, 8, 8) };
        DockPanel.SetDock(groupTop, Dock.Top);
        groupDock.Children.Add(groupTop);
        groupDock.Children.Add(MemberList);
        var group = new Border { BorderBrush = Ui.Stroke, BorderThickness = new Thickness(0, 0, 1, 0), Child = groupDock };
        Grid.SetColumn(group, 1);

        // Breakdown
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(HeadSwatch);
        name.Children.Add(HeadName);
        var names = new StackPanel();
        names.Children.Add(name);
        names.Children.Add(HeadSub);
        var ours = HeadOursBox;
        ours.Children.Add(HeadOursLabel);
        ours.Children.Add(HeadOurs);
        ours.Children.Add(HeadOursCaption);
        var total = new StackPanel();
        total.Children.Add(HeadBigLabel);
        total.Children.Add(HeadBig);
        total.Children.Add(HeadBigCaption);
        HeadFigures.Children.Add(ours);
        HeadFigures.Children.Add(total);
        Grid.SetColumn(HeadFigures, 1);
        var head = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        head.Children.Add(names);
        head.Children.Add(HeadFigures);
        var tabRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(CopyBar, Dock.Right);
        tabRow.Children.Add(CopyBar);
        tabRow.Children.Add(TabHost);
        var detail = new StackPanel { Margin = new Thickness(28, 20, 28, 28) };
        detail.Children.Add(head);
        detail.Children.Add(Tiles);
        detail.Children.Add(tabRow);
        detail.Children.Add(TabContent);
        DetailScroll.Content = detail;
        Grid.SetColumn(DetailScroll, 2);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(days);
        grid.Children.Add(group);
        grid.Children.Add(DetailScroll);
        return grid;
    }

    private static Control DayRow(LootDayItem day)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = day.Title, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        left.Children.Add(new TextBlock { Text = day.Sub, FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = day.Coin, FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        right.Children.Add(new TextBlock { Text = day.Caption, FontSize = 10, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Control MemberRow(LootMemberItem m)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = m.Brush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        name.Children.Add(new TextBlock { Text = m.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(name);
        var value = new TextBlock { Text = m.Value, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        var sub = new TextBlock { Text = m.Sub, FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(16, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(sub, 1);
        Grid.SetColumnSpan(sub, 2);
        grid.Children.Add(sub);
        var bar = new Grid { Height = 3, Margin = new Thickness(16, 6, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = m.Fill });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = m.Rest });
        var back = new Border { CornerRadius = new CornerRadius(1.5), Background = Ui.Subtle };
        Grid.SetColumnSpan(back, 2);
        bar.Children.Add(back);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = m.Brush });
        Grid.SetRow(bar, 2);
        Grid.SetColumnSpan(bar, 2);
        grid.Children.Add(bar);
        return grid;
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
        if (_answer) return;   // the answer is drawn again by RefreshDays whenever the ledger changes
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

        ShowList();
    }

    /// <summary>Draws the list of days (with the search's answer first, when there is one) and the right-hand side.</summary>
    private void ShowList()
    {
        if (_answer && NameSearch.Words(SearchBox.Text ?? "").Length == 0) _answer = false;
        _updatingLists = true;
        // The days that match the search, under a heading for each month
        var rows = new List<object>();
        foreach (var month in WithAnswer().Where(Matches).GroupBy(i => i.Month))
        {
            rows.Add(new MonthHeading(month.Key));
            rows.AddRange(month);
        }
        DayList.ItemsSource = rows;
        if (_answer) DayList.SelectedItem = _answerItem;
        else if (_day is { } keep) DayList.SelectedItem = _items.FirstOrDefault(i => i.Day == keep);
        _updatingLists = false;
        DayCount.Text = _items.Count == 0 ? "No loot in the ledger yet" : Plural(_items.Count, "day") + " with loot";
        if (_items.Count == 0) ShowNothing();
        else if (_answer || _day is not null) RenderDay();
    }

    // ---- The search across every day ----------------------------------------------------------------------------

    private string Query => (SearchBox.Text ?? "").Trim();

    /// <summary>What the box finds across every day of the characters in view, worked out like one day; null when nothing is typed or nothing matches.</summary>
    private LootDay? Found()
    {
        var query = Query;
        if (NameSearch.Words(query).Length == 0) return null;
        var values = _store.Values;
        if (_found is { } f && f.Query == query && f.Version == _store.Version && ReferenceEquals(f.Values, values)) return f.Day;
        var day = LootSearch.Answer(_store.Days().SelectMany(d => _store.Events(d)), query, values);
        _found = (query, _store.Version, values, day);
        return day;
    }

    /// <summary>The days, with the row that stands for all of them first while something is typed and found.</summary>
    private List<LootDayItem> WithAnswer()
    {
        var list = new List<LootDayItem>(_items.Count + 1);
        _answerItem = null;
        if (Found() is { } a)
        {
            var query = Query;
            _answerItem = new LootDayItem(LootSearch.AnswerDay, "All days", $"{Plural(a.Totals.Items, "item")} taken · {Plural(LootSearch.Days(a.Events), "day")}",
                LootSummary.Worth(a.ItemValue, a.Approximate, a.Unpriced > 0), $"Matching “{query}”", query, "items worth");
            list.Add(_answerItem);
        }
        list.AddRange(_items);
        return list;
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n:N0} {word}s";

    /// <summary>"Today", "Yesterday", "Tue, Oct 20" (with the year in another year): short enough for the day list.</summary>
    private static string DayName(DateOnly day) =>
        day == Today ? "Today" : day == Today.AddDays(-1) ? "Yesterday" : day.ToString(day.Year == Today.Year ? "ddd, MMM d" : "MMM d, yyyy", CultureInfo.CurrentCulture);

    private bool Matches(LootDayItem i) => NameSearch.Matches(i.Search, SearchBox.Text ?? "");

    private void OnSearch()
    {
        // Wait for a pause in the typing: the answer looks through every day, so each letter must not redo it
        _typing ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _typing.Stop();
        _typing.Tick -= OnTyped;
        _typing.Tick += OnTyped;
        _typing.Start();
    }

    private void OnTyped(object? sender, EventArgs e) => ApplySearch();

    /// <summary>Types into the box (for a picture) and shows the result at once, optionally with one looter of the answer picked.</summary>
    internal void SearchFor(string text, string? member = null)
    {
        SearchBox.Text = text;
        ApplySearch();
        if (member is not null && _answer) SelectMember(member);
    }

    /// <summary>
    /// The text in the box changed: narrow the days and, when something is typed, show what it finds across all of them; when it is cleared,
    /// go back to the day that was open. Refining a search keeps the tab and the looter that were picked.
    /// </summary>
    private void ApplySearch()
    {
        _typing?.Stop();
        var was = _answer;
        _answer = NameSearch.Words(SearchBox.Text ?? "").Length > 0;
        if (was != _answer)
        {
            _tab = "";
            _member = null;
            _pendingMember = null;
        }
        _scopeStart = null;
        ShowList();
        DetailScroll.ScrollToHome();
    }

    private void OnDaySelected()
    {
        if (_updatingLists || DayList.SelectedItem is not LootDayItem item) return;
        if (item.Day == LootSearch.AnswerDay) _answer = true;
        else
        {
            _answer = false;
            _day = item.Day;
        }
        _member = null;
        _pendingMember = null;
        _scopeStart = null;
        _tab = "";
        RenderDay();
        DetailScroll.ScrollToHome();
    }

    private void OnOpenFolder()
    {
        if (_store.LedgerPath is { } path && Directory.Exists(path)) Ui.Open(path);
    }

    /// <summary>Shows a day (from the panel), optionally one session of it and one player's breakdown.</summary>
    public void ShowDay(DateOnly day, string? member = null, LootSession? session = null)
    {
        _answer = false;
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
        if (DayList.SelectedItem is { } selected) DayList.ScrollIntoView(selected);
        _updatingLists = false;
        _day = day;
        _member = member;
        _pendingMember = member;
        _tab = "";
        RenderDay();
        DetailScroll.ScrollToHome();
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
        if (_answer) { RenderAnswer(); return; }
        if (_day is not { } day) return;
        var session = ScopeSession();
        var d = _shown = Load(day, session);
        _shownRaw = _store.Events(day).Count;
        DayTitle.Text = DayName(day) + (session is null ? "" : " · session");
        DayWhen.Text = d.Events.Count == 0 ? "Nothing looted" : $"{d.First:t} to {d.Last:t} · {Plural(d.Members.Count, "looter")}";
        BuildScopeBar(day, session);

        _updatingLists = true;
        // Everyone: the group's total. Each player: what they looted, since of the others the ledger never shows what an autosplit gave them
        long top = Math.Max(1, d.Members.Select(m => m.ItemValue).DefaultIfEmpty(0).Max());
        var rows = new List<LootMemberItem>
        {
            new(null, "Everyone", LootSummary.Worth(d.Total, d.Approximate, d.Unpriced > 0 || d.Unrecorded > 0), $"{Plural(d.Totals.Mobs, "mob")} · {(d.Unrecorded > 0 ? $"{d.Totals.Items:N0}+ items" : Plural(d.Totals.Items, "item"))}", Palette.Muted,
                new GridLength(1, GridUnitType.Star), new GridLength(0.0001, GridUnitType.Star)),
        };
        foreach (var m in d.Members)
        {
            double frac = (double)m.ItemValue / top;
            rows.Add(new LootMemberItem(m.Name, m.Class.Length > 0 ? $"{m.Name} · {m.Class}" : m.Name,
                m.ItemValue > 0 || m.Unpriced > 0 || m.Unrecorded > 0 ? LootSummary.ItemsWorthOf(m) : "—",
                $"{Plural(m.Mobs, "mob")} · {LootSummary.ItemsOf(m)}" + (m.Level > 0 ? $" · level {m.Level}" : ""), BrushOf(m),
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
        ScopeHost.IsVisible = sessions.Count > 1;
        if (sessions.Count <= 1) return;
        void Add(string text, LootSession? s)
        {
            var rb = new RadioButton
            {
                Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis }, Classes = { "segment" },
                GroupName = "LootScope", IsChecked = (s?.Start) == active?.Start, Margin = new Thickness(0, 0, 0, 1),
            };
            rb.IsCheckedChanged += (_, _) =>
            {
                if (_updatingLists || rb.IsChecked != true) return;
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

    private static IBrush BrushOf(LootMember m) => Palette.For(m.Name, m.Class.Length > 0 ? m.Class : null);

    private void OnMemberSelected()
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
        DetailScroll.ScrollToHome();
    }

    /// <summary>
    /// The headline figures at the top of the detail. With a <paramref name="day"/> they are two: Ours (what reached the ledger's owner) beside
    /// the group's Total; without one the single number stands alone.
    /// </summary>
    private void SetHead(string big, string caption, LootDay? day = null)
    {
        HeadBig.Text = big;
        HeadBigCaption.Text = caption;
        var both = day is not null;
        HeadOursBox.IsVisible = HeadBigLabel.IsVisible = both;
        HeadBig.Tip(both ? "Everyone's: every purse counted once, whole, plus what a vendor would pay for all the items" : null);

        // Alone, the number sits at the right of the name; as a pair, under it and left-aligned
        var align = both ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        Grid.SetRow(HeadFigures, both ? 1 : 0);
        Grid.SetColumn(HeadFigures, both ? 0 : 1);
        Grid.SetColumnSpan(HeadFigures, both ? 2 : 1);
        HeadFigures.HorizontalAlignment = align;
        HeadFigures.Margin = both ? new Thickness(22, 12, 0, 0) : new Thickness(0);
        foreach (var text in new[] { HeadOursLabel, HeadOurs, HeadOursCaption, HeadBigLabel, HeadBig, HeadBigCaption }) text.HorizontalAlignment = align;
        if (day is null) return;
        var ours = day.Ours;
        bool mine = day.Members.Any(m => m.You);
        HeadOurs.Text = mine ? LootSummary.Worth(ours.Total, ours.Approximate, ours.Unpriced > 0 || ours.Unrecorded > 0) : "—";
        HeadOursCaption.Text = mine ? $"coin {(ours.EstimatedShares > 0 ? "~" : "")}{LootSummary.Coin(ours.Copper)} + items {LootSummary.Worth(ours.ItemValue, ours.Estimated > 0, ours.Unrecorded > 0)}" : "nothing of yours yet";
        HeadOursBox.Tip(mine
            ? "What reached you: your coin (in a group, only your shares of the purses, your own included; the purses you looted alone, whole) and what a vendor would pay for the items you looted" +
              (ours.EstimatedShares > 0 ? Environment.NewLine + $"The ledger doesn't say what you were handed of the {ours.EstimatedShares:N0} purses you looted in a group, so those shares are estimated (~) from what you got of other players' purses." : "")
            : "Nothing has reached you in this view: no coin and no items");
    }

    private void ShowNothing(string title = "No loot yet", string name = "Nothing looted yet", string sub = "Loot shows up here as the game writes it to its ledger.")
    {
        DayTitle.Text = title;
        DayWhen.Text = "";
        MemberList.ItemsSource = null;
        HeadName.Text = name;
        HeadSub.Text = sub;
        SetHead("", "");
        HeadSwatch.Background = Palette.Muted;
        Tiles.Children.Clear();
        TabBar.Children.Clear();
        TabHost.IsVisible = false;
        CopyBar.Children.Clear();
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

    // ---- What the search found across every day ------------------------------------------------------------------

    private void RenderAnswer()
    {
        var query = Query;
        var d = _shown = Found();
        _shownRaw = -1;
        ScopeHost.IsVisible = false;
        if (d is null)
        {
            ShowNothing($"Matching “{query}”", $"Nothing matches “{query}”", "No item taken on any day has a player, item or monster with all of those words in it.");
            return;
        }
        int days = LootSearch.Days(d.Events);
        DayTitle.Text = $"Matching “{query}”";
        DayWhen.Text = $"{d.First:d} to {d.Last:d} · {Plural(days, "day")} · {Plural(d.Members.Count, "looter")}";

        _updatingLists = true;
        double top = Math.Max(1, d.Members.Select(m => m.Items).DefaultIfEmpty(0).Max());
        var rows = new List<LootMemberItem>
        {
            new(null, "Everyone", $"{d.Totals.Items:N0} taken", $"{Plural(d.Items.Count, "kind")} · {Plural(days, "day")}", Palette.Muted,
                new GridLength(1, GridUnitType.Star), new GridLength(0.0001, GridUnitType.Star)),
        };
        foreach (var m in d.Members.OrderByDescending(m => m.Items).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            double frac = m.Items / top;
            rows.Add(new LootMemberItem(m.Name, m.Class.Length > 0 ? $"{m.Name} · {m.Class}" : m.Name, $"{m.Items:N0} taken",
                $"{Plural(m.Taken.Count, "kind")} · {Plural(DaysOf(d, m.Name), "day")}", BrushOf(m),
                new GridLength(Math.Max(frac, 0.0001), GridUnitType.Star), new GridLength(Math.Max(1 - frac, 0.0001), GridUnitType.Star)));
        }
        MemberList.ItemsSource = rows;
        if (_member != null && d.Member(_member) is null) _member = null;
        MemberList.SelectedItem = rows.FirstOrDefault(r => string.Equals(r.Key, _member, StringComparison.OrdinalIgnoreCase));
        _updatingLists = false;
        RenderDetail();
    }

    private static int DaysOf(LootDay d, string who) =>
        d.Events.Where(e => e.Who.Equals(who, StringComparison.OrdinalIgnoreCase)).Select(e => e.Day).Distinct().Count();

    private void RenderAnswerEveryone(LootDay d)
    {
        var t = d.Totals;
        int days = LootSearch.Days(d.Events);
        HeadSwatch.Background = Palette.Muted;
        HeadName.Text = $"Matching “{_found?.Query ?? Query}” · all days";
        HeadSub.Text = $"{d.First:d} to {d.Last:d} · {Plural(days, "day")} · {Plural(d.Members.Count, "looter")}";
        SetHead(LootSummary.Worth(d.ItemValue, d.Approximate, d.Unpriced > 0), "items worth");

        Tiles.Children.Clear();
        AddTile("Taken", $"{t.Items:N0}", Plural(d.Items.Count, "kind"));
        AddTile("Days", $"{days:N0}", $"{d.First:MMM d} to {d.Last:MMM d}");
        AddTile("Looters", $"{d.Members.Count:N0}", d.Members.MaxBy(m => m.Items) is { } most ? $"most: {most.Name}" : "");
        AddTile("From", $"{d.Mobs.Count:N0}", d.Mobs.Count == 1 ? "kind of monster" : "kinds of monster");
        AddTile("Items worth", LootSummary.Worth(d.ItemValue, d.Approximate, d.Unpriced > 0), ItemsNote(d.Priced, d.Estimated, d.Unpriced));
        if (d.ItemsWorth.FirstOrDefault(i => i.Value.Known) is { } best) AddTile("Best item", best.Item, $"{(best.Value.Estimated ? "~" : "")}{LootSummary.Coin(best.Value.Each)} each · {best.Count:N0} taken");

        var query = _found?.Query ?? Query;
        Tabs(["Items", "Looters", "Monsters", "Days", "Loot log"], tab => tab switch
        {
            "Items" => ItemsTable(d, search: false),
            "Looters" => AnswerLootersTable(d),
            "Monsters" => AnswerMobsTable(d),
            "Days" => AnswerDaysTable(d),
            _ => Log(d.Events, withDate: true),
        }, _ => [("Copy items", () => LootSearch.Text(query, d))]);
    }

    private void RenderAnswerMember(LootDay day, LootMember m)
    {
        var t = day.Totals;
        var brush = BrushOf(m);
        HeadSwatch.Background = brush;
        HeadName.Text = m.Name;
        HeadSub.Text = (m.ClassLevel.Length > 0 ? m.ClassLevel + " · " : "") + $"matching “{_found?.Query ?? Query}” · taken {m.First:d} to {m.Last:d}";
        SetHead(LootSummary.Worth(m.ItemValue, m.Estimated > 0, m.Unpriced > 0), "items worth");

        string Of(double part, double whole) => whole > 0 ? $"{Fmt.Pct(part / whole)} of all found" : "—";
        Tiles.Children.Clear();
        AddTile("Taken", $"{m.Items:N0}", Of(m.Items, t.Items), brush);
        AddTile("Kinds", $"{m.Taken.Count:N0}", $"of {Plural(day.Items.Count, "kind")} found");
        AddTile("Days", $"{DaysOf(day, m.Name):N0}", $"{m.First:MMM d} to {m.Last:MMM d}");
        AddTile("Corpses", $"{m.Mobs:N0}", "looted that gave one");
        AddTile("Items worth", LootSummary.Worth(m.ItemValue, m.Estimated > 0, m.Unpriced > 0), ItemsNote(m.Priced, m.Estimated, m.Unpriced));
        if (m.Taken.OrderByDescending(x => x.Total).FirstOrDefault(x => x.Value.Known) is { } best) AddTile("Best item", best.Item, $"{(best.Value.Estimated ? "~" : "")}{LootSummary.Coin(best.Value.Each)} each · {best.Count:N0} taken");

        Tabs(["Items", "Monsters", "Loot log"], tab => tab switch
        {
            "Items" => MemberItemsTable(m, search: false),
            "Monsters" => MemberMobsTable(m, coin: false),
            _ => Log(day.Events.Where(e => e.Who.Equals(m.Name, StringComparison.OrdinalIgnoreCase)), withDate: true),
        }, _ => [("Copy summary", () => LootSummary.MemberText(m))]);
    }

    private Control AnswerLootersTable(LootDay d)
    {
        var days = d.Events.GroupBy(e => e.Who, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Day).Distinct().Count(), StringComparer.OrdinalIgnoreCase);
        double total = Math.Max(1, d.Totals.Items);
        return Table.Build("answer-looters", d.Members, new Col<LootMember>[]
        {
            new("Player", 0, m => m.ClassLevel.Length > 0 ? $"{m.Name} · {m.ClassLevel}" : m.Name, m => m.Name, Right: false),
            new("Taken", 66, m => $"{m.Items:N0}", m => m.Items, Tip: "How many of the matching items they took"),
            new("Kinds", 60, m => $"{m.Taken.Count:N0}", m => m.Taken.Count),
            new("Worth", 100, m => m.ItemValue > 0 || m.Unpriced > 0 ? LootSummary.Worth(m.ItemValue, m.Estimated > 0, m.Unpriced > 0) : "—", m => m.ItemValue,
                Tip: "What a vendor would pay for them. ~ = some valued by their family, + = some with no known price (not counted)"),
            new("Share", 60, m => Fmt.Pct(m.Items / total), m => m.Items, Tip: "Of everything the search found"),
            new("Days", 56, m => $"{days.GetValueOrDefault(m.Name):N0}", m => days.GetValueOrDefault(m.Name), Tip: "Days they took one"),
            new("Last", 90, m => m.Last.ToString("MMM d", CultureInfo.CurrentCulture), m => m.Last, Right: false),
        }, Rebuild, defaultSort: 1, bar: m => m.Items, color: BrushOf, click: m => SelectMember(m.Name));
    }

    private Control AnswerMobsTable(LootDay d) =>
        Table.Build("answer-mobs", d.Mobs, new Col<MobLoot>[]
        {
            new("Monster", 0, m => m.Mob, m => m.Mob, Right: false),
            new("Taken", 66, m => $"{m.Items:N0}", m => m.Items, Tip: "How many of the matching items came off this kind of monster"),
            new("Corpses", 74, m => $"{m.Looted:N0}", m => m.Looted, Tip: "Looted corpses that gave one"),
            new("Per corpse", 86, m => m.ItemsEach.ToString("0.0", CultureInfo.CurrentCulture), m => m.ItemsEach, Tip: "Matching items per corpse that gave one"),
            new("Taken most by", 150, m => m.TopLooter, m => m.TopLooter, Right: false),
        }, Rebuild, bar: m => m.Items, color: _ => Palette.Taken);

    private Control AnswerDaysTable(LootDay d) =>
        Table.Build("answer-days", LootSearch.ByDay(d.Events, _store.Values), new Col<DayTaken>[]
        {
            new("Day", 0, r => DayName(r.Day), r => r.Day, Right: false),
            new("Taken", 66, r => $"{r.Taken:N0}", r => r.Taken),
            new("Kinds", 60, r => $"{r.Kinds:N0}", r => r.Kinds),
            new("Looters", 66, r => $"{r.Looters:N0}", r => r.Looters),
            new("Worth", 100, r => LootSummary.Worth(r.Worth, r.Estimated, r.Unpriced), r => r.Worth),
        }, Rebuild, defaultSort: 0, bar: r => r.Taken, color: _ => Palette.You, click: r => ShowDay(r.Day),
        rowTip: _ => "Open this day");

    private void RenderEveryone(LootDay d)
    {
        if (LootSearch.IsAnswer(d)) { RenderAnswerEveryone(d); return; }
        var t = d.Totals;
        HeadSwatch.Background = Palette.Muted;
        HeadName.Text = "Everyone · " + DayName(d.Day);
        HeadSub.Text = $"{d.First:t} to {d.Last:t} · {Plural(d.Members.Count, "looter")}";
        SetHead(LootSummary.Worth(d.Total, d.Approximate, d.Unpriced > 0 || d.Unrecorded > 0), $"coin {LootSummary.Coin(t.Copper)} + items {LootSummary.Worth(d.ItemValue, d.Approximate, d.Unrecorded > 0)}", d);

        Tiles.Children.Clear();
        AddTile("Mobs looted", $"{t.Mobs:N0}", $"{d.Mobs.Count:N0} kinds");
        AddTile("Coin", LootSummary.Coin(t.Copper), t.Mobs > 0 ? $"{LootSummary.Coin(t.Copper / t.Mobs)} a mob" : "in purses");
        AddTile("Items worth", LootSummary.Worth(d.ItemValue, d.Approximate, d.Unpriced > 0), ItemsNote(d.Priced, d.Estimated, d.Unpriced));
        AddTile("Items", $"{t.Items:N0}", $"{d.Items.Count:N0} kinds");
        if (d.Unrecorded > 0) AddTile("Items not recorded", $"{d.Unrecorded:N0} corpses", "the game's ledger leaves out the items of monsters with an apostrophe in their name (Skri'Var…)", Palette.Muted);
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
            ? [("Copy summary", () => LootSummary.Breakdown(d.Day, d.Events, values: _store.Values)), ("Copy items", () => LootSummary.ItemsText(d.Day, NameSearch.Filter(d.ItemsWorth, i => i.Item, _searches.GetValueOrDefault("items", "")), ScopeText()))]
            : [("Copy summary", () => LootSummary.Breakdown(d.Day, d.Events, values: _store.Values))]);
    }

    private void RenderMember(LootDay day, LootMember m)
    {
        if (LootSearch.IsAnswer(day)) { RenderAnswerMember(day, m); return; }
        var t = day.Totals;
        var brush = BrushOf(m);
        HeadSwatch.Background = brush;
        HeadName.Text = m.Name;
        HeadSub.Text = (m.ClassLevel.Length > 0 ? m.ClassLevel + " · " : "") + $"looted {m.First:t} to {m.Last:t}";
        SetHead(LootSummary.Worth(m.ItemValue, m.Estimated > 0, m.Unpriced > 0), "items worth");

        string Of(double part, double whole) => whole > 0 ? $"{Fmt.Pct(part / whole)} of the group's" : "—";
        Tiles.Children.Clear();
        AddTile("Mobs looted", $"{m.Mobs:N0}", Of(m.Mobs, t.Mobs), brush);
        // Purses are whole and known for anyone; what an autosplit gave a player is known only for the ledger's owner
        AddTile("Purses looted", LootSummary.Coin(m.Purses), Of(m.Purses, t.Copper));
        if (m.You) AddTile("Your coin", (m.EstimatedShares > 0 ? "~" : "") + LootSummary.Coin(m.Copper), m.Received > 0 ? $"{(m.EstimatedShares > 0 ? "~" : "")}{LootSummary.Coin(m.Received)} in splits" : "purses you kept", Palette.Heal);
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

    private void AddTile(string label, string value, string sub, IBrush? accent = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Palette.Muted });
        stack.Children.Add(new TextBlock
        {
            Text = value, FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 2, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = accent ?? Palette.Text,
        }.Tip(value));
        stack.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis }.Tip(sub));
        Tiles.Children.Add(new Border { Classes = { "tile" }, Child = stack });
    }

    private void Tabs(IReadOnlyList<string> tabs, Func<string, Control> content, Func<string, (string Label, Func<string> Text)[]>? copies = null)
    {
        if (!tabs.Contains(_tab)) _tab = tabs[0];
        TabHost.IsVisible = true;
        void FillCopies(string tab)
        {
            CopyBar.Children.Clear();
            foreach (var (label, text) in copies?.Invoke(tab) ?? [])
            {
                var b = new Button { Content = label, Classes = { "text" }, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), [ToolTip.TipProperty] = "Copy to the clipboard as text" };
                var shown = label;
                b.Click += (_, _) =>
                {
                    b.Content = ClipboardText.Copy(text()) ? "Copied" : "Clipboard busy";
                    Ui.Later(TimeSpan.FromMilliseconds(1400), () => b.Content = shown);
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
                Classes = { "segment" },
                GroupName = "LootTab",
                IsChecked = t == _tab,
            };
            rb.IsCheckedChanged += (_, _) =>
            {
                if (rb.IsChecked != true || _tab == t) return;
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
        var offset = DetailScroll.Offset;
        RenderDetail();
        Dispatcher.UIThread.Post(() => DetailScroll.Offset = offset, DispatcherPriority.Loaded);
    }

    private Control LootersTable(LootDay d)
    {
        // Compared by what each looted: of other players the ledger never shows what an autosplit gave them, only the ledger's owner's own coin is known
        // (it is the OURS figure at the top)
        double total = Math.Max(1, d.ItemValue);
        return Searchable("looters", "Search players by name or class", true, d.Members, m => m.Name + " " + m.Class, rows => Table.Build("loot-members", rows, new Col<LootMember>[]
        {
            new("Player", 0, m => m.ClassLevel.Length > 0 ? $"{m.Name} · {m.ClassLevel}" : m.Name, m => m.Name, Right: false),
            new("Mobs", 56, m => $"{m.Mobs:N0}", m => m.Mobs, Tip: "Corpses they took loot from"),
            new("Items", 56, m => m.Items == 0 && m.Unrecorded > 0 ? "—" : $"{m.Items:N0}{(m.Unrecorded > 0 ? "+" : "")}", m => m.Items, Tip: "+ = the ledger left out the items of some of their corpses (a monster with an apostrophe in its name)"),
            new("Items worth", 106, m => m.ItemValue > 0 || m.Unpriced > 0 || m.Unrecorded > 0 ? LootSummary.ItemsWorthOf(m) : "—", m => m.ItemValue,
                Tip: "What a vendor would pay for their items. ~ = some valued by their family, + = some with no known price (not counted)"),
            new("Share", 60, m => Fmt.Pct(m.ItemValue / total), m => m.ItemValue, Tip: "Of the items worth looted in all"),
            new("Coin / mob", 92, m => m.Mobs > 0 ? LootSummary.Coin((long)m.CopperPerMob) : "—", m => m.CopperPerMob, Tip: "What the purses they looted held a mob, whole"),
        }, Rebuild, defaultSort: 3, bar: m => m.ItemValue, color: BrushOf, click: m => SelectMember(m.Name)));
    }

    private Control ItemsTable(LootDay d, bool search = true) =>
        Searchable("items", "Search items by name", search, d.ItemsWorth, i => i.Item, rows => Table.Build("loot-items", rows, new Col<ItemWorth>[]
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

    /// <summary>
    /// A table with a search box over it (<see cref="Table.Searchable"/>), or the bare table when <paramref name="search"/> is false. What is typed is
    /// kept per <paramref name="key"/>, so the items tabs (the day's and a player's) share one text, and so do the monsters tabs, and each is
    /// kept as the reader moves between tabs and days.
    /// </summary>
    private Control Searchable<T>(string key, string hint, bool search, IReadOnlyList<T> rows, Func<T, string> name, Func<IReadOnlyList<T>, Control> table) =>
        search ? Table.Searchable(rows, name, hint, () => _searches.GetValueOrDefault(key, ""), s => _searches[key] = s, table) : table(rows);

    private Control MobsTable(LootDay d) =>
        Searchable("mobs", "Search monsters by name", true, d.Mobs, m => m.Mob, rows => Table.Build("loot-mobs", rows, new Col<MobLoot>[]
        {
            new("Mob", 0, m => m.Mob, m => m.Mob, Right: false),
            new("Looted", 70, m => $"{m.Looted:N0}", m => m.Looted),
            new("Coin", 108, m => LootSummary.Coin(m.Copper), m => m.Copper),
            new("Coin each", 100, m => m.Looted > 0 && m.Copper > 0 ? LootSummary.Coin(m.Copper / m.Looted) : "—", m => m.CopperEach),
            new("Items each", 92, m => m.ItemsEach.ToString("0.0", CultureInfo.CurrentCulture), m => m.ItemsEach),
            new("Looted most by", 150, m => m.TopLooter, m => m.TopLooter, Right: false),
        }, Rebuild, bar: m => m.Looted, color: _ => Palette.Taken));

    private Control MemberItemsTable(LootMember m, bool search = true) =>
        Searchable("items", "Search items by name", search, m.Taken, i => i.Item, rows => Table.Build("loot-member-items", rows, new Col<MemberItem>[]
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

    /// <summary>A player's monsters; with <paramref name="coin"/> off (the answer to a search, which is about items) the purse columns are left out.</summary>
    private Control MemberMobsTable(LootMember m, bool coin = true) =>
        Searchable("mobs", "Search monsters by name", true, m.MobList, x => x.Mob, rows => Table.Build(coin ? "loot-member-mobs" : "answer-member-mobs", rows,
            coin
                ? new Col<MemberMob>[]
                {
                    new("Mob", 0, x => x.Mob, x => x.Mob, Right: false),
                    new("Looted", 70, x => $"{x.Looted:N0}", x => x.Looted),
                    new("Coin", 108, x => LootSummary.Coin(x.Copper), x => x.Copper),
                    new("Coin each", 100, x => x.Copper > 0 ? LootSummary.Coin((long)x.CopperEach) : "—", x => x.CopperEach),
                    new("Items", 64, x => $"{x.Items:N0}", x => x.Items),
                    new("Items each", 92, x => x.ItemsEach.ToString("0.0", CultureInfo.CurrentCulture), x => x.ItemsEach),
                }
                : new Col<MemberMob>[]
                {
                    new("Monster", 0, x => x.Mob, x => x.Mob, Right: false),
                    new("Taken", 66, x => $"{x.Items:N0}", x => x.Items, Tip: "How many of the matching items they took off this kind of monster"),
                    new("Corpses", 74, x => $"{x.Looted:N0}", x => x.Looted, Tip: "Looted corpses that gave one"),
                    new("Per corpse", 86, x => x.ItemsEach.ToString("0.0", CultureInfo.CurrentCulture), x => x.ItemsEach),
                }, Rebuild, bar: x => x.Looted, color: _ => BrushOf(m)));

    private sealed record LogRow(DateTime Time, string Who, string What, string From, IBrush IBrush);

    /// <summary>The loot lines, newest first, with a search box over them; <paramref name="withDate"/> when they can be from several days.</summary>
    private Control Log(IEnumerable<LootEvent> events, bool withDate = false)
    {
        var all = events.OrderByDescending(e => e.Time).Select(e => new LogRow(e.Time, e.Who,
            e.Kind == LootKind.Item ? $"[{e.Item}]" + (e.Count > 1 ? $" ×{e.Count}" : "") : LootSummary.Coin(e.Copper) + (e.Shared ? (e.Split > 0 ? $"  (split · your share {LootSummary.Coin(e.Split)})" : "  (split)") : ""),
            e.Source, e.Kind == LootKind.Item ? Palette.Text : Palette.Heal)).ToList();
        return Searchable("log", "Search the log by looter, item or monster", true, all, r => $"{r.Who} {r.What} {r.From}", shown =>
        {
            var table = Table.Build("loot-log", shown.Take(LogRows).ToList(), new Col<LogRow>[]
            {
                new("Time", withDate ? 124 : 80, r => r.Time.ToString(withDate ? "MMM d  HH:mm" : "HH:mm:ss", CultureInfo.CurrentCulture), r => r.Time, Right: false),
                new("Looter", 150, r => r.Who, r => r.Who, Right: false),
                new("Loot", 0, r => r.What, r => r.What, Right: false),
                new("From", 240, r => r.From, r => r.From, Right: false),
            }, Rebuild, defaultSort: 0);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = shown.Count > LogRows ? $"The latest {LogRows:N0} of {shown.Count:N0} lines (the rest are in the ledger)" : $"{shown.Count:N0} lines",
                FontSize = 11.5, Foreground = Palette.Muted, Margin = new Thickness(8, 0, 0, 6),
            });
            panel.Children.Add(table);
            return panel;
        });
    }
}
