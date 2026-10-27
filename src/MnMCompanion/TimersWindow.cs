using System.Media;
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
/// only the ledger and the journal and the game writes no cast to either, so it cannot know when a spell was cast: the player starts a timer with a
/// click or with the key they gave it (see <see cref="HotkeyHost"/>). Names, lengths, keys and the corpse timer are set in <see cref="TimerSetupWindow"/>.
/// A timer goes gold for its last half minute, flashes red for the last ten seconds, and can beep at both (not the corpse ones, which are many).
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
    private readonly StackPanel _rows = new();
    private readonly Dictionary<string, Row> _live = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel _corpseRows = new();
    private readonly Dictionary<string, Row> _corpseLive = new(StringComparer.Ordinal);
    private readonly TextBlock _corpseHeader = new() { Text = "Corpses", Style = (Style)Ui.Theme["Label"], Margin = new Thickness(0, 8, 0, 4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _corpseMore = new() { FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(2, 0, 0, 4), Visibility = Visibility.Collapsed };
    private readonly WrapPanel _presets = new();
    private readonly TextBlock _empty;
    private readonly TextBlock _message = new() { FontSize = 11, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
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
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1C));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x90, 0xA0, 0xB0));
        BorderThickness = new Thickness(1);
        Topmost = settings.OnTop;
        MinWidth = 250;
        MinHeight = 190;
        UseLayoutRounding = true;
        ShowInTaskbar = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var b = settings.Timers ?? new Bounds(double.NaN, double.NaN, 300, 400);
        Width = b.Width;
        Height = b.Height;
        var work = SystemParameters.WorkArea;
        Left = double.IsNaN(b.Left) ? work.Right - Width - 520 : b.Left;
        Top = double.IsNaN(b.Top) ? work.Top + 80 : b.Top;
        KeepOnScreen();

        var close = TitleButton("", "Hide this window. Timers keep running, and keep beeping if Sound is on.", HideWindow);
        var caption = new TextBlock { Text = "Timers", Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        var title = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)), Height = 24, Cursor = Cursors.SizeAll, LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(caption, Dock.Left);
        title.Children.Add(close);
        title.Children.Add(caption);
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); };

        _empty = new TextBlock
        {
            Text = "Nothing running. Click a timer below, or press its key, to start it. Set up… adds timers and keys, and can start a timer on every corpse you loot. " +
                   "The game doesn't tell Companion when you cast something, so you start those yourself.",
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
        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(title);
        root.Children.Add(bottom);
        root.Children.Add(scroll);
        Content = root;

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
        RebuildPresets();
        RegisterKeys();
        Tick();
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
            host.Pressed += name =>
            {
                if (_book.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { } preset) Start(preset.Name, preset.Seconds);
            };
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

    // ------------------------------------------------------------------ the bottom: the timers to hand

    private FrameworkElement BuildBottom()
    {
        var setup = Pill("Set up…", "Name your timers, give them keys, and turn on a timer for every corpse you loot", OpenSetup);
        var sound = new CheckBox
        {
            Content = "Sound", IsChecked = _settings.TimerSound, Foreground = Palette.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Focusable = false,
            ToolTip = "A short beep when a timer is down to its last half minute and when it ends (not for the corpse timers)",
        };
        sound.Click += (_, _) => { _settings.TimerSound = sound.IsChecked == true; _settings.Save(); };

        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(sound, Dock.Right);
        buttons.Children.Add(sound);
        buttons.Children.Add(setup);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Start", Style = (Style)Ui.Theme["Label"], Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 120, Content = _presets, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(buttons);
        stack.Children.Add(_message);
        return new Border
        {
            Background = (Brush)Ui.Theme["Panel"], BorderBrush = (Brush)Ui.Theme["Stroke"], BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 8), Child = stack,
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

    private void Say(string text) => _message.Text = text;

    private void RebuildPresets()
    {
        _presets.Children.Clear();
        foreach (var preset in _book.Presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var p = preset;
            var text = new TextBlock();
            text.Inlines.Add(new System.Windows.Documents.Run($"{p.Name}   {TimerClock.Spell(p.Seconds)}"));
            if (p.Key is not null) text.Inlines.Add(new System.Windows.Documents.Run("   " + p.Key) { Foreground = Palette.Muted, FontSize = 11 });
            var button = new Button
            {
                Content = text, Style = (Style)Ui.Theme["TextButton"], Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(9, 4, 9, 4),
                ToolTip = p.Key is null ? "Click to start (or start over). Right-click to edit." : $"Click or press {p.Key} to start (or start over). Right-click to edit.",
            };
            button.Click += (_, _) => { Say(""); Start(p.Name, p.Seconds); };
            var menu = new ContextMenu();
            var again = new MenuItem { Header = "Start" };
            again.Click += (_, _) => Start(p.Name, p.Seconds);
            var edit = new MenuItem { Header = "Set up…" };
            edit.Click += (_, _) => OpenSetup();
            menu.Items.Add(again);
            menu.Items.Add(edit);
            button.ContextMenu = menu;
            _presets.Children.Add(button);
        }
        if (_presets.Children.Count == 0)
            _presets.Children.Add(new TextBlock { Text = "No timers yet. Press Set up… to name one, give it a length and, if you like, a key.", FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
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
        RebuildPresets();
        RegisterKeys();
        BookReplaced?.Invoke();
    }

    private void Render(DateTime nowUtc)
    {
        bool blinkOn = nowUtc.Ticks / (TimeSpan.TicksPerMillisecond * 400) % 2 == 0;
        var timers = _book.ByEnd();
        Sync(_rows, _live, timers.Select(t => (t.Name, t.Name, TimerBook.Remaining(t, nowUtc), t.Seconds)).ToList(), blinkOn, stoppable: true);

        // The corpse timers are worked out from the loot each time; only while the player wants them
        var nowLocal = DateTime.Now;
        var corpses = _book.Corpse.On && _corpses is not null ? CorpseTimers.Active(_corpses(), _book.Corpse, nowLocal) : [];
        var shown = corpses.Take(CorpseTimers.MaxShown).Select(c => (c.Id, c.Label, (c.EndsLocal - nowLocal).TotalSeconds, c.Seconds)).ToList();
        Sync(_corpseRows, _corpseLive, shown, blinkOn, stoppable: false);
        _corpseHeader.Visibility = shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _corpseMore.Visibility = corpses.Count > shown.Count ? Visibility.Visible : Visibility.Collapsed;
        _corpseMore.Text = $"+{corpses.Count - shown.Count} more, ending later";
        _empty.Visibility = timers.Count == 0 && shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Makes the rows of <paramref name="panel"/> match <paramref name="items"/> (in that order): new ones made, gone ones taken away, the rest redrawn.</summary>
    private void Sync(StackPanel panel, Dictionary<string, Row> live, IReadOnlyList<(string Id, string Label, double Remaining, int Seconds)> items, bool blinkOn, bool stoppable)
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
                if (stoppable && _settings.TimerSound)
                {
                    if (phase == TimerPhase.Warning && row.Phase == TimerPhase.Running) SystemSounds.Asterisk.Play();
                    else if (phase == TimerPhase.Done) SystemSounds.Exclamation.Play();
                }
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
            var stop = TitleButton("", "Stop this timer", () => { _book.Stop(id); SaveBook(); Tick(); });
            stop.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA1));
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

    internal static Button TitleButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Content = glyph, Width = 26, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 10,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xC0, 0xD0)),
            Cursor = Cursors.Hand, Focusable = false, ToolTip = tip,
        };
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
