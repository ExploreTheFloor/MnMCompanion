using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MnMCompanion;

/// <summary>The shared styles and brushes (Theme.xaml and Menus.xaml), merged into each window.</summary>
public static class Ui
{
    private static ResourceDictionary? _theme;
    private static ResourceDictionary? _menus;

    /// <summary>The program's own name as the resource addresses spell it (it is the file name, so it follows a rename).</summary>
    private static readonly string AssemblyName = typeof(Ui).Assembly.GetName().Name!;

    public static ResourceDictionary Theme => _theme ??= new ResourceDictionary
    {
        Source = new Uri($"pack://application:,,,/{AssemblyName};component/Theme.xaml", UriKind.Absolute),
    };

    public static ResourceDictionary Menus => _menus ??= new ResourceDictionary
    {
        Source = new Uri($"pack://application:,,,/{AssemblyName};component/Menus.xaml", UriKind.Absolute),
    };
}

/// <summary>Colours used for people and things, the same everywhere.</summary>
public static class Palette
{
    public static readonly Color AccentColor = Color.FromRgb(0x5B, 0x9D, 0xFF);
    public static readonly Color DangerColor = Color.FromRgb(0xFF, 0x6B, 0x6B);
    public static readonly Color HealColor = Color.FromRgb(0x3D, 0xD6, 0x8C);
    public static readonly Color MutedColor = Color.FromRgb(0x8B, 0x93, 0xA1);

    public static readonly Brush You = Freeze(AccentColor);
    public static readonly Brush Taken = Freeze(DangerColor);
    public static readonly Brush Heal = Freeze(HealColor);
    public static readonly Brush Gold = Freeze(Color.FromRgb(0xF5, 0xC8, 0x6B));
    public static readonly Brush Text = Freeze(Color.FromRgb(0xF2, 0xF4, 0xF7));
    public static readonly Brush Muted = Freeze(MutedColor);

    private static readonly Brush[] Others =
    [
        Freeze(Color.FromRgb(0xB3, 0x8B, 0xFA)), Freeze(Color.FromRgb(0xF5, 0xA6, 0x23)), Freeze(Color.FromRgb(0x2D, 0xD4, 0xBF)),
        Freeze(Color.FromRgb(0xF4, 0x72, 0xB6)), Freeze(Color.FromRgb(0xA3, 0xE6, 0x35)), Freeze(Color.FromRgb(0x94, 0xA3, 0xB8)),
    ];

    /// <summary>A colour per class.</summary>
    private static readonly Dictionary<string, Brush> Classes = new(StringComparer.OrdinalIgnoreCase)
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
    public static Brush For(string name, string? cls = null)
    {
        if (cls is not null && Classes.TryGetValue(cls, out var byClass)) return byClass;
        uint h = 2166136261;
        foreach (char c in name) h = (h ^ char.ToLowerInvariant(c)) * 16777619;
        return Others[h % (uint)Others.Length];
    }

    public static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
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

    public static FrameworkElement Build<T>(string id, IEnumerable<T> rows, IReadOnlyList<Col<T>> cols, Action rebuild,
        Func<T, double>? bar = null, Func<T, Brush>? color = null, Action<T>? click = null, int defaultSort = 1,
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
                Style = (Style)Ui.Theme["HeaderButton"],
                HorizontalContentAlignment = c.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                ToolTip = c.Tip,
                IsEnabled = c.Sort != null,
            };
            int ci = i;
            b.Click += (_, _) =>
            {
                SortState[id] = (ci, ci == sortCol ? !desc : c.Right);
                rebuild();
            };
            return (UIElement)b;
        }).ToList(), cols.Select(c => c.Width).ToList()));
        panel.Children.Add(new Border { Height = 1, Background = (Brush)Ui.Theme["Stroke"] });

        if (list.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Nothing here.", Foreground = Palette.Muted, Margin = new Thickness(8, 10, 0, 6), FontSize = 12 });
            return panel;
        }

        double maxBar = bar is null ? 0 : list.Max(bar);
        foreach (var item in list)
        {
            var cells = cols.Select((c, i) => (UIElement)new TextBlock
            {
                Text = c.Text(item),
                Foreground = Palette.Text,
                FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal,
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

            var rowBorder = new Border { Child = grid, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
            if (copy != null)
            {
                // A small copy button at the right end of the row, there while the pointer is on it
                var row = item;
                var cb = new Button { Content = "", ToolTip = "Copy this line to the clipboard", Style = (Style)Ui.Theme["IconButton"], Width = 24, Height = 24, Padding = new Thickness(0), FontSize = 11, Visibility = Visibility.Hidden, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                Grid.SetColumnSpan(cb, cols.Count);
                cb.Click += (_, e) =>
                {
                    e.Handled = true;
                    ClipboardText.Copy(copy(row));
                    cb.Content = "";
                    var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                    t.Tick += (_, _) => { t.Stop(); cb.Content = ""; };
                    t.Start();
                };
                grid.Children.Add(cb);
                rowBorder.MouseEnter += (_, _) => cb.Visibility = Visibility.Visible;
                rowBorder.MouseLeave += (_, _) => cb.Visibility = Visibility.Hidden;
            }
            if (rowTip?.Invoke(item) is { Length: > 0 } tip)
            {
                rowBorder.ToolTip = new ToolTip { Content = new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 }, Style = (Style)Ui.Theme["Tip"] };
                ToolTipService.SetInitialShowDelay(rowBorder, 300);
            }
            rowBorder.MouseEnter += (_, _) => rowBorder.Background = (Brush)Ui.Theme["Subtle"];
            rowBorder.MouseLeave += (_, _) => rowBorder.Background = Brushes.Transparent;
            if (click != null)
            {
                rowBorder.Cursor = Cursors.Hand;
                rowBorder.MouseLeftButtonUp += (_, _) => click(item);
            }
            panel.Children.Add(rowBorder);
        }
        return panel;
    }

    private static Grid Row(IReadOnlyList<UIElement> cells, IReadOnlyList<double> widths)
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
    /// <summary>Puts text on the clipboard; another program may have it open for a moment, in which case nothing is copied.</summary>
    public static bool Copy(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }
}
