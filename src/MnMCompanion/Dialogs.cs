using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MnMCompanion;

/// <summary>The bits every small dialog of the app shares: the dark look, the title-bar colour, and the buttons and text blocks in the app's style.</summary>
internal static class Dialogs
{
    /// <summary>Gives a window the app's colours, fonts and dark title bar; call before the content is set.</summary>
    public static void Dress(Window window, string title, double width)
    {
        window.Resources.MergedDictionaries.Add(Ui.Theme);
        window.Title = title;
        window.Width = width;
        window.SizeToContent = SizeToContent.Height;
        window.MaxHeight = 900;
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ShowInTaskbar = true;
        window.Background = (Brush)Ui.Theme["Window"];
        window.Foreground = Palette.Text;
        window.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        window.SourceInitialized += (_, _) => DarkTitleBar(window);
    }

    public static TextBlock Title(string text) => new()
    {
        Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
    };

    public static TextBlock Text(string text, double top, Brush colour, double size = 12.5) =>
        new() { Text = text, FontSize = size, Foreground = colour, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };

    public static Button Pill(string text, double side, bool bold = false, string? tip = null, Action? click = null)
    {
        var b = new Button
        {
            Content = text, Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(side, 9, side, 9),
            FontSize = 13, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, ToolTip = tip,
        };
        if (click is not null) b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Opens an address in the player's browser; a missing browser is nothing to crash over.</summary>
    public static void OpenPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void DarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }
}
