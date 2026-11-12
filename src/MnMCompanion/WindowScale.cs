using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MnMCompanion.Display;

namespace MnMCompanion;

/// <summary>
/// The size of one window's contents, as the player set it (Ctrl + wheel over the window, Ctrl + and Ctrl −, Ctrl 0 for the normal size, or the Size and font
/// window). The contents are drawn through a scale, the window grows or shrinks with them so that it shows the same page only larger, and the size is
/// remembered by the kind of window (<see cref="Settings.WindowScales"/>), so the big window on the second screen stays big and the timers stay small.
/// Menus and tooltips open in their own little windows that a scale does not reach, so they are given the scale of the window they belong to
/// (<see cref="Register"/>). Nothing here reads or sends anything: it is how big things are drawn.
/// </summary>
internal sealed class WindowScale
{
    /// <summary>The factor a window is drawn at (1.0 normal), on the window itself; menus and tooltips read it from the window they belong to.</summary>
    public static readonly DependencyProperty FactorProperty = DependencyProperty.RegisterAttached("Factor", typeof(double), typeof(WindowScale), new PropertyMetadata(1.0));

    public static double FactorOf(DependencyObject window) => (double)window.GetValue(FactorProperty);

    /// <summary>Where the small dialogs (sharing, suggestions, what's new, timer setup) find the settings: set once the app has started for real, so a picture-taking run never reads the player's sizes.</summary>
    public static Settings? Shared { get; set; }

    private readonly Window _window;
    private readonly Settings _settings;
    private readonly double _minWidth, _minHeight, _maxHeight;
    private int _wheel;

    /// <summary>The kind of window this is (the name in <see cref="Settings.WindowScales"/>).</summary>
    public string Kind { get; }

    /// <summary>What the window is called in the Size and font window.</summary>
    public string Name { get; }

    public int Percent { get; private set; }

    /// <summary>The size changed.</summary>
    public event Action? Changed;

    private WindowScale(Window window, Settings settings, string key, string name)
    {
        _window = window;
        _settings = settings;
        Kind = key;
        Name = name;
        Percent = settings.ScaleOf(key);
        _minWidth = window.MinWidth;
        _minHeight = window.MinHeight;
        _maxHeight = window.MaxHeight;
    }

    /// <summary>
    /// Makes <paramref name="window"/> follow the size saved under <paramref name="key"/>. Call it once the window's content is set (a window whose content comes later is
    /// drawn right when it loads). <paramref name="sizeIsSaved"/> says the window's width and height are already the ones it had at that size (its saved place); otherwise
    /// they are the ones for 100% and are scaled here.
    /// </summary>
    public static WindowScale Attach(Window window, Settings settings, string key, string name, bool sizeIsSaved)
    {
        var scale = new WindowScale(window, settings, key, name);
        window.SetValue(Holder, scale);
        scale.Apply();
        if (!sizeIsSaved && scale.Percent != UiScale.Default)
        {
            if (window.SizeToContent is not (SizeToContent.Width or SizeToContent.WidthAndHeight)) window.Width = UiScale.Resized(window.Width, UiScale.Default, scale.Percent);
            if (window.SizeToContent is not (SizeToContent.Height or SizeToContent.WidthAndHeight)) window.Height = UiScale.Resized(window.Height, UiScale.Default, scale.Percent);
        }
        window.Loaded += (_, _) => scale.Apply();
        window.PreviewMouseWheel += scale.OnWheel;
        window.PreviewKeyDown += scale.OnKey;
        return scale;
    }

    /// <summary>The scale a window was attached with, or null.</summary>
    public static WindowScale? Of(Window window) => window.GetValue(Holder) as WindowScale;

    private static readonly DependencyProperty Holder = DependencyProperty.RegisterAttached("Holder", typeof(WindowScale), typeof(WindowScale));

