using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using MnMCompanion.Community;
using MnMCompanion.Update;

namespace MnMCompanion;

internal enum ShareMode
{
    /// <summary>The start-up question: Yes or No. Closing it is a No.</summary>
    Ask,
    /// <summary>From the ⓘ menu: shows the current answer as a Yes | No switch, and what sharing includes.</summary>
    Settings,
}

/// <summary>
/// The one window about sharing with the community. In <see cref="ShareMode.Ask"/> it is a short question (Yes / No) with "What is
/// shared" unfolding inline, so there is never a second window on top of it. In <see cref="ShareMode.Settings"/> it shows the answer as a
/// Yes | No switch, what is shared, what never is, and can save a copy of the file. No is the default; while it is No nothing leaves
/// the PC and community data isn't used.
/// </summary>
internal sealed class ShareWindow : Window
{
    private readonly HistoryData _history;
    private readonly Settings _settings;
    private readonly string _app;
    private readonly ShareMode _mode;
    private readonly StackPanel _stack = new() { Margin = new Thickness(26, 22, 26, 22) };
    private readonly StackPanel _details = new();
    private readonly StackPanel _preview = new();
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _id = new() { FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _saved = new() { FontSize = 12, Foreground = Palette.Heal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _save;
    private Contribution? _contribution;
    private bool _loading = true;

    /// <summary>The preview of what would be shared has been worked out and is on show (for drawing a picture of the window).</summary>
    internal bool PreviewReady => _contribution is not null;

    /// <param name="yes">Ask mode only: called when the player presses Yes.</param>
    public ShareWindow(HistoryData history, Settings settings, string app, ShareMode mode, Action? yes = null)
    {
        _history = history;
        _settings = settings;
        _app = app;
        _mode = mode;
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = mode == ShareMode.Ask ? "Share your data with the community?" : "Sharing with the community";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 900;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;
        Topmost = mode == ShareMode.Ask;
        Background = (Brush)Ui.Theme["Window"];
        Foreground = Palette.Text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        SourceInitialized += (_, _) => DarkTitleBar();

        _stack.Children.Add(new TextBlock
        {
            Text = mode == ShareMode.Ask ? "Share your data with the community?" : "Sharing with the community",
            FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        });
        _stack.Children.Add(Text("Players pool their own totals into one combined database of drop rates, vendor prices and gathering yields, kept privately by the project and never published. " +
            "Saying Yes adds yours for you, automatically, and lets you use what everyone has shared.", 10, Palette.Muted));
        if (settings.ConsentNeedsRenewal)
            _stack.Children.Add(Text(CommunityPolicy.RenewalNote(settings.ShareConsentSchema, ContributionBuilder.Schema), 10, Palette.Text));
        _stack.Children.Add(Bullet("Yes", "your totals are shared for you, and you can use the community data."));
        _stack.Children.Add(Bullet("No", mode == ShareMode.Ask
            ? "nothing leaves your PC and the community data stays off. You'll be asked again next time."
            : "nothing leaves your PC and the community data stays off (the default)."));

        _details.Visibility = mode == ShareMode.Settings ? Visibility.Visible : Visibility.Collapsed;
        if (mode == ShareMode.Ask)
        {
            // Yes | What is shared | No: three buttons of one size, the middle one unfolding the details below
            var yesButton = Pill("Yes, share for me", 8, bold: true, tip: "Turns sharing on. You can change it any time from the ⓘ menu.", click: () => { yes?.Invoke(); Close(); });
            var toggle = Pill("What is shared  ▾", 8, tip: "Shows exactly what sharing includes, before you decide");
            var noButton = Pill("No", 8, tip: "Nothing is shared. You'll be asked again next time.", click: Close);
            toggle.Click += (_, _) =>
            {
                bool open = _details.Visibility != Visibility.Visible;
                _details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                toggle.Content = open ? "What is shared  ▴" : "What is shared  ▾";
            };
            yesButton.Margin = new Thickness(0, 0, 4, 0);
            toggle.Margin = new Thickness(4, 0, 4, 0);
            noButton.Margin = new Thickness(4, 0, 0, 0);
            var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 20, 0, 0) };
            row.Children.Add(yesButton);
            row.Children.Add(toggle);
            row.Children.Add(noButton);
            _stack.Children.Add(row);
        }
        else
        {
            _stack.Children.Add(Switch());
            _stack.Children.Add(_status);
            ShowStatus();
        }

        _details.Children.Add(Heading("Included: only your own play"));
        _details.Children.Add(_preview);
        _preview.Children.Add(Line("Working out your totals…", Palette.Muted));
        _details.Children.Add(Heading("Never included"));
        foreach (var line in new[]
        {
            "Your character names, or any other player's name",
            "Individual loot lines, or when anything happened (only the day the file was made, the weeks the running totals fall in, and the day of each trade)",
            "Who you traded with: the other player's name or number is left out of every trade",
            "Anything from other players' ledgers: only what you looted, sold and gathered",
            "Your character's name in what NPCs said to you (it is replaced), or when they said it",
        })
            _details.Children.Add(Line(line, Palette.Text, "✕  "));
        _details.Children.Add(_id);
        _details.Children.Add(Text("Your totals are only used to build the combined figures that sharing players get back. They are kept privately and are not published.", 8, Palette.Muted, 11.5));
        _details.Children.Add(_saved);

        _save = Pill("Save a copy of my file…", 16, tip: "Saves what would be shared, so you can read it");
        _save.IsEnabled = false;
        _save.Margin = new Thickness(0, 14, 0, 0);
        _save.HorizontalAlignment = HorizontalAlignment.Left;
        _save.Click += (_, _) => Save();
        if (mode == ShareMode.Settings) _details.Children.Add(_save);
        _stack.Children.Add(_details);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stack };
        Loaded += async (_, _) => { _loading = false; await Prepare(); };
    }

    // ------------------------------------------------------------------ the pieces

    /// <summary>The Yes | No switch, in the same segmented style as the app's tabs.</summary>
    private FrameworkElement Switch()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, value) in new[] { ("Yes", true), ("No", false) })
        {
            var rb = new RadioButton
            {
                Content = new TextBlock { Text = text, Margin = new Thickness(30, 0, 30, 0) }, Style = (Style)Ui.Theme["Segment"],
                GroupName = "ShareChoice", IsChecked = _settings.Sharing == value, FontSize = 13,
            };
            bool answer = value;
            rb.Checked += (_, _) =>
            {
                if (_loading) return;
                _settings.SetSharing(answer);
                _settings.Save();
                ShowStatus();
            };
            row.Children.Add(rb);
        }
        return new Border { CornerRadius = new CornerRadius(8), Background = (Brush)Ui.Theme["Subtle"], Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0), Child = row };
    }

    private void ShowStatus()
    {
        bool on = _settings.Sharing;
        _status.Text = CommunityPolicy.Status(on, sendingConnected: true, _settings.ShareNote);
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
        _id.Text = $"Your random id is {_contribution.Contributor[..8]}…, not linked to your name or characters. Each time your totals are shared they replace your last ones, so nothing is counted twice.";
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
            File.WriteAllText(dialog.FileName, _contribution.ToCoreJson(indented: true));
            // The dialogue is sent as a second, separate file, so the copy has it beside the first
            if (_contribution.Npcs.Count > 0) File.WriteAllText(Path.ChangeExtension(dialog.FileName, null) + ".npcs.json", _contribution.ToNpcPartJson(indented: true));
            if ((_contribution.Trades ?? []).Count > 0) File.WriteAllText(Path.ChangeExtension(dialog.FileName, null) + ".trades.json", _contribution.ToTradesPartJson(indented: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "The file couldn't be saved: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _saved.Text = $"Saved to {dialog.FileName}" + (_contribution.Npcs.Count > 0 ? $" and {Path.GetFileName(Path.ChangeExtension(dialog.FileName, null))}.npcs.json (what NPCs said to you, sent as a second small file)" : "")
            + ((_contribution.Trades ?? []).Count > 0 ? $" and {Path.GetFileName(Path.ChangeExtension(dialog.FileName, null))}.trades.json (your trades with other players, sent as another small file)" : "")
            + ". It's only a copy to read; sharing is the Yes | No switch above.";
        _saved.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ small helpers (every text is given its colour: nothing is left to the system's)

    private static Button Pill(string text, double side, bool bold = false, double margin = 0, string? tip = null, Action? click = null)
    {
        var b = new Button
        {
            Content = text, Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(side, 9, side, 9), Margin = new Thickness(margin, 0, 0, 0),
            FontSize = 13, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, ToolTip = tip,
        };
        if (click is not null) b.Click += (_, _) => click();
        return b;
    }

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, Margin = new Thickness(0, 16, 0, 6) };

    private static TextBlock Line(string text, Brush colour, string bullet = "") =>
        new() { Text = bullet + text, FontSize = 12.5, Foreground = colour, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };

    private static TextBlock Text(string text, double top, Brush colour, double size = 12.5) =>
        new() { Text = text, FontSize = size, Foreground = colour, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };

    /// <summary>"Yes  your totals…": the word in bold, then the rest.</summary>
    private static TextBlock Bullet(string word, string rest)
    {
        var block = new TextBlock { FontSize = 12.5, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        block.Inlines.Add(new Run(word + "  ") { FontWeight = FontWeights.SemiBold, Foreground = Palette.Text });
        block.Inlines.Add(new Run(rest) { Foreground = Palette.Muted });
        return block;
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

