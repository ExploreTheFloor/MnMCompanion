using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using MnMCompanion.Timers;

namespace MnMCompanion;

/// <summary>
/// The timers window: buff and cooldown timers the player starts by clicking. Companion reads only the ledger and the journal and the game
/// writes no cast to either, so it cannot know when a spell was cast; every timer is started by the player (a click on one they keep to hand, or a
/// name and a length typed in). It then counts down, goes gold for the last half minute, flashes red for the last ten seconds, and can beep at
/// both. Timers are kept per character, as the moment they end, so they carry on if the app is closed. Closing this window only hides it:
/// the timers (and the beeps) keep going.
/// </summary>
internal sealed class TimersWindow : Window
{
    private static readonly Brush BarBack = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Amber = Palette.Gold;

    private readonly Settings _settings;
    private readonly Func<string?> _folder;
    private TimerBook _book = new();
    private string? _loadedFolder;
    private bool _followed;
    private readonly StackPanel _rows = new();
    private readonly Dictionary<string, Row> _live = new(StringComparer.OrdinalIgnoreCase);
    private readonly WrapPanel _presets = new();
    private readonly TextBlock _empty;
    private readonly TextBlock _message = new() { FontSize = 11, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _name = new() { Style = (Style)Ui.Theme["Field"], MaxLength = TimerBook.MaxName, ToolTip = "What the timer is for: a spell, a cooldown, a respawn" };
    private readonly TextBox _length = new() { Style = (Style)Ui.Theme["Field"], MaxLength = 12, Width = 70, ToolTip = "How long: 90, 1:30, 5m, 1h30m" };
    private DispatcherTimer? _saveSoon;

    private sealed class Row
    {
        public required Border Box { get; init; }
        public required TextBlock Name { get; init; }
        public required TextBlock Clock { get; init; }
        public required ProgressBar Bar { get; init; }
        public TimerPhase Phase;
    }

    public TimersWindow(Settings settings, Func<string?> stateFolder)
    {
        _settings = settings;
        _folder = stateFolder;
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

        var b = settings.Timers ?? new Bounds(double.NaN, double.NaN, 300, 430);
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
            Text = "Nothing running. Click a timer below to start it, or type one in. The game doesn't tell Companion when you cast something, so you start each timer yourself.",
            FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6),
        };

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0) };
        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 8) };
        body.Children.Add(_empty);
        body.Children.Add(_rows);
        scroll.Content = body;

        var bottom = BuildForm();
        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(title);
        root.Children.Add(bottom);
        root.Children.Add(scroll);
        Content = root;

        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
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

    // ------------------------------------------------------------------ the form at the bottom

    private FrameworkElement BuildForm()
    {
        var start = Pill("Start", "Starts a timer with this name and length, without keeping it", () => StartTyped(save: false));
        var keep = Pill("Keep", "Keeps it in the list above so it starts with one click, and starts it", () => StartTyped(save: true));
        var sound = new CheckBox
        {
            Content = "Sound", IsChecked = _settings.TimerSound, Foreground = Palette.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Focusable = false,
            ToolTip = "A short beep when a timer is down to its last half minute and when it ends",
        };
        sound.Click += (_, _) => { _settings.TimerSound = sound.IsChecked == true; _settings.Save(); };
        _name.KeyDown += Enter;
        _length.KeyDown += Enter;

        var fields = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _length.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_length, 1);
        fields.Children.Add(_name);
        fields.Children.Add(_length);

        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(sound, Dock.Right);
        buttons.Children.Add(sound);
        start.Margin = new Thickness(0, 0, 6, 0);
        buttons.Children.Add(start);
        buttons.Children.Add(keep);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Start", Style = (Style)Ui.Theme["Label"], Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 96, Content = _presets, Margin = new Thickness(0, 0, 0, 2) });
        stack.Children.Add(new TextBlock { Text = "New timer  (a name and a length: 90, 1:30, 5m, 1h30m)", FontSize = 11, Foreground = Palette.Muted, Margin = new Thickness(0, 6, 0, 4) });
        stack.Children.Add(fields);
        stack.Children.Add(buttons);
        stack.Children.Add(_message);
        return new Border
        {
            Background = (Brush)Ui.Theme["Panel"], BorderBrush = (Brush)Ui.Theme["Stroke"], BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 8), Child = stack,
        };
    }

    private void Enter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        StartTyped(save: false);
    }

    /// <summary>Starts what is typed in the form; <paramref name="save"/> also keeps it in the list.</summary>
    private void StartTyped(bool save)
    {
        if (TimerBook.CleanName(_name.Text) is not { } name) { Say("Type a name for the timer."); _name.Focus(); return; }
        if (TimerClock.TryParse(_length.Text) is not { } seconds) { Say("Type a length such as 90, 1:30, 5m or 1h30m (up to 24h)."); _length.Focus(); return; }
        if (save && !_book.AddPreset(name, seconds)) { Say($"The list is full ({TimerBook.MaxPresets})."); return; }
        if (save) RebuildPresets();
        if (!Start(name, seconds)) return;
        Say(save ? $"Kept “{name}” and started it." : "");
        _name.Clear();
        _length.Clear();
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

    // ------------------------------------------------------------------ the timers to hand

    private void RebuildPresets()
    {
        _presets.Children.Clear();
        foreach (var preset in _book.Presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var p = preset;
            var button = new Button
            {
                Content = $"{p.Name}   {TimerClock.Spell(p.Seconds)}", Style = (Style)Ui.Theme["TextButton"], Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(9, 4, 9, 4),
                ToolTip = "Click to start (or start over). Right-click to edit or remove.",
            };
            button.Click += (_, _) => { Say(""); Start(p.Name, p.Seconds); };
            var menu = new ContextMenu();
            var again = new MenuItem { Header = "Start" };
            again.Click += (_, _) => Start(p.Name, p.Seconds);
            var edit = new MenuItem { Header = "Edit…", ToolTip = "Puts it in the form below; Keep saves your change" };
            edit.Click += (_, _) => { _name.Text = p.Name; _length.Text = TimerClock.Spell(p.Seconds); Say("Change it, then press Keep."); _length.Focus(); _length.SelectAll(); };
            var remove = new MenuItem { Header = "Remove from the list" };
            remove.Click += (_, _) => { _book.RemovePreset(p.Name); SaveBook(); RebuildPresets(); };
            menu.Items.Add(again);
            menu.Items.Add(edit);
            menu.Items.Add(new Separator());
            menu.Items.Add(remove);
            button.ContextMenu = menu;
            _presets.Children.Add(button);
        }
        if (_presets.Children.Count == 0)
            _presets.Children.Add(new TextBlock { Text = "Nothing kept yet. Type a name and a length below and press Keep.", FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
    }

    // ------------------------------------------------------------------ the running timers

    /// <summary>Twice a second or so: follows the character being played, tidies ended timers and redraws the running ones.</summary>
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
        RebuildPresets();
    }

    private void Render(DateTime now)
    {
        var timers = _book.ByEnd();
        var names = timers.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _live.Keys.Where(k => !names.Contains(k)).ToList())
        {
            _rows.Children.Remove(_live[gone].Box);
            _live.Remove(gone);
        }
        _empty.Visibility = timers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        bool blinkOn = now.Ticks / (TimeSpan.TicksPerMillisecond * 400) % 2 == 0;
        for (int i = 0; i < timers.Count; i++)
        {
            var t = timers[i];
            double remaining = TimerBook.Remaining(t, now);
            var phase = TimerClock.Phase(remaining);
            if (!_live.TryGetValue(t.Name, out var row))
            {
                row = MakeRow(t.Name);
                row.Phase = phase;   // a timer met in the middle of its life (after a restart) doesn't beep for the stage it is already past
                _live[t.Name] = row;
            }
            if (_rows.Children.IndexOf(row.Box) != i)
            {
                _rows.Children.Remove(row.Box);
                _rows.Children.Insert(Math.Min(i, _rows.Children.Count), row.Box);
            }
            if (phase != row.Phase)
            {
                if (_settings.TimerSound)
                {
                    if (phase == TimerPhase.Warning && row.Phase == TimerPhase.Running) SystemSounds.Asterisk.Play();
                    else if (phase == TimerPhase.Done) SystemSounds.Exclamation.Play();
                }
                row.Phase = phase;
            }

            row.Name.Text = t.Name;
            row.Clock.Text = phase == TimerPhase.Done ? "Ended" : TimerClock.Format(remaining);
            row.Bar.Value = phase == TimerPhase.Done ? 1 : Math.Clamp(remaining / Math.Max(1, t.Seconds), 0, 1);
            var colour = phase switch { TimerPhase.Warning => Amber, TimerPhase.Ending or TimerPhase.Done => Palette.Taken, _ => Palette.You };
            row.Bar.Foreground = colour;
            row.Clock.Foreground = phase == TimerPhase.Running ? Palette.Text : colour;
            row.Clock.Opacity = phase == TimerPhase.Ending && !blinkOn ? 0.35 : 1;
        }
    }

    private Row MakeRow(string name)
    {
        var label = new TextBlock { FontSize = 13, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var clock = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
        var stop = TitleButton("", "Stop this timer", () => { _book.Stop(name); SaveBook(); Tick(); });
        stop.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA1));
        var bar = new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, Background = BarBack, BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 0), Foreground = Palette.You };

        var top = new DockPanel();
        DockPanel.SetDock(stop, Dock.Right);
        DockPanel.SetDock(clock, Dock.Right);
        top.Children.Add(stop);
        top.Children.Add(clock);
        top.Children.Add(label);
        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(bar);
        var box = new Border
        {
            Background = (Brush)Ui.Theme["Subtle"], CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 6, 7), Margin = new Thickness(0, 0, 0, 5), Child = stack,
            ToolTip = "Right-click to start it over",
        };
        var menu = new ContextMenu();
        var again = new MenuItem { Header = "Start over" };
        again.Click += (_, _) =>
        {
            if (_book.Running.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) is { } running) Start(running.Name, running.Seconds);
        };
        menu.Items.Add(again);
        box.ContextMenu = menu;
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

    private static Button TitleButton(string glyph, string tip, Action click)
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
