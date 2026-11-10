using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MnMCompanion.Update;

namespace MnMCompanion;

/// <summary>
/// ⓘ → Send a suggestion…: a box to type into and a Send button. What goes is the words, the program's version and the player's random contributor id
/// (so a flood from one install can be shut off), and only when Send is pressed; the service keeps it privately for the project's owner. Nothing from
/// the game or the ledger goes with it. For a bug with details, the GitHub issue page is a click away.
/// </summary>
internal sealed class SuggestionWindow : Window
{
    private readonly HistoryData _history;
    private readonly CommunityClient _client;
    private readonly TextBox _box;
    private readonly TextBlock _count = new() { FontSize = 11.5, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = Palette.Muted };
    private readonly Button _send;
    private bool _sending;

    public SuggestionWindow(HistoryData history, CommunityClient client)
    {
        _history = history;
        _client = client;
        Dialogs.Dress(this, "Send a suggestion", 520);

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        stack.Children.Add(Dialogs.Title("Send a suggestion"));
        stack.Children.Add(Dialogs.Text("An idea, something that bothers you, a page you'd like: it goes privately to the person who makes Companion. " +
            "Nothing from your game or your ledger goes with it, only what you write, the program's version and your random id (the one sharing uses), so that nobody can flood the box.", 10, Palette.Muted));

        _box = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, MinHeight = 150, MaxHeight = 320, Margin = new Thickness(0, 14, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1C)), Foreground = Palette.Text, CaretBrush = Palette.Text,
            BorderBrush = (Brush)Ui.Theme["Stroke"], BorderThickness = new Thickness(1), MaxLength = Suggestion.MaxChars,
        };
        _box.TextChanged += (_, _) => Count();
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; _ = SendAsync(); }
        };
        stack.Children.Add(_box);
        stack.Children.Add(_count);

        var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var github = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var link = new Hyperlink(new Run("Report a problem on GitHub instead")) { Foreground = new SolidColorBrush(Palette.AccentColor), ToolTip = "Opens the project's issue page in your browser, for a bug with details to attach." };
        link.Click += (_, _) => Dialogs.OpenPage(UpdateService.ProjectUrl + "/issues/new/choose");
        github.Inlines.Add(link);
        DockPanel.SetDock(github, Dock.Left);
        row.Children.Add(github);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _send = Dialogs.Pill("Send", 18, bold: true, tip: "Sends what you wrote (Ctrl+Enter does too).", click: () => _ = SendAsync());
        _send.IsEnabled = false;
        var close = Dialogs.Pill("Close", 14, click: Close);
        close.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(_send);
        buttons.Children.Add(close);
        row.Children.Add(buttons);
        stack.Children.Add(row);
        stack.Children.Add(_status);

        Content = stack;
        Count();
        Loaded += (_, _) => _box.Focus();
    }

    private void Count()
    {
        var length = Suggestion.Clean(_box.Text).Length;
        _count.Text = $"{length:N0} / {Suggestion.MaxChars:N0}";
        _send.IsEnabled = length > 0 && !_sending;
    }

    private async Task SendAsync()
    {
        if (_sending || Suggestion.Clean(_box.Text).Length == 0) return;
        _sending = true;
        _send.IsEnabled = false;
        _box.IsReadOnly = true;
        _status.Foreground = Palette.Muted;
        _status.Text = "Sending…";
        try
        {
            var id = await Task.Run(_history.ContributorId);
            var result = await _client.SuggestAsync(id ?? "", _box.Text, AppVersion.Current());
            if (result.Outcome == SendOutcome.Sent)
            {
                _status.Foreground = Palette.Heal;
                _status.Text = "Sent. Thank you! Close this window, or write another.";
                _box.Clear();
            }
            else
            {
                _status.Foreground = Palette.Taken;
                _status.Text = result.Message + (result.Outcome is SendOutcome.Failed ? " Your words are still here; try again in a moment." : "");
            }
        }
        finally
        {
            _sending = false;
            _box.IsReadOnly = false;
            Count();
        }
    }
}
