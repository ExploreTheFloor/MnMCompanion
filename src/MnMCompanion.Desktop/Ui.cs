using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using MnMCompanion.Loot;

namespace MnMCompanion;

/// <summary>The theme's brushes (Theme.axaml), for code that builds its controls itself, and a few helpers every window uses.</summary>
public static class Ui
{
    public static readonly IBrush Stroke = Palette.Freeze(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    public static readonly IBrush Subtle = Palette.Freeze(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
    public static readonly IBrush Hover = Palette.Freeze(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    public static readonly IBrush Window = Palette.Freeze(Color.FromRgb(0x0F, 0x11, 0x15));
    public static readonly IBrush Panel = Palette.Freeze(Color.FromRgb(0x16, 0x19, 0x20));
    public static readonly IBrush Selected = Palette.Freeze(Color.FromArgb(0x2A, 0x5B, 0x9D, 0xFF));
    public static readonly IBrush Transparent = Brushes.Transparent;

    /// <summary>The icon font (Lucide, carried inside the program), for the pictures on buttons.</summary>
    public static readonly FontFamily Icons = new("avares://MnMCompanion/Assets#lucide");

    /// <summary>The font for words (Inter, carried inside the program, so it looks the same on every system).</summary>
    public static readonly FontFamily Text = new("fonts:Inter#Inter");

    public static readonly Cursor Hand = new(StandardCursorType.Hand);

    /// <summary>Gives a control the theme's style of that name (a class in Theme.axaml).</summary>
    public static T Styled<T>(this T control, params string[] classes) where T : StyledElement
    {
        foreach (var c in classes) control.Classes.Add(c);
        return control;
    }

    /// <summary>Sets a tooltip: words are wrapped, anything else is shown as it is. Null takes it away.</summary>
    public static T Tip<T>(this T control, object? tip, int delay = 400) where T : Control
    {
        ToolTip.SetTip(control, tip is string s ? new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 } : tip);
        ToolTip.SetShowDelay(control, delay);
        return control;
    }

    /// <summary>Whether a control is on show: in a window, and neither it nor anything it is in hidden (what WPF calls IsVisible).</summary>
    public static bool OnScreen(this Control control) => control.IsEffectivelyVisible && TopLevel.GetTopLevel(control) is not null;

    /// <summary>The program's main window (the small one), for the clipboard, the file pickers and opening links.</summary>
    public static Window? MainWindow => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>Opens a web page, a folder or a file with whatever the desktop opens it with (the browser, the file manager).</summary>
    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { }
    }

    /// <summary>Runs <paramref name="action"/> once after <paramref name="after"/>, on the window's thread.</summary>
    public static void Later(TimeSpan after, Action action) => DispatcherTimer.RunOnce(action, after);
}

/// <summary>The pictures on the buttons: characters of the icon font (<see cref="Ui.Icons"/>).</summary>
public static class Glyphs
{
    public const string Copy = "";
    public const string Check = "";
    public const string Pin = "";
    public const string Close = "";
    public const string Info = "";
    public const string History = "";
    public const string Timer = "";
    public const string Grid = "";
    public const string Refresh = "";
    public const string Previous = "";
    public const string Next = "";
    public const string Play = "";
    public const string Stop = "";
    public const string Delete = "";
    public const string External = "";
}

/// <summary>Colours used for people and things, the same everywhere.</summary>
public static class Palette
{
    public static readonly Color AccentColor = Color.FromRgb(0x5B, 0x9D, 0xFF);
    public static readonly Color DangerColor = Color.FromRgb(0xFF, 0x6B, 0x6B);
    public static readonly Color HealColor = Color.FromRgb(0x3D, 0xD6, 0x8C);
    public static readonly Color MutedColor = Color.FromRgb(0x8B, 0x93, 0xA1);

    public static readonly IBrush You = Freeze(AccentColor);
    public static readonly IBrush Taken = Freeze(DangerColor);
    public static readonly IBrush Heal = Freeze(HealColor);
    public static readonly IBrush Gold = Freeze(Color.FromRgb(0xF5, 0xC8, 0x6B));
    public static readonly IBrush Text = Freeze(Color.FromRgb(0xF2, 0xF4, 0xF7));
    public static readonly IBrush Muted = Freeze(MutedColor);

    private static readonly IBrush[] Others =
    [
        Freeze(Color.FromRgb(0xB3, 0x8B, 0xFA)), Freeze(Color.FromRgb(0xF5, 0xA6, 0x23)), Freeze(Color.FromRgb(0x2D, 0xD4, 0xBF)),
        Freeze(Color.FromRgb(0xF4, 0x72, 0xB6)), Freeze(Color.FromRgb(0xA3, 0xE6, 0x35)), Freeze(Color.FromRgb(0x94, 0xA3, 0xB8)),
    ];

