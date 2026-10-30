using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using MnMCompanion.Data;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion;

/// <summary>One page of the small window.</summary>
internal interface IMiniPage
{
    Control Element { get; }

    /// <summary>Whether it reads the saved history, which the window then keeps fresh while the page is on show.</summary>
    bool NeedsHistory { get; }

    /// <summary>Draws it (the data may arrive a moment later).</summary>
    void Refresh();

    /// <summary>Draws it now on this thread: for a picture, where nothing may finish later.</summary>
    void RefreshNow();

    /// <summary>Called every couple of seconds: redraws if something it shows has changed.</summary>
    void Tick();

    void Say(string text);
}

/// <summary>
/// A page of the small window that shows the saved history (vendor, trades, items, harvest) for the day and session on show, laid out like the
/// loot page: the day and session at the top, a headline figure, tabs, and ranked rows or log lines. It asks the saved history for the
/// session's stretch of time off the UI thread and redraws when the history, the session or the character changes.
/// </summary>
internal abstract class StatsPanel<T> : Border, IMiniPage where T : class
{
    protected readonly LootStore Store;
    protected readonly HistoryData? Data;
    protected readonly SessionScope Session;

    protected readonly TextBlock Sub = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    protected readonly TextBlock Big = new() { FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, HorizontalAlignment = HorizontalAlignment.Right };
    protected readonly TextBlock Caption = new() { FontSize = 10, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -2, 1, 0) };
    protected readonly StackPanel Body = new() { Margin = new Thickness(0, 6, 0, 0) };

    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _scope = new Button { HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(9, 4, 9, 4) }.Styled("text");
    private readonly TextBlock _status;
    private readonly string _historyKey;
    private readonly Action<string> _openHistory;
    private string _tab;
    private T? _data;
    private string _seen = "", _loading = "";
    private int _generation;

    private readonly record struct Context(HistoryStore Store, string Server, string Character, Period Period, string Key);

    /// <param name="historyKey">The History window page that shows the same thing in full ("Vendors", "Trades", "Changes", "Harvest").</param>
    protected StatsPanel(LootStore store, HistoryData? data, SessionScope session, TextBlock status, string historyKey, Action<string> openHistory, string[] tabs)
    {
        Store = store;
        Data = data;
        Session = session;
        _status = status;
        _historyKey = historyKey;
        _openHistory = openHistory;
        _tab = tabs[0];
        Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x15));
        Padding = new Thickness(12, 10, 12, 8);
        _scope.Tip("Which part of the day to show: this session, an earlier one, or the whole day. It is the same on every page.");

        var stack = new StackPanel();
        stack.Children.Add(SessionHeader.Build(session, _title, Sub, Big, Caption, _scope));
        if (tabs.Length > 1)
        {
            var bar = new Border { CornerRadius = new CornerRadius(8), Background = Ui.Subtle, Padding = new Thickness(2), Margin = new Thickness(0, 8, 0, 0) };
            var grid = new UniformGrid { Columns = tabs.Length };
            foreach (var t in tabs)
            {
                var name = t;
                var rb = new RadioButton { Content = new TextBlock { Text = t }, GroupName = "Mini" + historyKey, IsChecked = t == _tab }.Styled("segment");
                rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked != true || _tab == name) return; _tab = name; Redraw(); };
                grid.Children.Add(rb);
            }
            bar.Child = grid;
            stack.Children.Add(bar);
        }
        var root = new DockPanel();
        DockPanel.SetDock(stack, Dock.Top);
        root.Children.Add(stack);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = Body });
        Child = root;
        session.Changed += () => { if (this.OnScreen()) Refresh(); };
    }

    public Control Element => this;

    public bool NeedsHistory => true;

    protected abstract T Query(HistoryStore store, string server, string character, Period period);

    protected abstract void Render(T data, string tab);

    // ------------------------------------------------------------------ reading

    private string KeyNow() => Session.Key + "|" + Store.SelectionKey + "|" + (Data?.Version ?? -1);

    private Context? Capture()
    {
        _title.Text = SessionHeader.DayTitle(Session.Day);
        _scope.Content = Session.ScopeLabel() + "  ▾";
        if (Store.Selected is not { } who) { Message("No Monsters and Memories ledger found."); return null; }
        if (Data?.Store is not { } store) { Message("The saved history isn't available: " + (Data?.Status ?? "not opened")); return null; }
        return new Context(store, who.Server, who.Character, Session.Period, KeyNow());
    }

    public void Refresh()
    {
        var generation = ++_generation;
        if (Capture() is not { } c) return;
        _loading = c.Key;
        _ = LoadAsync(c, generation);
    }

    private async Task LoadAsync(Context c, int generation)
    {
        try
        {
            var data = await Task.Run(() => Query(c.Store, c.Server, c.Character, c.Period));
            if (generation == _generation) Show(data, c.Key);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (generation == _generation) Message("Can't read the saved history: " + ex.Message);
        }
    }

    public void RefreshNow()
    {
        ++_generation;
        if (Capture() is not { } c) return;
        Show(Query(c.Store, c.Server, c.Character, c.Period), c.Key);
    }

    public void Tick()
    {
        if (!this.OnScreen()) return;
        var key = KeyNow();
        if (key != _seen && key != _loading) Refresh();
    }

    private void Show(T data, string key)
    {
        _data = data;
        _seen = key;
        _loading = "";
        Redraw();
    }

    private void Redraw()
    {
        Body.Children.Clear();
        Sub.Text = Big.Text = Caption.Text = "";
        _status.Text = "";
        if (_data is not null) Render(_data, _tab);
    }

    private void Message(string text)
    {
        _data = null;
        Body.Children.Clear();
        Sub.Text = Big.Text = Caption.Text = "";
        Body.Children.Add(LootPanel.Line(text, Palette.Muted));
    }

    public void Say(string text) => _status.Text = text;

    // ------------------------------------------------------------------ drawing helpers

    protected void Empty(string text) => Body.Children.Add(LootPanel.Line(text, Palette.Muted));

    protected static string Plural(int n, string word) => LootPanel.Plural(n, word);

    protected static string Coin(long copper) => LootSummary.Coin(copper);

    protected void OpenInHistory() => _openHistory(_historyKey);

    protected Control RankRow(int rank, string name, string sub, string value, IBrush brush, double fraction, string tip) =>
        LootPanel.Row(rank, name, sub, value, brush, fraction, tip, OpenInHistory);

    protected static TextBlock LogLine(params (string Text, IBrush Brush, bool Bold)[] parts)
    {
        var line = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 2), Inlines = new InlineCollection() };
        foreach (var (text, brush, bold) in parts)
            if (text.Length > 0) line.Inlines!.Add(new Run(text) { Foreground = brush, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal });
        return line;
    }

    protected static string Time(DateTime t) => t.ToString("HH:mm:ss") + "  ";

    protected string Heading(string what) => $"{what} · {SessionHeader.DayTitle(Session.Day)} · {Session.ScopeLabel()}";
}

