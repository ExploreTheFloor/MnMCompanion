using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MnMCompanion.Data;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion;

/// <summary>What the History window needs of any of its pages.</summary>
internal interface IHistoryPage
{
    /// <summary>Looks again if the character or the saved history changed since the page was drawn.</summary>
    void Tick();

    /// <summary>Reads and draws now, on this thread (for a picture).</summary>
    void RefreshNow();

    /// <summary>Opens one of the page's tabs by name.</summary>
    void PickTab(string tab);
}

/// <summary>
/// A page of the History window that reads one of the event tables: the same layout as the Loot page's detail pane (a heading
/// with a big number, tiles, tabs with copy buttons, the table), and the same handling of the shared period and filter. A page
/// supplies <see cref="Query"/> (run off the UI thread) and <see cref="Render"/>.
/// </summary>
internal abstract class HistoryPage<T> : UserControl, IHistoryPage where T : class
{
    private const int LogRows = 400;

    protected readonly LootStore Store;
    protected readonly HistoryData Data;
    protected readonly HistoryView View;

    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _detail = new() { Margin = new Thickness(28, 20, 28, 28) };
    private readonly Border _swatch = new() { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = new() { FontSize = 26, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), Foreground = Palette.Text };
    private readonly TextBlock _sub = new() { FontSize = 12, Foreground = Palette.Muted, Margin = new Thickness(22, 2, 0, 0) };
    private readonly TextBlock _big = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), Foreground = Palette.Text };
    private readonly TextBlock _bigCaption = new() { FontSize = 11, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -4, 0, 0) };
    private readonly WrapPanel _tiles = new() { Margin = new Thickness(0, 0, -10, 6) };
    private readonly StackPanel _copyBar = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _tabBar = new() { Orientation = Orientation.Horizontal };
    private readonly ContentControl _tabContent = new();
    private readonly Border _tabHost;
    private readonly DockPanel _tabRow;
    private readonly TextBlock _message = new() { FontSize = 13, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(32, 28, 32, 0), Visibility = Visibility.Collapsed };

    private string _tab = "";
    private T? _data;
    private string _who = "";
    private long _seen = -1;
    private Period? _shownPeriod;
    private int _generation;
    private readonly string _emptyHint;

    protected HistoryPage(LootStore store, HistoryData data, HistoryView view, string emptyHint)
    {
        Store = store;
        Data = data;
        View = view;
        _emptyHint = emptyHint;
        Background = (Brush)Ui.Theme["Window"];

        var head = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(_swatch);
        title.Children.Add(_name);
        left.Children.Add(title);
        left.Children.Add(_sub);
        head.Children.Add(left);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_big);
        right.Children.Add(_bigCaption);
        Grid.SetColumn(right, 1);
        head.Children.Add(right);

        _tabHost = new Border { CornerRadius = new CornerRadius(8), Background = (Brush)Ui.Theme["Subtle"], Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Child = _tabBar };
        _tabRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(_copyBar, Dock.Right);
        _tabRow.Children.Add(_copyBar);
        _tabRow.Children.Add(_tabHost);

        _detail.Children.Add(head);
        _detail.Children.Add(_tiles);
        _detail.Children.Add(_tabRow);
        _detail.Children.Add(_tabContent);
        _scroll.Content = _detail;

        var root = new Grid();
        root.Children.Add(_scroll);
        root.Children.Add(_message);
        Content = root;

        Loaded += (_, _) =>
        {
            View.Changed += OnViewChanged;
            Refresh();
        };
        Unloaded += (_, _) => View.Changed -= OnViewChanged;
    }

    protected abstract T Query(HistoryStore store, string server, string character, Period period);

    protected abstract void Render(T data);

    // ------------------------------------------------------------------ reading

    public void Tick()
    {
        var who = Store.Selected is { } s ? s.Server + "/" + s.Character : "";
        if (who != _who || Data.Version != _seen) Refresh();
    }

    public void PickTab(string tab)
    {
        _tab = tab;
        Redraw();
    }

    private void OnViewChanged()
    {
        if (_data is not null && View.Period == _shownPeriod) Redraw();   // only the filter changed: no need to ask again
        else Refresh();
    }

    private (HistoryStore Store, string Server, string Character, Period Period, long Version, string Who)? Capture()
    {
        if (Store.Selected is not { } who) { Say("Pick a character (the name at the top left of the small window) to see its history."); return null; }
        if (Data.Store is not { } store) { Say("The saved history isn't available: " + Data.Status); return null; }
        return (store, who.Server, who.Character, View.Period, Data.Version, who.Server + "/" + who.Character);
    }

    public async void Refresh()
    {
        var generation = ++_generation;
        if (Capture() is not { } c) return;
        try
        {
            var data = await Task.Run(() => Query(c.Store, c.Server, c.Character, c.Period));
            if (generation == _generation) Show(data, c.Period, c.Version, c.Who);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (generation == _generation) Say("Can't read the saved history: " + ex.Message);
        }
    }

    public void RefreshNow()
    {
        ++_generation;
        if (Capture() is not { } c) return;
        Show(Query(c.Store, c.Server, c.Character, c.Period), c.Period, c.Version, c.Who);
    }

    private void Show(T data, Period period, long version, string who)
    {
        _data = data;
        _shownPeriod = period;
        _seen = version;
        _who = who;
        _message.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;
        Render(data);
    }

    private void Say(string text)
    {
        _data = null;
        _message.Text = text;
        _message.Visibility = Visibility.Visible;
        _scroll.Visibility = Visibility.Collapsed;
    }

    private void Redraw()
    {
        if (_data is null) return;
        var offset = _scroll.VerticalOffset;
        Render(_data);
        Dispatcher.BeginInvoke(() => _scroll.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
    }

    /// <summary>After a table is re-sorted: draw it again, staying where the reader was.</summary>
    protected void Rebuild() => Redraw();

    // ------------------------------------------------------------------ pieces for a page's Render

    protected void Head(string name, string sub, string big, string caption, Brush? swatch = null)
    {
        _swatch.Background = swatch ?? Palette.Muted;
        _name.Text = name;
        _sub.Text = sub;
        _big.Text = big;
        _bigCaption.Text = caption;
        _tiles.Children.Clear();
    }

    protected void AddTile(string label, string value, string sub, Brush? accent = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Palette.Muted });
        stack.Children.Add(new TextBlock
        {
            Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = accent ?? Palette.Text, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), ToolTip = value,
        });
        stack.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = sub });
        _tiles.Children.Add(new Border { Style = (Style)Ui.Theme["Tile"], Child = stack });
    }

    /// <summary>A page with nothing to show for the period: the heading, no tiles or tabs, and a line saying what would appear here.</summary>
    protected void Empty(string name, string sub)
    {
        Head(name, sub, "", "");
        _tabHost.Visibility = Visibility.Collapsed;
        _copyBar.Children.Clear();
        _tabContent.Content = new TextBlock { Text = _emptyHint, FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Margin = new Thickness(8, 6, 0, 0) };
    }

    protected void Tabs(IReadOnlyList<string> tabs, Func<string, FrameworkElement> content, Func<string, (string Label, Func<string> Text)[]>? copies = null)
    {
        _tabHost.Visibility = tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;   // one tab needs no tab bar
        if (!tabs.Contains(_tab)) _tab = tabs[0];
        void FillCopies(string tab)
        {
            _copyBar.Children.Clear();
            foreach (var (label, text) in copies?.Invoke(tab) ?? [])
            {
                var b = new Button { Content = label, Style = (Style)Ui.Theme["TextButton"], Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), ToolTip = "Copy to the clipboard as text" };
                var shown = label;
                b.Click += (_, _) =>
                {
                    b.Content = ClipboardText.Copy(text()) ? "Copied" : "Clipboard busy";
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                    t.Tick += (_, _) => { t.Stop(); b.Content = shown; };
                    t.Start();
                };
                _copyBar.Children.Add(b);
            }
        }
        _tabBar.Children.Clear();
        foreach (var name in tabs)
        {
            var tab = name;
            var rb = new RadioButton
            {
                Content = new TextBlock { Text = tab, Margin = new Thickness(14, 0, 14, 0) }, Style = (Style)Ui.Theme["Segment"],
                GroupName = "Tab" + GetHashCode(), IsChecked = tab == _tab,
            };
            rb.Checked += (_, _) =>
            {
                _tab = tab;
                _tabContent.Content = content(tab);
                FillCopies(tab);
            };
            _tabBar.Children.Add(rb);
        }
        _tabContent.Content = content(_tab);
        FillCopies(_tab);
    }

    /// <summary>How a time reads in a log: just the clock inside one day, with the date when the period is longer.</summary>
    protected string When(DateTime time) =>
        View.Period.From is { } from && View.Period.To is { } to && to - from <= TimeSpan.FromDays(1) ? time.ToString("HH:mm:ss", CultureInfo.CurrentCulture) : time.ToString("MMM d  HH:mm", CultureInfo.CurrentCulture);

    /// <summary>The line under the heading: the period, and the filter when there is one.</summary>
    protected string Scope(string what) => View.Period.Label + " · " + what + (View.Filter.Length > 0 ? $" · matching “{View.Filter}”" : "");

    protected static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n:N0} {word}s";

    protected static string Signed(long copper) => (copper < 0 ? "−" : copper > 0 ? "+" : "") + LootSummary.Coin(Math.Abs(copper));

    protected static string Duration(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{(int)t.TotalMinutes}m";

    protected static string LogNote(int shown, int all) =>
        all > shown ? $"The latest {shown:N0} of {all:N0} lines (the rest are in the saved copy)" : Plural(all, "line");

    protected int Rows => LogRows;

    protected FrameworkElement WithNote(string note, FrameworkElement table)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = note, FontSize = 11.5, Foreground = Palette.Muted, Margin = new Thickness(8, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(table);
        return panel;
    }
}
