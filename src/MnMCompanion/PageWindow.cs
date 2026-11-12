using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using MnMCompanion.Loot;

namespace MnMCompanion;

/// <summary>
/// One page of the small window (Map, Loot, Vendor, Trades, Items, Harvest, Recipes or Market) in a window of its own, the way History is: moved and resized
/// wherever the player likes, kept on top of the game or not, drawn at its own size (Ctrl + wheel). It is a second copy of the page, looking at the same ledger, history and
/// session, and the small window keeps it up to date (<see cref="Refresh"/>, <see cref="Tick"/>), so it shows the same thing as the page does there. Where it sits and
/// whether it was open are remembered (<see cref="Settings.PageWindows"/>), and it comes back the same way when the app starts.
/// </summary>
internal sealed class PageWindow : Window
{
    private readonly LootStore _store;
    private readonly Settings _settings;
    private readonly TextBlock _who;
    private readonly Button _pin;
    private DispatcherTimer? _saveSoon;

    /// <summary>The page shown here.</summary>
    public IMiniPage Page { get; }

    /// <summary>The name of the page: "Map", "Loot", ...</summary>
    public string Key { get; }

    /// <summary>The player pressed the ✕ (rather than the app closing): the window is not brought back next time.</summary>
    public bool ClosedByPlayer { get; private set; }

    /// <param name="status">The page's status line, which this window shows along its bottom.</param>
    /// <param name="owner">The small window: this one stays in front of it, and goes with it.</param>
    /// <param name="cascade">How many page windows are open already, so a new one that has no place yet doesn't land on top of them.</param>
    public PageWindow(string key, IMiniPage page, TextBlock status, LootStore store, Settings settings, Window owner, int cascade)
    {
        Key = key;
        Page = page;
        _store = store;
        _settings = settings;
        Owner = owner;
        Title = "Companion · " + key;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1C));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x90, 0xA0, 0xB0));
        BorderThickness = new Thickness(1);
        Topmost = settings.OnTop;
        MinWidth = 300;
        MinHeight = 240;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        this.UseText();

        var saved = settings.PageWindows.GetValueOrDefault(key);
        var scaleNow = Display.UiScale.Factor(settings.ScaleOf("Page " + key));
        bool placed = saved is not null && saved.Width >= MinWidth * scaleNow && saved.Height >= MinHeight * scaleNow;
        var b = placed ? saved! : new Bounds(double.NaN, double.NaN, 440, 580);
        Width = b.Width;
        Height = b.Height;
        Left = double.IsNaN(b.Left) ? owner.Left + 36 + 30 * cascade : b.Left;
        Top = double.IsNaN(b.Top) ? owner.Top + 60 + 30 * cascade : b.Top;
        KeepOnScreen();

        var caption = new TextBlock { Text = key, Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        _who = new TextBlock { Foreground = Palette.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
        var close = TimersWindow.TitleButton("", "Close this window. The page is still in the small window.", () => { ClosedByPlayer = true; Close(); });
        _pin = TimersWindow.TitleButton("", "Keep this window on top of the game", () => { Topmost = !Topmost; ShowPin(); });
        var look = TimersWindow.TitleButton("", "Size and font. Ctrl and the mouse wheel over this window changes its size too.", () => LookWindow.Open(this, settings));
        var title = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)), Height = 32, Cursor = Cursors.SizeAll, LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_pin, Dock.Right);
        DockPanel.SetDock(look, Dock.Right);
        DockPanel.SetDock(caption, Dock.Left);
        DockPanel.SetDock(_who, Dock.Left);
        title.Children.Add(close);
        title.Children.Add(_pin);
        title.Children.Add(look);
        title.Children.Add(caption);
        title.Children.Add(_who);
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); };

        status.Margin = new Thickness(0);
        var bottom = new Border
        {
            Background = (Brush)Ui.Theme["Panel"], BorderBrush = (Brush)Ui.Theme["Stroke"], BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 5, 12, 5), Child = status,
        };
        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(title);
        root.Children.Add(bottom);
        root.Children.Add(page.Element);
        Content = root;
        WindowScale.Attach(this, settings, "Page " + key, key + " window", sizeIsSaved: placed);

        ShowPin();
        ShowSelection();
        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
        Loaded += (_, _) => page.Refresh();
    }

    /// <summary>The character the page is about, in the title bar.</summary>
    public void ShowSelection() => _who.Text = _store.Selected is { } s ? $"{s.Character} · {s.ServerName}" : "No ledger";

    public bool NeedsHistory => Page.NeedsHistory;

    public void Refresh()
    {
        ShowSelection();
        Page.Refresh();
    }

    public void Tick() => Page.Tick();

    private void ShowPin() => _pin.Foreground = Topmost ? new SolidColorBrush(Palette.AccentColor) : new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x88));

    private void Remember()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.PageWindows[Key] = new Bounds(Left, Top, Width, Height);
        _saveSoon ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _saveSoon!.Stop(); _settings.Save(); }, Dispatcher);
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    /// <summary>Saved on another monitor layout: pull it back onto the desktop.</summary>
    private void KeepOnScreen()
    {
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, l, Math.Max(l, r - 120));
        Top = Math.Clamp(Top, t, Math.Max(t, b - 60));
    }
}
