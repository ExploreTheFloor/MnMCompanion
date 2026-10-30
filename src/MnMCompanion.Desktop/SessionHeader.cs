using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MnMCompanion;

/// <summary>The top of every page of the small window that shows a day: the day arrows, the day, a line of figures, the headline number, and the session chip.</summary>
internal static class SessionHeader
{
    public static string DayTitle(DateOnly day) =>
        day == SessionScope.Today ? $"Today · {day.ToString("ddd d MMM", CultureInfo.CurrentCulture)}" : day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);

    /// <param name="big">The headline number, at the right of the title; null when the page puts its figures in <paramref name="below"/> instead.</param>
    /// <param name="below">A row of figures between the title and the session chip (the Loot page's Ours and Total). The counts line then sits at the right of the title instead of under it.</param>
    /// <param name="scopeExtras">Buttons at the right end of the session chip's row.</param>
    public static Control Build(SessionScope session, TextBlock title, TextBlock sub, TextBlock? big, TextBlock? caption, Button scope, Control? below = null, Control? scopeExtras = null)
    {
        var prev = LootPanel.Icon(Glyphs.Previous, "Previous day", "Show the day before with loot.", () => session.Step(-1), size: 30, fontSize: 15);
        var next = LootPanel.Icon(Glyphs.Next, "Next day", "Show the day after (back to today).", () => session.Step(+1), size: 30, fontSize: 15);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(prev);
        nav.Children.Add(next);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(title);
        var head = new DockPanel();
        DockPanel.SetDock(nav, Dock.Left);
        head.Children.Add(nav);
        if (big is not null && caption is not null)
        {
            titles.Children.Add(sub);
            var bigs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            bigs.Children.Add(big);
            bigs.Children.Add(caption);
            DockPanel.SetDock(bigs, Dock.Right);
            head.Children.Add(bigs);
        }
        else if (below is not null)
        {
            // No headline number to put at the right: the counts take that room, so they need no line of their own
            sub.Margin = new Thickness(8, 0, 0, 0);
            sub.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(sub, Dock.Right);
            head.Children.Add(sub);
        }
        else titles.Children.Add(sub);
        head.Children.Add(titles);

        scope.Click += (_, _) => session.OpenMenu(scope);

        scope.Margin = new Thickness(0);
        var scopeRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (scopeExtras is not null)
        {
            scopeExtras.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(scopeExtras, Dock.Right);
            scopeRow.Children.Add(scopeExtras);
        }
        scopeRow.Children.Add(scope);
        var header = new StackPanel();
        header.Children.Add(head);
        if (below is not null) header.Children.Add(below);
        header.Children.Add(scopeRow);
        return header;
    }
}
