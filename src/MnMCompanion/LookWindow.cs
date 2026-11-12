using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MnMCompanion;

/// <summary>
/// "Size and font…": how big the window it was opened from is drawn, and which typeface every window uses. A change is made as it is chosen and
/// kept at once. The size belongs to that kind of window (the big History window on the second screen can be large while the timers stay small);
/// the font is one for the whole program. Nothing is sent anywhere.
/// </summary>
internal sealed class LookWindow : Window
{
    private static LookWindow? _open;

    private readonly WindowScale? _scale;
    private readonly Settings _settings;
    private readonly TextBlock _percent = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, Width = 78, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(int Percent, RadioButton Button)> _presets = [];
    private bool _showing;

    /// <summary>Opens the window for <paramref name="target"/> (one at a time: a second request replaces the first).</summary>
    public static void Open(Window target, Settings settings)
    {
        _open?.Close();
        var window = new LookWindow(target, settings) { Owner = target };
        window.Closed += (_, _) => { if (ReferenceEquals(_open, window)) _open = null; };
        _open = window;
        window.Show();
        window.Activate();
    }

    private LookWindow(Window target, Settings settings)
    {
        _settings = settings;
        _scale = WindowScale.Of(target);
        Dialogs.Dress(this, "Size and font", 470);

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        stack.Children.Add(Dialogs.Title("Size and font"));

        if (_scale is { } scale)
        {
            var heading = Dialogs.Text($"Size of the {scale.Name}", 16, Palette.Text, 13.5);
            heading.FontWeight = FontWeights.SemiBold;
            stack.Children.Add(heading);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Dialogs.Pill("−", 16, bold: true, tip: "Smaller (Ctrl and −)", click: scale.Smaller));
            row.Children.Add(_percent);
            row.Children.Add(Dialogs.Pill("+", 16, bold: true, tip: "Bigger (Ctrl and +)", click: scale.Bigger));
            var normal = Dialogs.Pill("Normal size", 12, tip: "Back to 100% (Ctrl and 0)", click: scale.Reset);
            normal.Margin = new Thickness(14, 0, 0, 0);
            row.Children.Add(normal);
            stack.Children.Add(row);

            var grid = new UniformGrid { Columns = UiPresets.Count };
            foreach (var preset in UiPresets)
            {
                var p = preset;
                var rb = new RadioButton { Content = new TextBlock { Text = p + "%" }, Style = (Style)Ui.Theme["Segment"], GroupName = "LookSize", Focusable = false };
                rb.Checked += (_, _) => { if (!_showing) scale.Set(p); };
                _presets.Add((p, rb));
                grid.Children.Add(rb);
            }
            stack.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = (Brush)Ui.Theme["Subtle"], Padding = new Thickness(2), Margin = new Thickness(0, 12, 0, 0), Child = grid });
            stack.Children.Add(Dialogs.Text("Ctrl and the mouse wheel over a window does the same, as do Ctrl and + or −, and Ctrl and 0 for the normal size. Each kind of window keeps its own size, " +
                "and the window grows with it so you see the same page, only larger.", 10, Palette.Muted, 11.5));
            scale.Changed += ShowPercent;
            Closed += (_, _) => scale.Changed -= ShowPercent;
            ShowPercent();
        }

        var fontHeading = Dialogs.Text("Font, for every window", _scale is null ? 16 : 22, Palette.Text, 13.5);
        fontHeading.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(fontHeading);
        var list = BuildFontList();
        list.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(8), BorderBrush = (Brush)Ui.Theme["Stroke"], BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = list });
        stack.Children.Add(Dialogs.Text("Only fonts that Windows has are listed. The icons keep their own font.", 8, Palette.Muted, 11.5));

        var close = Dialogs.Pill("Close", 16, click: Close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 16, 0, 0);
        stack.Children.Add(close);
        Content = stack;
    }

    private static IReadOnlyList<int> UiPresets => Display.UiScale.Presets;

    private ListBox BuildFontList()
    {
        var list = new ListBox { Style = (Style)Ui.Theme["NavList"], MaxHeight = 270, Focusable = false };
        var names = new List<string?> { null };
        names.AddRange(Ui.InstalledFonts);
        foreach (var name in names)
        {
            var text = new TextBlock { Text = name ?? "Default", FontSize = 14, Foreground = Palette.Text };
            if (name is null) text.Text = "Default";
            else text.FontFamily = new FontFamily(name);
            var row = new DockPanel();
            if (name is null)
            {
                var sample = new TextBlock { Text = "the program's own", FontSize = 11.5, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                row.Children.Add(text);
                row.Children.Add(sample);
            }
            else row.Children.Add(text);
            list.Items.Add(new ListBoxItem { Content = row, Tag = name });
        }
        list.SelectedIndex = Math.Max(0, names.FindIndex(n => string.Equals(n, Ui.Font, StringComparison.OrdinalIgnoreCase)));
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem { } item) return;
            var chosen = item.Tag as string;
            Ui.ApplyFont(chosen);
            _settings.UiFont = Ui.Font;
            _settings.Save();
        };
        return list;
    }

    private void ShowPercent()
    {
        if (_scale is not { } scale) return;
        _showing = true;
        _percent.Text = scale.Percent + "%";
        foreach (var (percent, button) in _presets) button.IsChecked = percent == scale.Percent;
        _showing = false;
    }
}
