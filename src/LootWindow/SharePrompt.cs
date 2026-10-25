using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace LootWindow;

/// <summary>
/// The question asked every time the app starts while the answer is No: share your data with the community, yes or no. It doesn't
/// block the app (answer it or leave it), and closing it, with No or the cross, counts as No. Yes is remembered and the question stops.
/// </summary>
internal sealed class SharePrompt : Window
{
    /// <param name="yes">Called when the player presses Yes.</param>
    /// <param name="details">Opens the window that shows exactly what is shared.</param>
    public SharePrompt(Action yes, Action details)
    {
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = "Share your data with the community?";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        ShowInTaskbar = true;   // a taskbar button, so it can't be missed
        Background = (Brush)Ui.Theme["Window"];
        Foreground = Palette.Text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        SourceInitialized += (_, _) => DarkTitleBar();

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(new TextBlock { Text = "Share your data with the community?", FontSize = 18, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(Text("Players pool their own totals into an open database: how often monsters drop things, what vendors pay, what gathering yields. " +
            "It's made of what players like you share, and Loot Goblin can add yours for you, automatically.", 10));
        stack.Children.Add(Text("It never includes names (yours or anyone else's) or when anything happened. Only totals.", 8));
        stack.Children.Add(Text("**Yes** also lets you use the community data. **No** keeps everything on your PC and the community data off. You'll be asked again next time you start.", 8));

        var yesButton = new Button { Content = "Yes, share for me", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(18, 8, 18, 8), FontWeight = FontWeights.SemiBold, ToolTip = "Turns sharing on. You can change it any time from the ⓘ menu." };
        yesButton.Click += (_, _) => { yes(); Close(); };
        var noButton = new Button { Content = "No", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Nothing is shared. You'll be asked again next time." };
        noButton.Click += (_, _) => Close();
        var more = new Button { Content = "What is shared?", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Shows exactly what sharing includes, before you decide" };
        more.Click += (_, _) => details();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(yesButton);
        buttons.Children.Add(noButton);
        buttons.Children.Add(more);
        stack.Children.Add(buttons);
        Content = stack;
    }

    private static TextBlock Text(string text, double top)
    {
        var block = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        var parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++)
            block.Inlines.Add(new System.Windows.Documents.Run(parts[i]) { FontWeight = i % 2 == 1 ? FontWeights.SemiBold : FontWeights.Normal });
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
