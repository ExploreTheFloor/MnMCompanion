using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MnMCompanion.Update;

namespace MnMCompanion;

/// <summary>
/// "What's new": the change log's entry for the running version (and, after an update that skipped versions, every entry since the last one shown), read from the copy
/// of CHANGELOG.md carried inside the program, so it needs no network. It opens by itself once, the first time the program runs after a new version
/// (see <see cref="WhatsNewPolicy"/>), and from ⓘ → What's new in this version… whenever the player likes.
/// </summary>
internal sealed class WhatsNewWindow : Window
{
    /// <summary>The change log carried inside the program; empty if it was built without one.</summary>
    public static string Changelog()
    {
        try
        {
            using var stream = typeof(WhatsNewWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
            if (stream is null) return "";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException) { return ""; }
    }

    /// <param name="entries">What to show, newest first.</param>
    /// <param name="current">The running version (for the title).</param>
    /// <param name="footnote">A line under the notes, if any (where to switch automatic updates off, say).</param>
    public WhatsNewWindow(IReadOnlyList<ReleaseEntry> entries, Version current, string? footnote = null)
    {
        Dialogs.Dress(this, $"What's new in Companion {AppVersion.Show(current)}", 560);

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        stack.Children.Add(Dialogs.Title(entries.Count > 1 ? $"What's new since {AppVersion.Show(entries[^1].Version)}" : $"What's new in {AppVersion.Show(current)}"));
        if (entries.Count == 0)
            stack.Children.Add(Dialogs.Text("This copy carries no notes for its version. The change log on the project page lists every version.", 10, Palette.Muted));
        foreach (var entry in entries)
        {
            if (entries.Count > 1)
                stack.Children.Add(new TextBlock { Text = $"{AppVersion.Show(entry.Version)}  ·  {entry.Date:d MMMM yyyy}", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, Margin = new Thickness(0, 16, 0, 2) });
            else
                stack.Children.Add(Dialogs.Text($"Released {entry.Date:d MMMM yyyy}", 4, Palette.Muted, 11.5));
            foreach (var line in entry.Lines)
            {
                if (line.Length == 0) continue;
                bool bullet = ReleaseNotes.IsBullet(line, out var rest);
                var block = new TextBlock { FontSize = 12.5, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(bullet ? 14 : 0, bullet ? 5 : 10, 0, 0) };
                if (bullet) block.Inlines.Add(new Run("•  ") { Foreground = new SolidColorBrush(Palette.AccentColor) });
                foreach (var piece in ReleaseNotes.Pieces(rest, UpdateService.ProjectUrl)) block.Inlines.Add(Inline(piece));
                stack.Children.Add(block);
            }
        }
        if (footnote is { Length: > 0 }) stack.Children.Add(Dialogs.Text(footnote, 16, Palette.Muted, 11.5));

        var row = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var all = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var link = new Hyperlink(new Run("Every version's notes")) { Foreground = new SolidColorBrush(Palette.AccentColor), ToolTip = "Opens the change log on the project page in your browser." };
        link.Click += (_, _) => Dialogs.OpenPage(UpdateService.ProjectUrl + "/blob/main/CHANGELOG.md");
        all.Inlines.Add(link);
        DockPanel.SetDock(all, Dock.Left);
        row.Children.Add(all);
        var ok = Dialogs.Pill("OK", 22, bold: true, click: Close);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.IsDefault = true;
        row.Children.Add(ok);
        stack.Children.Add(row);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
    }

    private static Inline Inline(NotesPiece piece)
    {
        switch (piece)
        {
            case BoldPiece b:
                return new Run(b.Text) { FontWeight = FontWeights.SemiBold };
            case CodePiece c:
                return new Run(c.Text) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = Palette.Gold };
            case LinkPiece l:
                var link = new Hyperlink(new Run(l.Text)) { Foreground = new SolidColorBrush(Palette.AccentColor), ToolTip = l.Url };
                link.Click += (_, _) => Dialogs.OpenPage(l.Url);
                return link;
            default:
                return new Run(piece.Text);
        }
    }
}
