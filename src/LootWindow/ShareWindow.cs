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
/// "Share data with the community": says in plain words what the community data is, shows exactly what would be in the player's file
/// (and what never is), and saves it. The app sends nothing: the player uploads the file themselves on GitHub, or doesn't.
/// </summary>
internal sealed class ShareWindow : Window
{
    private readonly HistoryData _history;
    private readonly string _app;
    private readonly StackPanel _stack = new() { Margin = new Thickness(26, 22, 26, 22) };
    private readonly StackPanel _preview = new();
    private StackPanel? _after;
    private readonly TextBlock _id = new() { FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _saved = new() { FontSize = 12, Foreground = Palette.Heal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _save;
    private Contribution? _contribution;
    private string? _savedTo;

    public ShareWindow(HistoryData history, string app)
    {
        _history = history;
        _app = app;
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = "Share data with the community";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 900;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)Ui.Theme["Window"];
        Foreground = Palette.Text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        SourceInitialized += (_, _) => DarkTitleBar();

        var stack = _stack;
        stack.Children.Add(new TextBlock { Text = "Help build the community data", FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
        stack.Children.Add(Paragraph(
            "The community data is an open database that everyone can add to and use: how often each monster drops each item, what vendors pay and charge, and what gathering yields. " +
            "It gets better with every player who adds their own numbers.", 8, 0));
        stack.Children.Add(Paragraph(
            "Loot Goblin can save your own totals as one small file. **Nothing is sent by the app.** You look at what's in it below, and if you're happy, you upload the file to the " +
            "community's page on GitHub yourself.", 8, 0));

        stack.Children.Add(Heading("What's in your file"));
        stack.Children.Add(_preview);
        _preview.Children.Add(Line("Working out your totals…", Palette.Muted));

        stack.Children.Add(Heading("What is never in it"));
        foreach (var line in new[]
        {
            "Your character names, or any other player's name",
            "Individual loot lines, trades, or the time anything happened (only the day you save it)",
            "Anything from other players' ledgers: only what you looted, sold and gathered yourself",
        })
            stack.Children.Add(Line(line, Palette.Text, "✕  "));

        stack.Children.Add(_id);
        stack.Children.Add(Paragraph($"By sharing the file you give it to the community under {CommunityData.LicenseName}: free for anyone to use for non-commercial purposes, with credit.", 10, 0, muted: true));
        stack.Children.Add(_saved);

        _save = new Button { Content = "Save my file…", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(16, 7, 16, 7), IsEnabled = false, ToolTip = "Saves the file so you can look at it and upload it" };
        _save.Click += (_, _) => Save();
        var how = new Button { Content = "How to send it", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Opens the instructions on GitHub" };
        how.Click += (_, _) => Open(CommunityData.HowToUrl);
        var close = new Button { Content = "Close", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(_save);
        buttons.Children.Add(how);
        buttons.Children.Add(close);
        stack.Children.Add(buttons);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        Loaded += async (_, _) => await Prepare();
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
        var lines = ContributionBuilder.Describe(_contribution);
        foreach (var line in lines) _preview.Children.Add(Line(line, Palette.Text, "✓  "));
        bool nothing = _contribution.Totals is { Corpses: 0, Sales: 0, Purchases: 0, Harvests: 0 };
        if (nothing) _preview.Children.Add(Line("There is nothing to share yet. Loot, sell and gather a bit, then come back.", Palette.Muted));
        _id.Text = $"Your random id is {_contribution.Contributor[..8]}…  It isn't linked to your name or your characters. Sending a new file later with the same id replaces your old one, so nothing is counted twice.";
        _save.IsEnabled = !nothing;
    }

    private void Save()
    {
        if (_contribution is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save your file for the community data",
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
        _savedTo = dialog.FileName;
        _saved.Text = $"Saved to {dialog.FileName}\n\nNext: open the community page (button below), choose Add file → Upload files, drop this file in (keep its name), and press Commit. GitHub turns it into a request that is checked automatically.";
        _saved.Visibility = Visibility.Visible;
        var open = new Button { Content = "Open the community page", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 8, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => Open(CommunityData.SubmitUrl);
        var folder = new Button { Content = "Show the file", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        folder.Click += (_, _) => { if (_savedTo is not null) Open("explorer.exe", $"/select,\"{_savedTo}\""); };
        if (_after is not null) _stack.Children.Remove(_after);
        _after = new StackPanel { Orientation = Orientation.Horizontal };
        _after.Children.Add(open);
        _after.Children.Add(folder);
        _stack.Children.Insert(_stack.Children.IndexOf(_saved) + 1, _after);
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

    private static void Open(string target, string? arguments = null)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments ?? "" }); }
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
