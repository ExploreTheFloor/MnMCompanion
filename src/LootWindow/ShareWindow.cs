using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LootWindow.Community;
using LootWindow.Update;

namespace LootWindow;

/// <summary>
/// "Share my data with the community": the Yes / No switch, what saying Yes shares (and what never is), the player's own totals
/// previewed, and a way to save a copy of the file. No is the default; while it is No nothing leaves the PC and community data isn't
/// used. This window is also where the choice can be changed at any time.
/// </summary>
internal sealed class ShareWindow : Window
{
    private readonly HistoryData _history;
    private readonly Settings _settings;
    private readonly string _app;
    private readonly StackPanel _stack = new() { Margin = new Thickness(26, 22, 26, 22) };
    private readonly StackPanel _preview = new();
    private readonly TextBlock _status = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 4, 0, 0) };
    private readonly TextBlock _id = new() { FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _saved = new() { FontSize = 12, Foreground = Palette.Heal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly CheckBox _share;
    private readonly Button _save;
    private Contribution? _contribution;

    public ShareWindow(HistoryData history, Settings settings, string app)
    {
        _history = history;
        _settings = settings;
        _app = app;
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = "Share my data with the community";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 940;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Ui.Theme["Window"];
        Foreground = Palette.Text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        SourceInitialized += (_, _) => DarkTitleBar();

        _stack.Children.Add(new TextBlock { Text = "Share my data with the community", FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
        _stack.Children.Add(Paragraph(
            "The community data is an open database that everyone can add to and use: how often each monster drops each item, what vendors pay and charge, and what gathering yields. " +
            "It's built from the totals of players who choose to share.", 8, 0));
        _stack.Children.Add(Paragraph(
            "**Yes:** Loot Goblin shares your own totals for you, automatically, and keeps them up to date, and you can use the community data. " +
            "**No (the default):** nothing leaves your PC, and the community data isn't used.", 8, 0));

        _share = new CheckBox
        {
            Content = new TextBlock { Text = "Share my data with the community, automatically", FontSize = 13.5, FontWeight = FontWeights.SemiBold },
            IsChecked = settings.ShareWithCommunity, Margin = new Thickness(0, 16, 0, 0), Cursor = System.Windows.Input.Cursors.Hand,
        };
        _share.Click += (_, _) =>
        {
            _settings.ShareWithCommunity = _share.IsChecked == true;
            _settings.Save();
            ShowStatus();
        };
        _stack.Children.Add(_share);
        _stack.Children.Add(_status);
        ShowStatus();

        _stack.Children.Add(Heading("What your sharing includes"));
        _stack.Children.Add(_preview);
        _preview.Children.Add(Line("Working out your totals…", Palette.Muted));

        _stack.Children.Add(Heading("What is never included"));
        foreach (var line in new[]
        {
            "Your character names, or any other player's name",
            "Individual loot lines, trades, or the time anything happened (only the day)",
            "Anything from other players' ledgers: only what you looted, sold and gathered yourself",
        })
            _stack.Children.Add(Line(line, Palette.Text, "✕  "));

        _stack.Children.Add(_id);
        _stack.Children.Add(Paragraph($"What you share is given to the community under {CommunityData.LicenseName}: free for anyone to use for non-commercial purposes, with credit.", 10, 0, muted: true));
        _stack.Children.Add(_saved);

        _save = new Button { Content = "Save a copy of my file…", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(16, 7, 16, 7), IsEnabled = false, ToolTip = "Saves what would be shared, so you can read it" };
        _save.Click += (_, _) => Save();
        var how = new Button { Content = "How it works", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Opens the community project on GitHub" };
        how.Click += (_, _) => Open(CommunityData.HowToUrl);
        var close = new Button { Content = "Close", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(_save);
        buttons.Children.Add(how);
        buttons.Children.Add(close);
        _stack.Children.Add(buttons);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stack };
        Loaded += async (_, _) => await Prepare();
    }

    private void ShowStatus()
    {
        bool on = _settings.ShareWithCommunity;
        _status.Text = CommunityPolicy.Status(on, sendingConnected: Sharing.Connected);
        _status.Foreground = on ? Palette.Heal : Palette.Muted;
    }

    private async Task Prepare()
    {
        try
        {
            _contribution = await _history.BuildContributionAsync(_app);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _preview.Children.Clear();
            _preview.Children.Add(Line("Couldn't work out your totals: " + ex.Message, Palette.Taken));
            return;
        }
        _preview.Children.Clear();
        if (_contribution is null)
        {
            _preview.Children.Add(Line("The saved history isn't available, so there is nothing to share yet.", Palette.Muted));
            return;
        }
        foreach (var line in ContributionBuilder.Describe(_contribution)) _preview.Children.Add(Line(line, Palette.Text, "✓  "));
        bool nothing = _contribution.Totals is { Corpses: 0, Sales: 0, Purchases: 0, Harvests: 0 };
        if (nothing) _preview.Children.Add(Line("There is nothing to share yet. Loot, sell and gather a bit first.", Palette.Muted));
        _id.Text = $"Your random id is {_contribution.Contributor[..8]}…  It isn't linked to your name or your characters. Each time your totals are shared they replace your last ones, so nothing is counted twice.";
        _save.IsEnabled = !nothing;
    }

    private void Save()
    {
        if (_contribution is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save a copy of what would be shared",
            FileName = _contribution.Contributor + ".json",
            DefaultExt = ".json",
            Filter = "Community data file (*.json)|*.json",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, _contribution.ToJson());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "The file couldn't be saved: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _saved.Text = $"Saved to {dialog.FileName}. This is only a copy for you to read: sharing is the switch above.";
        _saved.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) };

    private static TextBlock Line(string text, Brush colour, string bullet = "") =>
        new() { Text = bullet + text, FontSize = 12.5, Foreground = colour, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };

    /// <summary>A paragraph; text between ** ** is bold.</summary>
    private static TextBlock Paragraph(string text, double top, double bottom, bool muted = false)
    {
        var block = new TextBlock { FontSize = 12.5, Foreground = muted ? Palette.Muted : Palette.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, bottom) };
        var parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++)
            block.Inlines.Add(new System.Windows.Documents.Run(parts[i]) { FontWeight = i % 2 == 1 ? FontWeights.SemiBold : FontWeights.Normal });
        return block;
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
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

/// <summary>Whether the part that sends a sharing player's totals is connected yet. It isn't: the community drop box isn't online.</summary>
internal static class Sharing
{
    public static bool Connected => false;
}