// ======================================================================================================================== Vendor

internal sealed record VendorMiniData(IReadOnlyList<VendorRow> Rows);

internal sealed class VendorMini(LootStore store, HistoryData? data, SessionScope session, TextBlock status, Action<string> openHistory)
    : StatsPanel<VendorMiniData>(store, data, session, status, "Vendors", openHistory, ["Sales", "Purchases", "Log"])
{
    protected override VendorMiniData Query(HistoryStore s, string server, string character, Period period) => new(s.Vendor(server, character, period));

    protected override void Render(VendorMiniData data, string tab)
    {
        var rows = data.Rows;
        var t = VendorSummary.Totals(rows);
        Sub.Text = $"{Plural(t.SoldStacks + t.BoughtStacks, "stack")} · sold {Coin(t.SoldCopper)} · bought {Coin(t.BoughtCopper)}";
        Big.Text = (t.Net < 0 ? "−" : "+") + Coin(Math.Abs(t.Net));
        Caption.Text = "sold minus bought";
        if (rows.Count == 0) { Empty("Nothing sold to or bought from a vendor in this part of the day."); return; }
        switch (tab)
        {
            case "Purchases": Items(rows, VendorDirection.Bought, Palette.Taken); break;
            case "Log": Log(rows); break;
            default: Items(rows, VendorDirection.Sold, Palette.Heal); break;
        }
    }

    private void Items(IReadOnlyList<VendorRow> rows, VendorDirection direction, IBrush brush)
    {
        var items = VendorSummary.ByItem(rows, direction);
        if (items.Count == 0) { Empty(direction == VendorDirection.Sold ? "Nothing sold yet." : "Nothing bought yet."); return; }
        double top = Math.Max(1, items.Max(i => (double)i.Total));
        int rank = 0;
        foreach (var i in items.Take(60))
            Body.Children.Add(RankRow(++rank, i.Item, Plural(i.Units, "item"), Coin(i.Total), brush, i.Total / top,
                $"{Plural(i.Stacks, "stack")} · {Coin(i.Each)} each · last {i.Last:HH:mm}"));
        if (items.Count > 60) Empty($"{items.Count - 60} more kinds are in the History window.");
    }

    private void Log(IReadOnlyList<VendorRow> rows)
    {
        foreach (var r in rows.OrderByDescending(r => r.Time).Take(200))
            Body.Children.Add(LogLine((Time(r.Time), Palette.Muted, false),
                (r.Direction == VendorDirection.Sold ? "Sold  " : "Bought  ", r.Direction == VendorDirection.Sold ? Palette.Heal : Palette.Taken, true),
                ($"[{r.Item}]" + (r.Qty > 1 ? $" ×{r.Qty}" : ""), Palette.Gold, false), (r.TotalCopper > 0 ? "  " + Coin(r.TotalCopper) : "", Palette.Text, false)));
    }
}