    /// <summary>A colour per class.</summary>
    private static readonly Dictionary<string, IBrush> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Archer"] = Freeze(Color.FromRgb(0xA3, 0xE6, 0x35)),
        ["Bard"] = Freeze(Color.FromRgb(0xF4, 0x72, 0xB6)),
        ["Beastmaster"] = Freeze(Color.FromRgb(0xC8, 0xA2, 0x7A)),
        ["Cleric"] = Freeze(Color.FromRgb(0xF5, 0xF5, 0xF4)),
        ["Druid"] = Freeze(Color.FromRgb(0xFB, 0x92, 0x3C)),
        ["Elementalist"] = Freeze(Color.FromRgb(0x38, 0xBD, 0xF8)),
        ["Enchanter"] = Freeze(Color.FromRgb(0xC0, 0x84, 0xFC)),
        ["Fighter"] = Freeze(Color.FromRgb(0xB4, 0x53, 0x09)),
        ["Inquisitor"] = Freeze(Color.FromRgb(0xEA, 0xB3, 0x08)),
        ["Monk"] = Freeze(Color.FromRgb(0x2D, 0xD4, 0xBF)),
        ["Necromancer"] = Freeze(Color.FromRgb(0x8B, 0x5C, 0xF6)),
        ["Paladin"] = Freeze(Color.FromRgb(0xFD, 0xE6, 0x8A)),
        ["Ranger"] = Freeze(Color.FromRgb(0x22, 0xC5, 0x5E)),
        ["Rogue"] = Freeze(Color.FromRgb(0x94, 0xA3, 0xB8)),
        ["Shadow Knight"] = Freeze(Color.FromRgb(0xDC, 0x26, 0x26)),
        ["Shaman"] = Freeze(Color.FromRgb(0x3B, 0x82, 0xF6)),
        ["Spellblade"] = Freeze(Color.FromRgb(0x67, 0xE8, 0xF9)),
        ["Wizard"] = Freeze(Color.FromRgb(0x81, 0x8C, 0xF8)),
    };

    /// <summary>A stable colour per person: by class when known, else one from a fixed set picked by their name.</summary>
    public static IBrush For(string name, string? cls = null)
    {
        if (cls is not null && Classes.TryGetValue(cls, out var byClass)) return byClass;
        uint h = 2166136261;
        foreach (char c in name) h = (h ^ char.ToLowerInvariant(c)) * 16777619;
        return Others[h % (uint)Others.Length];
    }

    public static IBrush Freeze(Color c) => new ImmutableSolidColorBrush(c);
}

public static class Fmt
{
    public static string Pct(double f) => $"{f * 100:0.#}%";
}

/// <summary>A column of a <see cref="Table"/>. Width 0 = takes the remaining space.</summary>
public sealed record Col<T>(string Header, double Width, Func<T, string> Text, Func<T, IComparable>? Sort = null, bool Right = true,
    string? Tip = null);

/// <summary>
/// Builds a sortable table: click a header to sort by it (again to flip). The first column can carry a share bar
/// in the row's colour.
/// </summary>
public static class Table
{
    private static readonly Dictionary<string, (int Col, bool Desc)> SortState = [];