    /// <summary>Gives every menu and tooltip the scale of the window it belongs to. Once, when the app starts.</summary>
    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((s, _) => Follow((ContextMenu)s)));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler((s, _) => Follow((ToolTip)s)));
        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.SubmenuOpenedEvent, new RoutedEventHandler((s, _) => FollowSubmenu((MenuItem)s)));
    }

    /// <summary>A submenu opens in a popup of its own: it gets the scale of the window the menu belongs to.</summary>
    private static void FollowSubmenu(MenuItem item)
    {
        if (item.Template?.FindName("PART_Popup", item) is not Popup { Child: FrameworkElement child }) return;
        DependencyObject owner = item;
        while (owner is MenuItem parent && ItemsControl.ItemsControlFromItemContainer(parent) is { } above) owner = above;
        var target = owner is ContextMenu menu ? menu.PlacementTarget : null;
        var window = target is null ? null : Window.GetWindow(target);
        var factor = window is null ? 1.0 : FactorOf(window);
        child.LayoutTransform = Math.Abs(factor - 1.0) < 0.001 ? Transform.Identity : new ScaleTransform(factor, factor);
    }

    private static void Follow(FrameworkElement popup)
    {
        var target = popup is ContextMenu menu ? menu.PlacementTarget : popup is ToolTip tip ? tip.PlacementTarget : null;
        var window = target is null ? null : Window.GetWindow(target);
        var factor = window is null ? 1.0 : FactorOf(window);
        popup.LayoutTransform = Math.Abs(factor - 1.0) < 0.001 ? Transform.Identity : new ScaleTransform(factor, factor);
    }

    /// <summary>Draws the contents at the current size (and keeps the window's smallest size in proportion).</summary>
    private void Apply()
    {
        var factor = UiScale.Factor(Percent);
        _window.SetValue(FactorProperty, factor);
        if (_window.Content is FrameworkElement content)
        {
            content.LayoutTransform = Math.Abs(factor - 1.0) < 0.001 ? Transform.Identity : new ScaleTransform(factor, factor);
            // Text is drawn crisper at the normal size in the "display" mode, smoother when it is scaled
            TextOptions.SetTextFormattingMode(_window, Math.Abs(factor - 1.0) < 0.001 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
        }
        _window.MinWidth = _minWidth * factor;
        _window.MinHeight = _minHeight * factor;
        if (!double.IsInfinity(_maxHeight)) _window.MaxHeight = _maxHeight * factor;
    }

    /// <summary>Sets the size. The window grows or shrinks to match (it stays on screen), the size is remembered, and <see cref="Changed"/> is raised.</summary>
    public void Set(int percent)
    {
        percent = UiScale.Clamp(percent);
        if (percent == Percent) return;
        var from = Percent;
        Percent = percent;
        Apply();
        if (_window.WindowState == WindowState.Normal && _window.IsLoaded)
        {
            if (_window.SizeToContent is not (SizeToContent.Width or SizeToContent.WidthAndHeight)) _window.Width = UiScale.Resized(_window.ActualWidth, from, percent);
            if (_window.SizeToContent is not (SizeToContent.Height or SizeToContent.WidthAndHeight)) _window.Height = UiScale.Resized(_window.ActualHeight, from, percent);
            KeepOnScreen();
        }
        _settings.SetScale(Kind, percent);
        _settings.Save();
        Changed?.Invoke();
    }

    public void Bigger() => Set(UiScale.Next(Percent, 1));

    public void Smaller() => Set(UiScale.Next(Percent, -1));

    public void Reset() => Set(UiScale.Default);

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || (Keyboard.Modifiers & ModifierKeys.Alt) != 0) return;
        e.Handled = true;
        // a smooth wheel sends small steps; one notch (120) is one step
        _wheel += e.Delta;
        while (Math.Abs(_wheel) >= 120)
        {
            if (_wheel > 0) { Bigger(); _wheel -= 120; }
            else { Smaller(); _wheel += 120; }
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || (Keyboard.Modifiers & ModifierKeys.Alt) != 0) return;
        switch (e.Key)
        {
            case Key.OemPlus or Key.Add: Bigger(); break;
            case Key.OemMinus or Key.Subtract: Smaller(); break;
            case Key.D0 or Key.NumPad0: Reset(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>After growing, pull the window back so it is not hanging off the desktop (its top-left corner stays where it is if it can).</summary>
    private void KeepOnScreen()
    {
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
        if (_window.Width > r - l) _window.Width = r - l;
        if (_window.Height > b - t) _window.Height = b - t;
        _window.Left = Math.Clamp(_window.Left, l, Math.Max(l, r - _window.Width));
        _window.Top = Math.Clamp(_window.Top, t, Math.Max(t, b - _window.Height));
    }
}