// ======================================================================================================================== Trades

internal sealed record TradesMiniData(IReadOnlyList<TradeRow> Rows);

internal sealed class TradesMini(LootStore store, HistoryData? data, SessionScope session, TextBlock status, Action<string> openHistory)
    : StatsPanel<TradesMiniData>(store, data, session, status, "Trades", openHistory, ["Trades"])
{
    protected override TradesMiniData Query(HistoryStore s, string server, string character, Period period) => new(s.Trades(server, character, period));

    protected override void Render(TradesMiniData data, string tab)
    {
        var trades = TradeSummary.Pair(data.Rows);
        long gave = trades.Sum(t => t.GaveCoin), got = trades.Sum(t => t.ReceivedCoin);
        Sub.Text = $"{Plural(trades.Count, "trade")} · {Plural(trades.Select(t => t.Partner).Distinct().Count(), "trader")} · gave {Coin(gave)}, got {Coin(got)} in coin";
        Big.Text = trades.Count.ToString("N0");
        Caption.Text = trades.Count == 1 ? "trade" : "trades";
        if (trades.Count == 0) { Empty("No trades in this part of the day."); return; }
        foreach (var t in trades.OrderByDescending(t => t.Time).Take(100))
            Body.Children.Add(LogLine((Time(t.Time), Palette.Muted, false), (TradeSummary.Label(t.Partner), Palette.Text, true),
                ("  gave ", Palette.Muted, false), (TradeSummary.Describe(t.GaveCoin, t.GaveItems), Palette.Taken, false),
                ("  got ", Palette.Muted, false), (TradeSummary.Describe(t.ReceivedCoin, t.ReceivedItems), Palette.Heal, false)));
    }
}

// ======================================================================================================================== Items (dropped, picked up, destroyed)

internal sealed record ItemsMiniData(IReadOnlyList<ItemMoveRow> Rows);