    public static Control Build<T>(string id, IEnumerable<T> rows, IReadOnlyList<Col<T>> cols, Action rebuild,
        Func<T, double>? bar = null, Func<T, IBrush>? color = null, Action<T>? click = null, int defaultSort = 1,
        Func<T, string?>? rowTip = null, Func<T, string>? copy = null)
    {
        var (sortCol, desc) = SortState.GetValueOrDefault(id, (defaultSort, true));
        var list = rows.ToList();
        if (cols[sortCol].Sort is { } key)
            list = desc ? list.OrderByDescending(key).ToList() : list.OrderBy(key).ToList();

        var panel = new StackPanel();
        panel.Children.Add(Row(cols.Select((c, i) =>
        {
            var arrow = i == sortCol ? (desc ? " ↓" : " ↑") : "";
            var b = new Button
            {
                Content = c.Header + arrow,
                HorizontalContentAlignment = c.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                IsEnabled = c.Sort != null,
            }.Styled("header");
            if (c.Tip is not null) b.Tip(c.Tip);
            int ci = i;
            b.Click += (_, _) =>
            {
                SortState[id] = (ci, ci == sortCol ? !desc : c.Right);
                rebuild();
            };
            return (Control)b;
        }).ToList(), cols.Select(c => c.Width).ToList()));
        panel.Children.Add(new Border { Height = 1, Background = Ui.Stroke });

        if (list.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Nothing here.", Foreground = Palette.Muted, Margin = new Thickness(8, 10, 0, 6), FontSize = 12 });
            return panel;
        }

        double maxBar = bar is null ? 0 : list.Max(bar);
        foreach (var item in list)
        {
            var cells = cols.Select((c, i) => (Control)new TextBlock
            {
                Text = c.Text(item),
                Foreground = Palette.Text,
                FontWeight = i == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                FontSize = 12.5,
                TextAlignment = c.Right ? TextAlignment.Right : TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            }).ToList();
            var grid = Row(cells, cols.Select(c => c.Width).ToList());
            grid.Height = 32;

            if (bar != null && maxBar > 0)
            {
                var brush = color?.Invoke(item) ?? Palette.You;
                double frac = bar(item) / maxBar;
                var fill = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(frac, 0.0001), GridUnitType.Star) });
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - frac, 0.0001), GridUnitType.Star) });
                fill.Children.Add(new Border { Background = brush, Opacity = 0.22, CornerRadius = new CornerRadius(5) });
                fill.Children.Add(new Border { Background = brush, Width = 3, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 3, 0, 3) });
                Grid.SetColumnSpan(fill, cols.Count);
                grid.Children.Insert(0, fill);
            }

            var rowBorder = new Border { Child = grid, CornerRadius = new CornerRadius(6), Background = Ui.Transparent };
            if (copy != null)
            {
                // A small copy button at the right end of the row, there while the pointer is on it
                var row = item;
                var cb = new Button { Content = Glyphs.Copy, Width = 24, Height = 24, FontSize = 11, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }
                    .Styled("icon").Tip("Copy this line to the clipboard");
                Grid.SetColumnSpan(cb, cols.Count);
                cb.Click += (_, e) =>
                {
                    e.Handled = true;
                    ClipboardText.Copy(copy(row));
                    cb.Content = Glyphs.Check;
                    Ui.Later(TimeSpan.FromMilliseconds(1200), () => cb.Content = Glyphs.Copy);
                };
                grid.Children.Add(cb);
                rowBorder.PointerEntered += (_, _) => cb.IsVisible = true;
                rowBorder.PointerExited += (_, _) => cb.IsVisible = false;
            }
            if (rowTip?.Invoke(item) is { Length: > 0 } tip) rowBorder.Tip(tip, 300);
            rowBorder.PointerEntered += (_, _) => rowBorder.Background = Ui.Subtle;
            rowBorder.PointerExited += (_, _) => rowBorder.Background = Ui.Transparent;
            if (click != null)
            {
                rowBorder.Cursor = Ui.Hand;
                rowBorder.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) click(item); };
            }
            panel.Children.Add(rowBorder);
        }
        return panel;
    }

    /// <summary>
    /// A table with a search box above it: typing narrows the rows to those whose name has every word typed (<see cref="NameSearch"/>),
    /// after a short pause in the typing. The box stays where it is while the table under it is drawn again. The text is kept by the
    /// caller (<paramref name="get"/> and <paramref name="set"/>), so sorting, or leaving the tab and coming back, doesn't empty it;
    /// Esc clears it. With no rows at all there is nothing to search, so no box.
    /// </summary>
    public static Control Searchable<T>(IReadOnlyList<T> rows, Func<T, string> name, string hint, Func<string> get, Action<string> set,
        Func<IReadOnlyList<T>, Control> table)
    {
        if (rows.Count == 0) return table(rows);

        var body = new ContentControl();
        var count = new TextBlock { FontSize = 11.5, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var field = new TextBox { Text = get(), PlaceholderText = hint, Width = 300 }.Styled("field").Tip("Show only the ones with these words in the name (Esc clears)");

        void Show()
        {
            var query = get().Trim();
            var shown = NameSearch.Filter(rows, name, query);
            count.Text = query.Length == 0 ? "" : $"{shown.Count:N0} of {rows.Count:N0}";
            body.Content = shown.Count > 0 ? table(shown)
                : new TextBlock { Text = $"Nothing matches “{query}”.", Foreground = Palette.Muted, Margin = new Thickness(8, 10, 0, 6), FontSize = 12 };
        }

        var typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        typing.Tick += (_, _) => { typing.Stop(); Show(); };
        field.TextChanged += (_, _) =>
        {
            set(field.Text ?? "");
            typing.Stop();
            typing.Start();
        };
        field.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || string.IsNullOrEmpty(field.Text)) return;
            field.Text = "";
            e.Handled = true;
        };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 10) };
        top.Children.Add(field);
        top.Children.Add(count);

        Show();
        var panel = new StackPanel();
        panel.Children.Add(top);
        panel.Children.Add(body);
        return panel;
    }

    private static Grid Row(IReadOnlyList<Control> cells, IReadOnlyList<double> widths)
    {
        var g = new Grid();
        for (int i = 0; i < cells.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = widths[i] <= 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(widths[i]),
                MinWidth = widths[i] <= 0 ? 140 : 0,
            });
            Grid.SetColumn(cells[i], i);
            g.Children.Add(cells[i]);
        }
        return g;
    }
}

public static class ClipboardText
{
    /// <summary>Puts text on the clipboard (through the main window; there is none in a run with no window, and then nothing is copied).</summary>
    public static bool Copy(string text)
    {
        if (Ui.MainWindow?.Clipboard is not { } clipboard) return false;
        _ = Set(clipboard, text);
        return true;
    }

    private static async Task Set(IClipboard clipboard, string text)
    {
        try { await clipboard.SetTextAsync(text); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }
}
