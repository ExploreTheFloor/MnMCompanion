using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using MnMCompanion.Loot;
using MnMCompanion.Timers;

namespace MnMCompanion;

/// <summary>
/// The timers window: the timers the player started, counting down, and (when they have turned it on) one for every corpse looted. Companion reads
/// only the ledger and the journal and the game writes no cast to either, so it cannot know when a spell was cast: the player starts a timer with the
/// key they gave it (see <see cref="HotkeyHost"/>) or from the setup window. Names, lengths, keys, sounds and the corpse timer are set in
/// <see cref="TimerSetupWindow"/>; this window is only the bars and one button, which opens it. A timer goes gold for its last half minute, flashes
/// red for the last ten seconds, and plays its own sound (if it has one) when it ends.
/// Timers are kept per character, as the moment they end, so they carry on if the app is closed. Closing this window only hides it: the timers
/// (and the keys, and the beeps) keep going.
/// </summary>
internal sealed class TimersWindow : Window
{
    private static readonly Brush BarBack = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Amber = Palette.Gold;

    private readonly Settings _settings;
    private readonly Func<string?> _folder;
    private readonly Func<IReadOnlyList<LootedCorpse>>? _corpses;
    private readonly HotkeyHost? _hotkeys;
    private TimerBook _book = new();
    private string? _loadedFolder;
    private bool _followed;
    private TimerSetupWindow? _setup;
    private readonly Button _pin;
    private readonly DockPanel _title;
    private readonly Border _bottom;
    private readonly StackPanel _rows = new();
    private readonly Dictionary<string, Row> _live = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel _corpseRows = new();
    private readonly Dictionary<string, Row> _corpseLive = new(StringComparer.Ordinal);
    private readonly TextBlock _corpseHeader = new() { Text = "Corpses", Style = (Style)Ui.Theme["Label"], Margin = new Thickness(0, 8, 0, 4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _corpseMore = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(2, 0, 0, 4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _empty;
    private readonly TextBlock _message = new() { FontSize = 11, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    private DispatcherTimer? _saveSoon;

    private sealed class Row
    {
        public required Border Box { get; init; }
        public required TextBlock Name { get; init; }
        public required TextBlock Clock { get; init; }
        public required ProgressBar Bar { get; init; }
        public TimerPhase Phase;
    }

    /// <summary>The timers were read again (another character is being played): the setup window shows the new ones.</summary>
    public event Action? BookReplaced;

    /// <param name="corpses">The corpses looted lately, when asked for (only while the corpse timer is on); null for none (a picture, a test).</param>
    public TimersWindow(Settings settings, Func<string?> stateFolder, Func<IReadOnlyList<LootedCorpse>>? corpses = null)
    {
        _settings = settings;
        _folder = stateFolder;
        _corpses = corpses;
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = "Companion timers";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true;   // so the background can be faded (see ApplyLook); the bars and words stay solid
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1C));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x90, 0xA0, 0xB0));
        BorderThickness = new Thickness(1);
        Topmost = settings.TimersOnTop;
        MinWidth = 250;
        MinHeight = 110;
        UseLayoutRounding = true;
        ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var b = settings.Timers ?? new Bounds(double.NaN, double.NaN, 300, 300);
        Width = b.Width;
        Height = b.Height;
        var work = SystemParameters.WorkArea;
        Left = double.IsNaN(b.Left) ? work.Right - Width - 520 : b.Left;
        Top = double.IsNaN(b.Top) ? work.Top + 80 : b.Top;
        KeepOnScreen();

        var close = TitleButton("\uE711", "Hide this window. Timers keep running and keep playing their sounds.", HideWindow);
        _pin = TitleButton("\uE718", "Keep this window on top of the game", TogglePin);
        var opacity = OpacityButton();
        var caption = new TextBlock { Text = "Timers", Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        var title = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)), Height = 30, Cursor = Cursors.SizeAll, LastChildFill = false };
        _title = title;
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_pin, Dock.Right);
        DockPanel.SetDock(opacity, Dock.Right);
        DockPanel.SetDock(caption, Dock.Left);
        title.Children.Add(close);
        title.Children.Add(_pin);
        title.Children.Add(opacity);
        title.Children.Add(caption);
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); };

        _empty = new TextBlock
        {
            Text = "Nothing running. Press a timer's key to start it. Set up… is where timers, keys and sounds are made, and where a timer on every corpse you loot is turned on.",
            FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6),
        };

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0) };
        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 8) };
        body.Children.Add(_empty);
        body.Children.Add(_rows);
        body.Children.Add(_corpseHeader);
        body.Children.Add(_corpseRows);
        body.Children.Add(_corpseMore);
        scroll.Content = body;

        var bottom = BuildBottom();
        _bottom = (Border)bottom;
        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(title);
        root.Children.Add(bottom);
        root.Children.Add(scroll);
        Content = root;
        ApplyLook();

        if (!settings.ReadOnly) _hotkeys = MakeHotkeys();

        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
        Closed += (_, _) => _hotkeys?.Dispose();
        var tick = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => Tick(), Dispatcher);
        tick.Start();
        Tick();
    }

    /// <summary>Shows the window if it is hidden, hides it if it is showing.</summary>
    public void Toggle()
    {
        if (IsVisible) { HideWindow(); return; }
        Show();
        Activate();
        SetOpen(true);
    }

    private void HideWindow()
    {
        Hide();
        SetOpen(false);
    }

    /// <summary>Remembers whether the window was open, so it comes back the same way (not set by the app shutting down).</summary>
    private void SetOpen(bool open)
    {
        _settings.TimersOpen = open;
        _settings.Save();
    }

    // ------------------------------------------------------------------ on top, and how see-through

    private void TogglePin()
    {
        Topmost = !Topmost;
        _settings.TimersOnTop = Topmost;
        _settings.Save();
        ApplyLook();
    }

    /// <summary>The button of the title bar that opens the menu of opacity levels: a half-filled circle and its name.</summary>
    private Button OpacityButton()
    {
        var picture = new TextBlock { Text = "◐", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var words = new TextBlock { Text = "Opacity", FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 9, 0) };
        content.Children.Add(picture);
        content.Children.Add(words);
        var button = new Button
        {
            Content = content, Style = (Style)Ui.Theme["IconButton"], Width = double.NaN, MinWidth = 30, Height = 24, Margin = new Thickness(1, 0, 1, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xE0)), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "How see-through the window is behind its bars and words, so the game shows through",
        };
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            menu.Items.Add(new MenuItem { Header = "Background", IsEnabled = false });
            int current = TimerLook.Nearest(_settings.TimersOpacity);
            foreach (var level in TimerLook.OpacityLevels)
            {
                var item = new MenuItem { Header = level == 100 ? "Solid" : $"{level}% solid", IsChecked = level == current };
                int chosen = level;
                item.Click += (_, _) => SetOpacity(chosen);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return button;
    }

    private void SetOpacity(int percent)
    {
        _settings.TimersOpacity = percent;
        _settings.Save();
        ApplyLook();
    }

    /// <summary>
    /// Paints the window for the pin and the opacity chosen: only the background fades. The title bar keeps enough of itself to be grabbed, and the pin is
    /// the accent colour while the window is on top (as the main window's is).
    /// </summary>
    private void ApplyLook()
    {
        int percent = _settings.TimersOpacity;
        Background = new SolidColorBrush(Color.FromArgb(TimerLook.Alpha(percent), 0x12, 0x16, 0x1C));
        _bottom.Background = new SolidColorBrush(Color.FromArgb(TimerLook.Alpha(percent), 0x16, 0x19, 0x20));
        _title.Background = new SolidColorBrush(Color.FromArgb(TimerLook.TitleAlpha(percent), 0x10, 0x14, 0x18));
        _pin.Foreground = Topmost ? new SolidColorBrush(Palette.AccentColor) : new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x88));
        _pin.ToolTip = Topmost ? "On top of the game. Click to let other windows cover it." : "Not on top. Click to keep this window on top of the game.";
    }

    // ------------------------------------------------------------------ the setup window and what it changes

    /// <summary>The timers of the character being played; the setup window edits this book and calls <see cref="BookEdited"/>.</summary>
    internal TimerBook Book => _book;

    /// <summary>Why keys can't be used on this PC (the listener couldn't be made), or null.</summary>
    internal string? KeysUnavailable { get; private set; }

    /// <summary>What Windows refused when the keys were taken (another program has them), one sentence each.</summary>
    internal IReadOnlyList<string> KeyProblems { get; private set; } = [];

    /// <summary>Opens the setup window, or brings it forward. Keys are let go while it is open, so a key can be pressed to be recorded.</summary>
    public void OpenSetup()
    {
        if (_setup is { } open)
        {
            open.Activate();
            return;
        }
        _setup = new TimerSetupWindow(this, _settings);
        _setup.Closed += (_, _) =>
        {
            _setup = null;
            ResumeKeys();
        };
        SuspendKeys();
        _setup.Show();
        _setup.Activate();
    }

    /// <summary>The setup window changed the timers: keep them, show them, and take the keys again once it closes.</summary>
    internal void BookEdited()
    {
        SaveBook();
        RegisterKeys();
        Tick();
    }

    /// <summary>Starts the timer called <paramref name="name"/> (or starts it over), as its key does. False if there is no such timer or too many are running.</summary>
    internal bool StartByName(string name)
    {
        if (_book.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } preset) return false;
        return Start(preset.Name, preset.Seconds);
    }

    internal void SuspendKeys() => _hotkeys?.Suspend();

    internal void ResumeKeys()
    {
        if (_hotkeys is null) return;
        KeyProblems = _hotkeys.Resume().Values.ToList();
        Say(string.Join(" ", KeyProblems));
    }

    // ------------------------------------------------------------------ keys

    private HotkeyHost? MakeHotkeys()
    {
        try
        {
            var host = new HotkeyHost();
            host.Pressed += name => StartByName(name);
            return host;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            KeysUnavailable = "Keys can't be used here (" + ex.GetType().Name + "). Timers can still be started by clicking them.";
            return null;
        }
    }

    /// <summary>Takes the keys of the timers that have one. Anything Windows refuses is said in the message line.</summary>
    private void RegisterKeys()
    {
        if (_hotkeys is null) return;
        var keys = _book.Presets
            .Select(p => (p.Name, Key: Hotkey.TryParse(p.Key)))
            .Where(k => k.Key is not null)
            .Select(k => (k.Name, Key: k.Key!))
            .ToList();
        KeyProblems = _hotkeys.Apply(keys).Values.ToList();
        Say(string.Join(" ", KeyProblems));
    }

    // ------------------------------------------------------------------ the bottom

    /// <summary>The bottom of the window: one button, which opens the setup. (A line above it says what Windows refused, if it refused a key.)</summary>
    private FrameworkElement BuildBottom()
    {
        var setup = Pill("Set up…", "Name your timers, give them keys and sounds, and turn on a timer for every corpse you loot", OpenSetup);
        setup.HorizontalAlignment = HorizontalAlignment.Left;
        var stack = new StackPanel();
        stack.Children.Add(_message);
        stack.Children.Add(setup);
        return new Border
        {
            Background = (Brush)Ui.Theme["Panel"], BorderBrush = (Brush)Ui.Theme["Stroke"], BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 6, 10, 6), Child = stack,
        };
    }

    private bool Start(string name, int seconds)
    {
        if (_book.Start(name, seconds, DateTime.UtcNow) is null)
        {
            Say($"Too many timers are running ({TimerBook.MaxRunning}).");
            return false;
        }
        SaveBook();
        Tick();
        return true;
    }

    private void Say(string text)
    {
        _message.Text = text;
        _message.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------ the running timers

    /// <summary>Four times a second: follows the character being played, tidies ended timers and redraws the running ones.</summary>
    private void Tick()
    {
        var now = DateTime.UtcNow;
        FollowCharacter();
        if (_book.Prune(now)) SaveBook();
        Render(now);
    }

    private void FollowCharacter()
    {
        var folder = _folder();
        if (_followed && string.Equals(folder, _loadedFolder, StringComparison.OrdinalIgnoreCase)) return;
        _followed = true;
        _book = TimerBook.Load(folder, DateTime.UtcNow);
        _loadedFolder = folder;
        _live.Clear();
        _rows.Children.Clear();
        _corpseLive.Clear();
        _corpseRows.Children.Clear();
        RegisterKeys();
        BookReplaced?.Invoke();
    }

    private void Render(DateTime nowUtc)
    {
        bool blinkOn = nowUtc.Ticks / (TimeSpan.TicksPerMillisecond * 400) % 2 == 0;
        var timers = _book.ByEnd();
        Sync(_rows, _live, timers.Select(t => (t.Name, t.Name, TimerBook.Remaining(t, nowUtc), t.Seconds)).ToList(), blinkOn, stoppable: true, soundOf: _book.SoundOf, throttled: false);

        // The corpse timers are worked out from the loot each time; only while the player wants them
        var nowLocal = DateTime.Now;
        var corpses = _book.Corpse.On && _corpses is not null ? CorpseTimers.Active(_corpses(), _book.Corpse, nowLocal) : [];
        var shown = corpses.Take(CorpseTimers.MaxShown).Select(c => (c.Id, c.Label, (c.EndsLocal - nowLocal).TotalSeconds, c.Seconds)).ToList();
        Sync(_corpseRows, _corpseLive, shown, blinkOn, stoppable: false, soundOf: _ => _book.Corpse.Sound, throttled: true);
        _corpseHeader.Visibility = shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _corpseMore.Visibility = corpses.Count > shown.Count ? Visibility.Visible : Visibility.Collapsed;
        _corpseMore.Text = $"+{corpses.Count - shown.Count} more, ending later";
        _empty.Visibility = timers.Count == 0 && shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Makes the rows of <paramref name="panel"/> match <paramref name="items"/> (in that order): new ones made, gone ones taken away, the rest redrawn.</summary>
    private void Sync(StackPanel panel, Dictionary<string, Row> live, IReadOnlyList<(string Id, string Label, double Remaining, int Seconds)> items, bool blinkOn, bool stoppable, Func<string, string?> soundOf, bool throttled)
    {
        var ids = items.Select(i => i.Id).ToHashSet(live.Comparer);
        foreach (var gone in live.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            panel.Children.Remove(live[gone].Box);
            live.Remove(gone);
        }
        for (int i = 0; i < items.Count; i++)
        {
            var (id, label, remaining, seconds) = items[i];
            var phase = TimerClock.Phase(remaining);
            if (!live.TryGetValue(id, out var row))
            {
                row = MakeRow(id, stoppable);
                row.Phase = phase;   // a timer met in the middle of its life (after a restart) doesn't beep for the stage it is already past
                live[id] = row;
            }
            if (panel.Children.IndexOf(row.Box) != i)
            {
                panel.Children.Remove(row.Box);
                panel.Children.Insert(Math.Min(i, panel.Children.Count), row.Box);
            }
            if (phase != row.Phase)
            {
                if (phase == TimerPhase.Done) TimerSoundPlayer.Play(soundOf(id), throttled);
                row.Phase = phase;
            }

            row.Name.Text = label;
            row.Clock.Text = phase == TimerPhase.Done ? "Ended" : TimerClock.Format(remaining);
            row.Bar.Value = phase == TimerPhase.Done ? 1 : Math.Clamp(remaining / Math.Max(1, seconds), 0, 1);
            var colour = phase switch { TimerPhase.Warning => Amber, TimerPhase.Ending or TimerPhase.Done => Palette.Taken, _ => Palette.You };
            row.Bar.Foreground = colour;
            row.Clock.Foreground = phase == TimerPhase.Running ? Palette.Text : colour;
            row.Clock.Opacity = phase == TimerPhase.Ending && !blinkOn ? 0.35 : 1;
        }
    }

    /// <summary>One bar. A timer the player started can be stopped or started over; a corpse timer just runs out.</summary>
    private Row MakeRow(string id, bool stoppable)
    {
        var label = new TextBlock { FontSize = 13, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var clock = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, stoppable ? 4 : 8, 0) };
        var bar = new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, Background = BarBack, BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 0), Foreground = Palette.You };

        var top = new DockPanel();
        DockPanel.SetDock(clock, Dock.Right);
        if (stoppable)
        {
            var stop = RowButton("\uE711", "Stop this timer", () => { _book.Stop(id); SaveBook(); Tick(); });
            DockPanel.SetDock(stop, Dock.Right);
            top.Children.Add(stop);
        }
        top.Children.Add(clock);
        top.Children.Add(label);
        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(bar);
        var box = new Border
        {
            Background = (Brush)Ui.Theme["Subtle"], CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 6, 7), Margin = new Thickness(0, 0, 0, 5), Child = stack,
        };
        if (stoppable)
        {
            box.ToolTip = "Right-click to start it over";
            var menu = new ContextMenu();
            var again = new MenuItem { Header = "Start over" };
            again.Click += (_, _) =>
            {
                if (_book.Running.FirstOrDefault(r => string.Equals(r.Name, id, StringComparison.OrdinalIgnoreCase)) is { } running) Start(running.Name, running.Seconds);
            };
            menu.Items.Add(again);
            box.ContextMenu = menu;
        }
        return new Row { Box = box, Name = label, Clock = clock, Bar = bar };
    }

    // ------------------------------------------------------------------ small things

    private void SaveBook() => _book.Save(_loadedFolder ?? _folder());

    private Button Pill(string text, string tip, Action click)
    {
        var button = new Button { Content = text, Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(14, 5, 14, 5), ToolTip = tip };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A button of a title bar: a picture big enough to see, with the theme's hover highlight.</summary>
    internal static Button TitleButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Content = glyph, Style = (Style)Ui.Theme["IconButton"], Width = 30, Height = 24, Margin = new Thickness(1, 0, 1, 0), FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xE0)), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center,
        };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>The small button on a timer's bar.</summary>
    private static Button RowButton(string glyph, string tip, Action click)
    {
        var b = new Button { Content = glyph, Style = (Style)Ui.Theme["IconButton"], Width = 24, Height = 22, FontSize = 11, ToolTip = tip, Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA1)) };
        b.Click += (_, _) => click();
        return b;
    }

    private static Brush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private void Remember()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.Timers = new Bounds(Left, Top, Width, Height);
        _saveSoon ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _saveSoon!.Stop(); _settings.Save(); }, Dispatcher);
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    /// <summary>Saved on another monitor layout: pull it back onto the desktop.</summary>
    private void KeepOnScreen()
    {
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, bottom = t + SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, l, Math.Max(l, r - 120));
        Top = Math.Clamp(Top, t, Math.Max(t, bottom - 60));
    }
}