internal sealed class ItemsMini(LootStore store, HistoryData? data, SessionScope session, TextBlock status, Action<string> openHistory)
    : StatsPanel<ItemsMiniData>(store, data, session, status, "Changes", openHistory, ["Log", "Lost"])
{
    protected override ItemsMiniData Query(HistoryStore s, string server, string character, Period period) => new(s.ItemMoves(server, character, period));

    protected override void Render(ItemsMiniData data, string tab)
    {
        var t = ItemChangeSummary.Totals(data.Rows);
        Sub.Text = $"{t.Dropped:N0} dropped · {t.PickedUp:N0} picked up · {t.Destroyed:N0} destroyed";
        Big.Text = (t.Dropped + t.PickedUp + t.Destroyed).ToString("N0");
        Caption.Text = "item changes";
        if (data.Rows.Count == 0) { Empty("No items dropped, picked up or destroyed in this part of the day."); return; }
        if (tab == "Lost") Lost(data.Rows); else Log(data.Rows);
    }

    private void Log(IReadOnlyList<ItemMoveRow> rows)
    {
        foreach (var r in rows.OrderByDescending(r => r.Time).Take(200))
        {
            var (word, brush) = r.Kind switch { ItemMoveKind.Dropped => ("Dropped  ", Palette.Gold), ItemMoveKind.PickedUp => ("Picked up  ", Palette.Heal), _ => ("Destroyed  ", Palette.Taken) };
            Body.Children.Add(LogLine((Time(r.Time), Palette.Muted, false), (word, brush, true), ($"[{r.Item}]" + (r.Qty > 1 ? $" ×{r.Qty}" : ""), Palette.Gold, false),
                (r.Zone.Length > 0 ? "  " + ZoneMaps.DisplayName(r.Zone) : "", Palette.Muted, false)));
        }
    }

    private void Lost(IReadOnlyList<ItemMoveRow> rows)
    {
        var lost = ItemChangeSummary.MostDiscarded(rows).Where(i => i.Lost > 0).ToList();
        if (lost.Count == 0) { Empty("Nothing was lost: whatever was dropped was picked up again."); return; }
        double top = Math.Max(1, lost.Max(i => (double)i.Lost));
        int rank = 0;
        foreach (var i in lost.Take(60))
            Body.Children.Add(RankRow(++rank, i.Item, $"{i.Dropped:N0} dropped · {i.Destroyed:N0} destroyed", "−" + i.Lost.ToString("N0"), Palette.Taken, i.Lost / top,
                $"{i.PickedUp:N0} picked up again"));
    }
}

// ======================================================================================================================== Harvest

internal sealed record HarvestMiniData(IReadOnlyList<HarvestRow> Rows);

internal sealed class HarvestMini(LootStore store, HistoryData? data, SessionScope session, TextBlock status, Action<string> openHistory)
    : StatsPanel<HarvestMiniData>(store, data, session, status, "Harvest", openHistory, ["Items", "Log"])
{
    protected override HarvestMiniData Query(HistoryStore s, string server, string character, Period period) => new(s.Harvests(server, character, period));

    protected override void Render(HarvestMiniData data, string tab)
    {
        var t = HarvestSummary.Totals(data.Rows);
        Sub.Text = $"{Plural(t.Harvests, "harvest")} · {t.Units:N0} gathered · {Plural(t.Kinds, "kind")}";
        if (t.PerHour is { } perHour) { Big.Text = $"{perHour:N0}/hr"; Caption.Text = "gathered an hour"; }
        else { Big.Text = t.Units.ToString("N0"); Caption.Text = "gathered"; }
        if (data.Rows.Count == 0) { Empty("Nothing gathered in this part of the day."); return; }
        if (tab == "Log") Log(data.Rows); else Items(data.Rows);
    }

    private void Items(IReadOnlyList<HarvestRow> rows)
    {
        var items = HarvestSummary.ByItem(rows);
        double top = Math.Max(1, items.Max(i => (double)i.Units));
        int rank = 0;
        foreach (var i in items.Take(60))
        {
            var v = Store.Values.Of(i.Item);
            var worth = v.Known ? (v.Estimated ? "~" : "") + Coin(v.Each * i.Units) : "";
            Body.Children.Add(RankRow(++rank, i.Item, worth.Length > 0 ? $"worth {worth}" : Plural(i.Harvests, "harvest"), "×" + i.Units.ToString("N0"), Palette.Heal, i.Units / top,
                $"{Plural(i.Harvests, "harvest")} · {string.Join(", ", i.Zones.Select(ZoneMaps.DisplayName))}"));
        }
    }

    private void Log(IReadOnlyList<HarvestRow> rows)
    {
        foreach (var r in rows.OrderByDescending(r => r.Time).Take(200))
            Body.Children.Add(LogLine((Time(r.Time), Palette.Muted, false), ($"[{r.Item}]" + (r.Qty > 1 ? $" ×{r.Qty}" : ""), Palette.Gold, false),
                (r.Zone.Length > 0 ? "  " + ZoneMaps.DisplayName(r.Zone) : "", Palette.Muted, false)));
    }
}
