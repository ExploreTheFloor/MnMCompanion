using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MnMCompanion.Loot;

namespace MnMCompanion;

/// <summary>
/// The loot panel, laid out like the DPS meter: a headline (the day's coin plus what its items are worth), tabs, and ranked
/// rows with a bar behind each (the looters by value, the items, the mobs) or the feed of loot. It shows one <em>session</em>
/// of a day (by default today's current one: "New session" archives what has been looted so far and starts again from
/// nothing, the ledger keeping everything) or the whole day, steps through earlier days, and a click on a name opens the full
/// breakdown on that player. <see cref="Refresh"/> rebuilds it (the window calls it when the ledger changed), which is cheap:
/// a day is a few thousand events at most.
/// </summary>
internal sealed class LootPanel : Border
{
    private const int FeedRows = 200;
    private static readonly string[] Views = ["Value", "Items", "Mobs", "Feed"];

    private readonly LootStore _store;
    private readonly Action<string> _viewChanged;
    private readonly Action<DateOnly, string?, LootSession?> _openBreakdown;

    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _sub = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _big = new() { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, HorizontalAlignment = HorizontalAlignment.Right, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") };
    private readonly TextBlock _caption = new() { FontSize = 10, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -2, 1, 0) };
    private readonly Button _scope = new() { Style = (Style)Ui.Theme["TextButton"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(9, 4, 9, 4) };
    private readonly TextBox _filter = new() { Style = (Style)Ui.Theme["Field"], Margin = new Thickness(0, 6, 0, 0), ToolTip = "Show only loot with this looter, item or corpse in it", Visibility = Visibility.Collapsed };
    private readonly StackPanel _body = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 10, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    private DateOnly? _picked;   // null = follow today
    private DateTime? _scopeStart;   // a session picked from the menu (by its start), when _wholeDay is false
    private bool _wholeDay;
    private string _view;
    private string _note = "";

    public LootPanel(LootStore store, string view, Action<string> viewChanged, Action<DateOnly, string?, LootSession?> openBreakdown)
    {
        _store = store;
        _viewChanged = viewChanged;
        _openBreakdown = openBreakdown;
        _view = Views.Contains(view) ? view : Views[0];
        Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x15));
        Padding = new Thickness(12, 10, 12, 8);

        var prev = Icon("", "Previous day", "Show the day before with loot.", () => Step(-1), size: 30);
        var next = Icon("", "Next day", "Show the day after (back to today).", () => Step(+1), size: 30);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(prev);
        nav.Children.Add(next);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(_title);
        titles.Children.Add(_sub);
        var bigs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        bigs.Children.Add(_big);
        bigs.Children.Add(_caption);
        var head = new DockPanel();
        DockPanel.SetDock(nav, Dock.Left);
        DockPanel.SetDock(bigs, Dock.Right);
        head.Children.Add(nav);
        head.Children.Add(bigs);
        head.Children.Add(titles);

        _scope.Click += (_, _) => OpenScopeMenu();

        var tabBar = new Border { CornerRadius = new CornerRadius(8), Background = (Brush)Ui.Theme["Subtle"], Padding = new Thickness(2), Margin = new Thickness(0, 8, 0, 0) };
        var grid = new UniformGrid { Columns = Views.Length };
        foreach (var v in Views)
        {
            var view_ = v;
            var rb = new RadioButton { Content = new TextBlock { Text = v }, Style = (Style)Ui.Theme["Segment"], GroupName = "LootView", IsChecked = v == _view };
            rb.Checked += (_, _) => { _view = view_; _viewChanged(view_); Refresh(); };
            grid.Children.Add(rb);
        }
        tabBar.Child = grid;

        _filter.TextChanged += (_, _) => Refresh();

