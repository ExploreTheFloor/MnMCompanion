using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace MnMCompanion;

/// <summary>The top of every page of the small window that shows a day: the day arrows, the day, a line of figures, the headline number, and the session chip.</summary>
internal static class SessionHeader
{
    public static string DayTitle(DateOnly day) =>
        day == SessionScope.Today ? $"Today · {day.ToString("ddd d MMM", CultureInfo.CurrentCulture)}" : day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);

    public static FrameworkElement Build(SessionScope session, TextBlock title, TextBlock sub, TextBlock big, TextBlock caption, Button scope)
    {
        var prev = LootPanel.Icon("", "Previous day", "Show the day before with loot.", () => session.Step(-1), size: 30);
        var next = LootPanel.Icon("", "Next day", "Show the day after (back to today).", () => session.Step(+1), size: 30);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(prev);
        nav.Children.Add(next);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(title);
        titles.Children.Add(sub);
        var bigs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        bigs.Children.Add(big);
        bigs.Children.Add(caption);
        var head = new DockPanel();
        DockPanel.SetDock(nav, Dock.Left);
        DockPanel.SetDock(bigs, Dock.Right);
        head.Children.Add(nav);
        head.Children.Add(bigs);
        head.Children.Add(titles);

        scope.Click += (_, _) => session.OpenMenu(scope);

        scope.Margin = new Thickness(0);
        var scopeRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        scopeRow.Children.Add(scope);
        var header = new StackPanel();
        header.Children.Add(head);
        header.Children.Add(scopeRow);
        return header;
    }
}
