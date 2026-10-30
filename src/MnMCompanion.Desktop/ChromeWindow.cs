using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MnMCompanion;

/// <summary>
/// A window with no frame of the system's: its own title bar (any control handed to <see cref="DragBy"/> moves it) and a thin edge all round that resizes it,
/// as the Windows build's borderless windows have. It remembers where it was (<see cref="Bounds"/> in the settings) and is put back there, pulled onto a
/// screen if the screens have changed since. (On Wayland a program may not place its own windows; the compositor puts it where it likes.)
/// </summary>
internal class ChromeWindow : Window
{
    private const double Edge = 5;
    private readonly Grid _frame = new();
    private readonly ContentControl _body = new();
    private DispatcherTimer? _saveSoon;

    protected ChromeWindow()
    {
        WindowDecorations = WindowDecorations.None;
        CanResize = true;
        UseLayoutRounding = true;
        FontFamily = Ui.Text;
        Background = Palette.Freeze(Color.FromRgb(0x12, 0x16, 0x1C));
        _frame.Children.Add(_body);
        base.Content = new Border { BorderBrush = Palette.Freeze(Color.FromArgb(0x80, 0x90, 0xA0, 0xB0)), BorderThickness = new Thickness(1), Child = _frame };
        AddEdge(WindowEdge.North, StandardCursorType.TopSide, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, Edge);
        AddEdge(WindowEdge.South, StandardCursorType.BottomSide, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, Edge);
        AddEdge(WindowEdge.West, StandardCursorType.LeftSide, HorizontalAlignment.Left, VerticalAlignment.Stretch, Edge, double.NaN);
        AddEdge(WindowEdge.East, StandardCursorType.RightSide, HorizontalAlignment.Right, VerticalAlignment.Stretch, Edge, double.NaN);
        AddEdge(WindowEdge.NorthWest, StandardCursorType.TopLeftCorner, HorizontalAlignment.Left, VerticalAlignment.Top, Edge * 2, Edge * 2);
        AddEdge(WindowEdge.NorthEast, StandardCursorType.TopRightCorner, HorizontalAlignment.Right, VerticalAlignment.Top, Edge * 2, Edge * 2);
        AddEdge(WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner, HorizontalAlignment.Left, VerticalAlignment.Bottom, Edge * 2, Edge * 2);
        AddEdge(WindowEdge.SouthEast, StandardCursorType.BottomRightCorner, HorizontalAlignment.Right, VerticalAlignment.Bottom, Edge * 3, Edge * 3);
    }

    /// <summary>What the window shows, inside its edge.</summary>
    protected Control? Body
    {
        get => _body.Content as Control;
        set => _body.Content = value;
    }

    private void AddEdge(WindowEdge edge, StandardCursorType cursor, HorizontalAlignment h, VerticalAlignment v, double width, double height)
    {
        var grip = new Border { Background = Ui.Transparent, HorizontalAlignment = h, VerticalAlignment = v, Width = width, Height = height, Cursor = new Cursor(cursor), ZIndex = 10 };
        grip.PointerPressed += (_, e) =>
        {
            if (!CanResize || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginResizeDrag(edge, e);
            e.Handled = true;
        };
        _frame.Children.Add(grip);
    }

    /// <summary>Pressing on <paramref name="bar"/> (not on a button in it) and dragging moves the window.</summary>
    protected void DragBy(Control bar)
    {
        bar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is Visual source && IsInButton(source, bar)) return;
            BeginMoveDrag(e);
        };
    }

    private static bool IsInButton(Visual source, Visual stop)
    {
        for (Visual? v = source; v is not null && v != stop; v = v.GetVisualParent())
            if (v is Avalonia.Controls.Primitives.ToggleButton or Button or TextBox) return true;
        return false;
    }

    /// <summary>Where the window is now, to be kept in the settings.</summary>
    public Bounds Placement => new(Position.X, Position.Y, Width, Height);

    /// <summary>
    /// Sizes and places the window as it was (<paramref name="saved"/>), or at <paramref name="fallback"/> (given the screen's working area) the first time.
    /// Pulled back onto a screen when it was saved on one that is gone.
    /// </summary>
    protected void Place(Bounds? saved, double width, double height, Func<PixelRect, PixelPoint> fallback)
    {
        Width = saved?.Width ?? width;
        Height = saved?.Height ?? height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var work = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var at = saved is { } b && !double.IsNaN(b.Left) && !double.IsNaN(b.Top) ? new PixelPoint((int)b.Left, (int)b.Top) : fallback(work);
        Position = KeepOnScreen(at);
    }

    /// <summary>Saved on another monitor layout: pull it back onto the desktop.</summary>
    private PixelPoint KeepOnScreen(PixelPoint at)
    {
        var all = Screens.All;
        if (all.Count == 0) return at;
        int l = all.Min(s => s.Bounds.X), t = all.Min(s => s.Bounds.Y), r = all.Max(s => s.Bounds.Right), b = all.Max(s => s.Bounds.Bottom);
        return new PixelPoint(Math.Clamp(at.X, l, Math.Max(l, r - 120)), Math.Clamp(at.Y, t, Math.Max(t, b - 60)));
    }

    /// <summary>Calls <paramref name="remember"/> whenever the window is moved or resized, then saves the settings a moment after the last change.</summary>
    protected void RememberIn(Settings settings, Action<Bounds> remember)
    {
        void Changed()
        {
            if (WindowState != WindowState.Normal || !IsVisible) return;
            remember(Placement);
            _saveSoon ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _saveSoon!.Stop(); settings.Save(); });
            _saveSoon.Stop();
            _saveSoon.Start();
        }
        PositionChanged += (_, _) => Changed();
        Resized += (_, _) => Changed();
    }
}

internal static class VisualParents
{
    public static Visual? GetVisualParent(this Visual v) => Avalonia.VisualTree.VisualExtensions.GetVisualParent(v);
}