        // The actions, as a row of buttons at the right of the session chip; each says what it does when the pointer rests on it
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(Tool("", "History", "Open the full breakdown: every day with loot, the group's coin and items, and a click on a name for that player's items and mobs.",
            () => _openBreakdown(Day, null, ActiveSession())));
        toolbar.Children.Add(Tool("", "New session", "Archive what has been looted so far (its breakdown is saved as a text file) and start again from nothing. Nothing is deleted, and it can be undone.",
            () =>
            {
                var ask = MessageBox.Show(Window.GetWindow(this), "Start a new session?\n\nWhat has been looted so far is archived and this panel starts again from nothing. Nothing is deleted, and \"Undo new session\" brings it back.",
                    "New session", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (ask == MessageBoxResult.Yes) StartSession();
            }));
        toolbar.Children.Add(Tool("", "Copy summary", "Copy the breakdown of what is shown to the clipboard as text.",
            () => Copy(LootSummary.Breakdown(Day, Events(), values: _store.Values))));
        toolbar.Children.Add(Tool("", "Open ledger folder", "Open the folder where the game keeps this character's ledger files (one a day for the character, one for the party).", OpenFolder));

        var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        foot.Children.Add(_status);

        // One row holds the session chip and the buttons; the tabs and the rows run the whole width beneath, so nothing hangs below
        // anything and no part of the panel is left empty
        _scope.Margin = new Thickness(0);
        var scopeRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(toolbar, Dock.Right);
        scopeRow.Children.Add(toolbar);
        scopeRow.Children.Add(_scope);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(scopeRow);
        stack.Children.Add(tabBar);
        stack.Children.Add(_filter);
        var root = new DockPanel();
        DockPanel.SetDock(stack, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(stack);
        root.Children.Add(foot);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _body });
        Child = root;
    }

    /// <summary>An icon button whose tooltip, in the app's dark style, names the action in bold and says what it does under it.</summary>
    private static Button Icon(string glyph, string title, string tip, Action click, double size = 26, double fontSize = 12)
    {
        var b = new Button { Content = glyph, Style = (Style)Ui.Theme["IconButton"], Width = size, Height = size, FontSize = fontSize };
        var text = new StackPanel { MaxWidth = 300 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Palette.Text });
        text.Children.Add(new TextBlock { Text = tip, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, Margin = new Thickness(0, 3, 0, 0) });
        b.ToolTip = new ToolTip { Content = text, Style = (Style)Ui.Theme["Tip"] };
        ToolTipService.SetInitialShowDelay(b, 150);
        ToolTipService.SetShowDuration(b, 30000);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A toolbar button: a larger <see cref="Icon"/> in a lighter pill, so it reads as a button.</summary>
    private static FrameworkElement Tool(string glyph, string title, string tip, Action click)
    {
        var b = Icon(glyph, title, tip, click, size: 32, fontSize: 15);
        b.Foreground = Palette.Text;
        return new Border { CornerRadius = new CornerRadius(8), Background = (Brush)Ui.Theme["Subtle"], Margin = new Thickness(6, 0, 0, 0), Child = b };
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private DateOnly Day => _picked ?? Today;

    /// <summary>Which tab is showing.</summary>
    public string View => _view;

    /// <summary>Shows a day (today follows the clock) and redraws.</summary>
    public void ShowDay(DateOnly day)
    {
        _picked = day >= Today ? null : day;
        _wholeDay = false;
        _scopeStart = null;
        Refresh();
    }

    /// <summary>Shows a line in the status bar until the next refresh.</summary>
    public void Say(string text)
    {
        _note = text;
        Refresh();
    }

    private void Step(int direction)
    {
        var days = _store.Days();
        var day = Day;
        DateOnly? target = direction < 0
            ? days.Where(d => d < day).Select(d => (DateOnly?)d).FirstOrDefault()
            : days.Where(d => d > day).Select(d => (DateOnly?)d).LastOrDefault();
        if (target is null && direction > 0 && day < Today) target = Today; // today, though it has nothing yet
        if (target is null) return;
        _picked = target >= Today ? null : target;
        _wholeDay = false;
        _scopeStart = null;
        Refresh();
    }

    // ------------------------------------------------------------------ sessions

    /// <summary>The session being shown: null for the whole day. By default today's current session, and a whole day for an earlier one.</summary>
    private LootSession? ActiveSession()
    {
        var sessions = _store.Sessions;
        if (sessions.Marks.Count == 0 || _wholeDay) return null;
        if (_scopeStart is { } start) return sessions.All.FirstOrDefault(s => s.Start == start);
        return Day == Today ? sessions.Current : null;
    }

    private IReadOnlyList<LootEvent> Events() => _store.Events(Day, ActiveSession());

    private void OpenScopeMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _scope, Placement = PlacementMode.Bottom };
        var active = ActiveSession();
        var dayEvents = _store.Events(Day);
        MenuItem Item(string header, Action click, bool check = false, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsCheckable = false, IsChecked = check, IsEnabled = enabled };
            mi.Click += (_, _) => click();
            return mi;
        }
        foreach (var s in _store.Sessions.All.Reverse())
        {
            var inside = dayEvents.Where(e => s.Contains(e.Time)).ToList();
            if (inside.Count == 0 && !(s.Open && Day == Today)) continue;
            var label = s.Label(inside.Count > 0 ? inside.Min(e => e.Time) : null) + (inside.Count > 0 ? $"  ·  {LootSummary.Totals(inside).Mobs} mobs" : "");
            var start = s.Start;
            menu.Items.Add(Item(s.Open ? "This session: " + label : "Session " + label, () => { _wholeDay = false; _scopeStart = start; Refresh(); }, check: active is not null && active.Start == s.Start));
        }
        menu.Items.Add(Item("Whole day", () => { _wholeDay = true; _scopeStart = null; Refresh(); }, check: active is null));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("New session: archive this one and start again", StartSession));
        menu.Items.Add(Item("Undo the last new session (merge it back)", UndoSession, enabled: _store.Sessions.Marks.Count > 0));
        menu.IsOpen = true;
    }

    private void StartSession()
    {
        var closed = _store.NewSession("manual");
        _note = closed is null ? "Nothing looted in this session yet: nothing to archive." : "Session archived (a text file in the sessions folder). Starting from nothing.";
        _picked = null;
        _wholeDay = false;
        _scopeStart = null;
        Refresh();
    }

    private void UndoSession()
    {
        if (_store.Sessions.RemoveLast()) _note = "The last session is merged back into the one before.";
        _wholeDay = false;
        _scopeStart = null;
        Refresh();
    }

    // ------------------------------------------------------------------ showing a day

    public void Refresh()
    {
        var day = Day;
        var session = ActiveSession();
        var all = _store.Events(day, session);
        var d = LootDay.Of(day, all, _store.Values);
        var totals = d.Totals;

        _title.Text = day == Today ? $"Today · {day.ToString("ddd d MMM", CultureInfo.CurrentCulture)}" : day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);
        _sub.Text = $"{Plural(totals.Mobs, "mob")} · {Plural(totals.Items, "item")} · {Plural(d.Members.Count, "looter")}";
        _big.Text = LootSummary.Worth(d.Total, d.Approximate, d.Unpriced > 0);
        _big.ToolTip = $"Coin {LootSummary.Coin(totals.Copper)} plus what a vendor would pay for the items: {LootSummary.Worth(d.ItemValue, d.Approximate, d.Unpriced > 0)}" + Environment.NewLine +
                       $"{d.Priced:N0} items priced from your vendor sales, {d.Estimated:N0} valued by their family (~), {d.Unpriced:N0} with no known price (not counted, +)" +
                       (totals.Share > 0 ? Environment.NewLine + $"{LootSummary.Coin(totals.Share)} of the purses reached you as splits" : "");
        _caption.Text = $"coin {LootSummary.Coin(totals.Copper)} + items {LootSummary.Worth(d.ItemValue, d.Approximate, false)}";
        var first = all.Count > 0 ? all.Min(e => e.Time) : (DateTime?)null;
        _scope.Content = (session is null ? "Whole day" : session.Open ? "This session: " + session.Label(first) : "Session " + session.Label(first)) + "  ▾";
        _scope.ToolTip = "Which part of the day to show: this session, an earlier one, or the whole day. A new session archives what has been looted so far.";

        _status.Text = _store.Selected is null ? ""
            : _store.Error is { } error ? $"Can't read the ledger: {error}"
            : _note.Length > 0 ? _note
            : d.Unpriced > 0 ? $"{Plural(d.Unpriced, "item")} unpriced (+): sell one to price it"
            : "";
        _note = "";

        _filter.Visibility = _view == "Feed" ? Visibility.Visible : Visibility.Collapsed;
        _body.Children.Clear();
        if (_store.Selected is null)
        {
            _body.Children.Add(Line("No Monsters and Memories ledger found.", Palette.Text));
            _body.Children.Add(Line($"Looked in {_store.LedgerRoot}. Use the ⓘ menu in the title bar to choose where the game keeps it.", Palette.Muted));
            return;
        }
        if (_store.Selected is { HasLedger: false } fresh && _store.FileCount == 0)
        {
            _body.Children.Add(Line($"No ledger yet for {fresh.Character} on {fresh.ServerName}.", Palette.Text));
            _body.Children.Add(Line("The game writes one as this character loots, sells, levels up and so on; this window picks it up by itself.", Palette.Muted));
            return;
        }
        if (d.Events.Count == 0)
        {
            var earlier = _store.Events(day);
            _body.Children.Add(Line(session is { Open: true } ? "Nothing looted yet this session." : day == Today && session is null ? "Nothing looted yet today." : "Nothing looted then.", Palette.Muted));
            if (session is not null && earlier.Count > 0)
            {
                // A new session starts from nothing, which looks like the loot is gone: say where it is
                _body.Children.Add(Line($"Earlier {(day == Today ? "today" : "that day")} there were {Plural(LootSummary.Totals(earlier).Mobs, "mob")} looted. They are in an earlier session.", Palette.Muted));
                var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
                actions.Children.Add(TextAction("Show the whole day", () => { _wholeDay = true; _scopeStart = null; Refresh(); }));
                if (_store.Sessions.Marks.Count > 0) actions.Children.Add(TextAction("Undo new session", UndoSession));
                _body.Children.Add(actions);
            }
            return;
        }
        switch (_view)
        {
            case "Items": ShowItems(d); break;
            case "Mobs": ShowMobs(d); break;
            case "Feed": ShowFeed(d); break;
            default: ShowValue(d); break;
        }
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n:N0} {word}s";

    private static Brush BrushOf(LootMember m) => Palette.For(m.Name, m.Class.Length > 0 ? m.Class : null);

    private void ShowValue(LootDay d)
    {
        _body.Children.Add(CopyBars(
            ("Copy loot counts", "each looter's name and number of loots on one line", () => LootSummary.LootCounts(d.Members)),
            ("Copy summary", "the summary", () => LootSummary.Breakdown(Day, Events(), values: _store.Values))));
        bool valueless = d.Members.All(m => m.Total == 0);
        double top = Math.Max(1, valueless ? d.Members.Max(m => m.Mobs) : d.Members.Max(m => (double)m.Total));
        int rank = 0;
        foreach (var m in d.Members)
        {
            var member = m;
            var tip = string.Join(Environment.NewLine, new[]
            {
                m.ClassLevel.Length > 0 ? m.ClassLevel : "", $"{Plural(m.Mobs, "mob")}, {Plural(m.Items, "item")}",
                $"coin {LootSummary.Coin(m.Copper)} + items {LootSummary.Worth(m.ItemValue, m.Estimated > 0, false)}",
                m.Unpriced > 0 ? $"{Plural(m.Unpriced, "item")} with no known price are not counted" : "",
                m.Estimated > 0 ? $"{Plural(m.Estimated, "item")} valued by their family (~)" : "",
                m.Mobs > 0 ? $"{LootSummary.Coin((long)m.CopperPerMob)} coin a mob, {m.ItemsPerMob:0.0} items a mob" : "",
                m.Split > 0 ? $"{LootSummary.Coin(m.Split)} of their purses reached you" : "", "Click for their items and mobs",
            }.Where(s => s.Length > 0));
            _body.Children.Add(Row(++rank, m.Name, $"{m.Mobs} mobs · {m.Items} items", LootSummary.Worth(m.Total, m.Estimated > 0, m.Unpriced > 0), BrushOf(m),
                (valueless ? m.Mobs : m.Total) / top, tip, () => _openBreakdown(Day, member.Name, ActiveSession())));
        }
    }

    private void ShowItems(LootDay d)
    {
        if (d.ItemsWorth.Count == 0) { _body.Children.Add(Line("No items looted.", Palette.Muted)); return; }
        double top = Math.Max(1, d.ItemsWorth.Max(i => (double)i.Total));
        int rank = 0;
        foreach (var i in d.ItemsWorth.Take(60))
        {
            var takers = string.Join(", ", i.By.Take(2).Select(b => b.Count > 1 ? $"{b.Who} {b.Count}" : b.Who)) + (i.By.Count > 2 ? $" +{i.By.Count - 2}" : "");
            var value = !i.Value.Known ? "—" : (i.Value.Estimated ? "~" : "") + LootSummary.Coin(i.Total);
            var tip = (i.Value.Known
                          ? $"{(i.Value.Estimated ? "about " : "")}{LootSummary.Coin(i.Value.Each)} each to a vendor ({SourceWord(i.Value.Source)})" + Environment.NewLine
                          : "No known price: no vendor sale of it in your ledgers." + Environment.NewLine + "Sell one to a vendor and its price is learned." + Environment.NewLine)
                      + string.Join(Environment.NewLine, i.By.Select(b => $"{b.Who}: {b.Count}"));
            var item = i;
            _body.Children.Add(Row(++rank, i.Count > 1 ? $"{i.Item} ×{i.Count}" : i.Item, takers, value, Palette.You, i.Total / top, tip, () => _openBreakdown(Day, null, ActiveSession()),
                copy: () => LootSummary.ItemLine(item)));
        }
        if (d.ItemsWorth.Count > 60) _body.Children.Add(Line($"{d.ItemsWorth.Count - 60} more kinds are in the full breakdown.", Palette.Muted));
    }

    private static string SourceWord(Loot.ValueSource s) => s switch
    {
        Loot.ValueSource.Sold => "what vendors paid you",
        Loot.ValueSource.Estimated => "from items of the same family",
        _ => "unknown",
    };

    private void ShowMobs(LootDay d)
    {
        if (d.Mobs.Count == 0) { _body.Children.Add(Line("No mobs looted.", Palette.Muted)); return; }
        double top = Math.Max(1, d.Mobs.Max(m => m.Looted));
        int rank = 0;
        foreach (var m in d.Mobs.Take(60))
        {
            var tip = $"{m.ItemsEach:0.0} items each · looted most by {m.TopLooter}";
            _body.Children.Add(Row(++rank, m.Mob, m.Copper > 0 ? $"{LootSummary.Coin(m.Copper / m.Looted)} each" : "", m.Looted.ToString("N0"), Palette.Taken, m.Looted / top,
                tip, () => _openBreakdown(Day, null, ActiveSession())));
        }
        if (d.Mobs.Count > 60) _body.Children.Add(Line($"{d.Mobs.Count - 60} more kinds are in the full breakdown.", Palette.Muted));
    }

    private void ShowFeed(LootDay d)
    {
        var f = _filter.Text.Trim();
        bool Has(string s) => s.Contains(f, StringComparison.OrdinalIgnoreCase);
        var events = d.Events.Where(e => f.Length == 0 || Has(e.Who) || Has(e.Item) || Has(e.Source)).ToList();
        if (events.Count == 0) { _body.Children.Add(Line("Nothing matches the filter.", Palette.Muted)); return; }
        foreach (var e in Enumerable.Reverse(events).Take(FeedRows))
        {
            var row = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 2), ToolTip = Tip(e) };
            row.Inlines.Add(new Run(e.Time.ToString("HH:mm:ss") + "  ") { Foreground = Palette.Muted });
            row.Inlines.Add(new Run(e.Who + "  ") { Foreground = e.You ? Palette.You : Palette.Text, FontWeight = e.You ? FontWeights.SemiBold : FontWeights.Normal });
            if (e.Kind == LootKind.Item) row.Inlines.Add(new Run($"[{e.Item}]" + (e.Count > 1 ? $" ×{e.Count}" : "")) { Foreground = Palette.Gold });
            else row.Inlines.Add(new Run(LootSummary.Coin(e.Copper)) { Foreground = Palette.Heal });
            row.Inlines.Add(new Run($"  ← {e.Source}") { Foreground = Palette.Muted });
            if (e.Kind == LootKind.Coin && e.Split > 0) row.Inlines.Add(new Run($"  (your split {LootSummary.Coin(e.Split)})") { Foreground = Palette.Muted });
            _body.Children.Add(row);
        }
        if (events.Count > FeedRows) _body.Children.Add(Line($"{events.Count - FeedRows} older lines are in the ledger.", Palette.Muted));
    }

    private static string Tip(LootEvent e) =>
        string.Join("\n", new[]
        {
            LootSummary.Describe(e), e.Zone.Length > 0 ? $"zone: {e.Zone}" : "",
            string.Join(" ", new[] { e.Class.Length > 0 ? GameClasses.NameOf(e.Class) : "", e.Level > 0 ? e.Level.ToString() : "" }.Where(s => s.Length > 0)) is { Length: > 0 } cl ? $"looter: {cl}" : "",
        }.Where(s => s.Length > 0));

    /// <summary>One ranked row in the meter's style: a bar behind it sized to its value, a stripe in the row's colour, rank, name, a muted note and the value.</summary>
    private static FrameworkElement Row(int rank, string name, string sub, string value, Brush brush, double fraction, string tip, Action click, Func<string>? copy = null)
    {
        var grid = new Grid { Height = 28, Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var back = new Border { CornerRadius = new CornerRadius(7), Background = (Brush)Ui.Theme["Subtle"] };
        grid.Children.Add(back);
        var fill = new Grid();
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.0001), GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fraction, 0.0001), GridUnitType.Star) });
        fill.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = brush, Opacity = 0.28 });
        grid.Children.Add(fill);
        grid.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = brush, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) });

        var dock = new DockPanel { Margin = new Thickness(11, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);
        right.Children.Add(new TextBlock { Text = sub, Foreground = Palette.Muted, FontSize = 11, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        right.Children.Add(new TextBlock { Text = value, Foreground = Palette.Text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, MinWidth = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        dock.Children.Add(right);
        if (copy != null)
        {
            // At the left of the row, before the rank
            var glyph = CopyGlyph("Copy this item's loot summary (who looted it and how many) to the clipboard", copy);
            DockPanel.SetDock(glyph, Dock.Left);
            dock.Children.Add(glyph);
        }
        dock.Children.Add(new TextBlock { Text = rank.ToString(), Foreground = Palette.Muted, FontSize = 11, Width = 16, VerticalAlignment = VerticalAlignment.Center });
        dock.Children.Add(new TextBlock { Text = name, Foreground = Palette.Text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(dock);

        if (tip.Length > 0) grid.ToolTip = new ToolTip { Content = new TextBlock { Text = tip, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 }, Style = (Style)Ui.Theme["Tip"] };
        grid.MouseEnter += (_, _) => back.Background = (Brush)Ui.Theme["Hover"];
        grid.MouseLeave += (_, _) => back.Background = (Brush)Ui.Theme["Subtle"];
        grid.MouseLeftButtonUp += (_, _) => click();
        return grid;
    }

    /// <summary>A small copy button for a row: copies <paramref name="text"/>() and shows a tick for a moment.</summary>
    private static Button CopyGlyph(string tip, Func<string> text)
    {
        var b = new Button { Content = "", ToolTip = tip, Style = (Style)Ui.Theme["IconButton"], Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 11, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        b.Click += (_, e) =>
        {
            e.Handled = true;
            Copy(text());
            b.Content = "";
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            t.Tick += (_, _) => { t.Stop(); b.Content = ""; };
            t.Start();
        };
        return b;
    }

    /// <summary>A "Copy ..." button at the top of a tab: copies the whole tab as text and says so in the status line.</summary>
    private FrameworkElement CopyBar(string label, string what, Func<string> text)
    {
        var b = new Button { Content = label, Style = (Style)Ui.Theme["TextButton"], HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(9, 3, 9, 3), ToolTip = "Copy " + what + " to the clipboard as text" };
        b.Click += (_, _) => { Copy(text()); _status.Text = $"Copied {what} to the clipboard."; };
        return b;
    }

    /// <summary>Several <see cref="CopyBar"/> buttons side by side at the right.</summary>
    private FrameworkElement CopyBars(params (string Label, string What, Func<string> Text)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, what, text) in buttons)
        {
            var b = (Button)CopyBar(label, what, text);
            b.Margin = new Thickness(4, 0, 0, 2);
            row.Children.Add(b);
        }
        return row;
    }

    private static Button TextAction(string label, Action click)
    {
        var b = new Button { Content = label, Style = (Style)Ui.Theme["TextButton"], Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(11, 6, 11, 6) };
        b.Click += (_, _) => click();
        return b;
    }

    private static TextBlock Line(string text, Brush brush) =>
        new() { Text = text, Foreground = brush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 2, 2), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };

    // ------------------------------------------------------------------ buttons

    private static void Copy(string text) => ClipboardText.Copy(text);

    private void OpenFolder()
    {
        try
        {
            if (_store.LedgerPath is { } path && Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    }
}
