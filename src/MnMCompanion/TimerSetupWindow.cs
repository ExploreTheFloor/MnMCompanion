using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using MnMCompanion.Timers;

namespace MnMCompanion;

/// <summary>
/// Where timers are made: each has a name, a length and, if the player likes, a key that starts it from anywhere (<see cref="HotkeyHost"/>) and a sound
/// it plays when it ends; and the corpse timer is turned on and given its name, length and sound. It edits the book of the character being played,
/// which <see cref="TimersWindow"/> shows and runs; every change is kept at once. Keys are let go while this window is open so a key can be pressed here
/// to be recorded.
/// </summary>
internal sealed class TimerSetupWindow : Window
{
    private static readonly Lazy<IReadOnlyList<TimerSound>> WindowsSounds = new(() => TimerSounds.WindowsFiles());

    private readonly TimersWindow _timers;
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MinHeight = 16 };
    private readonly CheckBox _corpseOn = new() { Content = "Start a timer on every corpse", Foreground = Palette.Text, FontSize = 12.5, Focusable = false };
    private readonly TextBox _corpseName = new() { Style = (Style)Ui.Theme["Field"], MaxLength = TimerBook.MaxName, ToolTip = "What the timer is called. The bar says this, then the monster: “Corpse: a bog crawler”" };
    private readonly TextBox _corpseLength = new() { Style = (Style)Ui.Theme["Field"], MaxLength = 12, Width = 80, ToolTip = "How long it runs from the moment the corpse was looted: 90, 1:30, 5m, 1h30m" };
    private readonly SoundField _corpseSound = new();
    private bool _loading;

    public TimerSetupWindow(TimersWindow timers, Settings settings)
    {
        _timers = timers;
        Resources.MergedDictionaries.Add(Ui.Theme);
        Title = "Companion timer setup";
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
        Width = 660;
        Height = 700;
        MinWidth = 580;
        MinHeight = 420;
        UseLayoutRounding = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var close = TimersWindow.TitleButton("", "Close. What you changed is already kept.", Close);
        var caption = new TextBlock { Text = "Timer setup", Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        var title = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)), Height = 24, Cursor = Cursors.SizeAll, LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(caption, Dock.Left);
        title.Children.Add(close);
        title.Children.Add(caption);
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); };

        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        body.Children.Add(Heading("Your timers"));
        body.Children.Add(Note("Each timer has a name and a length. It can also have a key that starts it from anywhere, the game included (click the key box, then press the keys; Escape or Delete empties it), " +
                               "and a sound for when it ends (pick one to hear it). The ▶ starts a timer from here."));
        body.Children.Add(_list);
        body.Children.Add(_status);
        body.Children.Add(KeysNote());
        body.Children.Add(Heading("Corpses", top: 20));
        body.Children.Add(BuildCorpseSection());

        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
        Content = root;

        _timers.BookReplaced += Rebuild;
        Closed += (_, _) => _timers.BookReplaced -= Rebuild;
        Rebuild();
    }

    // ------------------------------------------------------------------ your timers

    private TimerBook Book => _timers.Book;

    private void Rebuild()
    {
        _loading = true;
        _list.Children.Clear();
        _list.Children.Add(HeaderRow());
        for (int i = 0; i < Book.Presets.Count; i++) _list.Children.Add(PresetRow(i));
        _list.Children.Add(new TextBlock { Text = "New timer", Style = (Style)Ui.Theme["Label"], Margin = new Thickness(0, 12, 0, 4) });
        _list.Children.Add(NewRow());
        var corpse = Book.Corpse;
        _corpseOn.IsChecked = corpse.On;
        _corpseName.Text = corpse.Name;
        _corpseLength.Text = TimerClock.Spell(corpse.Seconds);
        _corpseSound.Set(corpse.Sound);
        _loading = false;
    }

    // Name, Length, Key, Sound, ▶, delete
    private static Grid Columns()
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 110 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        return g;
    }

    private static void Put(Grid g, FrameworkElement e, int column, int span = 1)
    {
        e.Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 0);
        Grid.SetColumn(e, column);
        Grid.SetColumnSpan(e, span);
        g.Children.Add(e);
    }

    private static Grid HeaderRow()
    {
        var g = Columns();
        g.Margin = new Thickness(0, 10, 0, 4);
        Put(g, Label("Name"), 0);
        Put(g, Label("Length"), 1);
        Put(g, Label("Key"), 2);
        Put(g, Label("Sound at the end"), 3, span: 3);
        return g;
    }

    private static Button IconAction(string glyph, string tip)
    {
        var b = new Button { Content = glyph, Style = (Style)Ui.Theme["IconButton"], ToolTip = tip, FontSize = 13 };
        return b;
    }

    private Grid PresetRow(int index)
    {
        var preset = Book.Presets[index];
        var name = new TextBox { Style = (Style)Ui.Theme["Field"], MaxLength = TimerBook.MaxName, Text = preset.Name };
        var length = new TextBox { Style = (Style)Ui.Theme["Field"], MaxLength = 12, Text = TimerClock.Spell(preset.Seconds), ToolTip = "90, 1:30, 5m, 1h30m" };
        var key = KeyBox(preset.Key);
        var sound = new SoundField();
        sound.Set(preset.Sound);
        var start = IconAction("", "Start this timer now");
        var remove = IconAction("", "Delete this timer");

        void Commit()
        {
            if (_loading || index >= Book.Presets.Count) return;
            var current = Book.Presets[index];
            int seconds = TimerClock.TryParse(length.Text) ?? 0;
            var keyText = (string?)key.Tag;
            if (TimerBook.CleanName(name.Text) == current.Name && seconds == current.Seconds && keyText == current.Key) return;
            if (Book.PresetProblem(index, name.Text, seconds, keyText) is { } problem)
            {
                Say(problem, error: true);
                name.Text = current.Name;
                length.Text = TimerClock.Spell(current.Seconds);
                SetKey(key, current.Key);
                return;
            }
            Book.SetPreset(index, name.Text, seconds, keyText);
            _timers.BookEdited();
            Say("");
            Rebuild();
        }

        name.LostFocus += (_, _) => Commit();
        length.LostFocus += (_, _) => Commit();
        name.KeyDown += (_, e) => EnterCommits(e, Commit);
        length.KeyDown += (_, e) => EnterCommits(e, Commit);
        key.Tag = preset.Key;
        key.KeyChosen += Commit;
        sound.Picked += () =>
        {
            Book.SetSound(index, sound.Sound);
            _timers.BookEdited();
        };
        start.Click += (_, _) =>
        {
            var current = Book.Presets[index];
            Say(_timers.StartByName(current.Name) ? $"Started “{current.Name}”." : "Too many timers are running.", error: false);
        };
        remove.Click += (_, _) =>
        {
            Book.Presets.RemoveAt(index);
            _timers.BookEdited();
            Say("");
            Rebuild();
        };

        var g = Columns();
        Put(g, name, 0);
        Put(g, length, 1);
        Put(g, key, 2);
        Put(g, sound, 3);
        Put(g, start, 4);
        Put(g, remove, 5);
        return g;
    }

    private Grid NewRow()
    {
        var name = new TextBox { Style = (Style)Ui.Theme["Field"], MaxLength = TimerBook.MaxName, ToolTip = "What the timer is for: a spell, a cooldown, a respawn" };
        var length = new TextBox { Style = (Style)Ui.Theme["Field"], MaxLength = 12, ToolTip = "How long: 90, 1:30, 5m, 1h30m" };
        var key = KeyBox(null);
        var sound = new SoundField();
        var add = new Button { Content = "Add", Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(6, 4, 6, 4), ToolTip = "Keep this timer" };

        void Add()
        {
            int seconds = TimerClock.TryParse(length.Text) ?? 0;
            if (!Book.AddNewPreset(name.Text, seconds, (string?)key.Tag, out var problem, sound.Sound))
            {
                Say(problem ?? "That timer can't be added.", error: true);
                return;
            }
            _timers.BookEdited();
            Say("Added.");
            Rebuild();
        }

        add.Click += (_, _) => Add();
        name.KeyDown += (_, e) => EnterCommits(e, Add);
        length.KeyDown += (_, e) => EnterCommits(e, Add);

        var g = Columns();
        Put(g, name, 0);
        Put(g, length, 1);
        Put(g, key, 2);
        Put(g, sound, 3);
        Put(g, add, 4, span: 2);
        return g;
    }

    private static void EnterCommits(KeyEventArgs e, Action act)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        act();
    }

    // ------------------------------------------------------------------ the sound box

    /// <summary>
    /// A button that shows a timer's sound and, when pressed, a menu of the sounds there are: none, the ones Windows plays for its own events, the ones in Windows'
    /// Media folder (alarms, rings and so on) and any .wav file the player has. A sound is played when it is picked, so it can be heard before it is kept.
    /// <see cref="Picked"/> is raised only when the player picks one, not when <see cref="Set"/> shows one.
    /// </summary>
    private sealed class SoundField : Button
    {
        public string? Sound { get; private set; }
        public event Action? Picked;

        public SoundField()
        {
            Style = (Style)Ui.Theme["TextButton"];
            Padding = new Thickness(8, 4, 8, 4);
            HorizontalContentAlignment = HorizontalAlignment.Left;
            ToolTip = "The sound this plays when it ends. Pick one to hear it.";
            Set(null);
            Click += (_, _) => OpenMenu();
        }

        public void Set(string? sound)
        {
            Sound = TimerSounds.Normalize(sound);
            Content = new TextBlock { Text = TimerSounds.Describe(Sound) + "  ▾", TextTrimming = TextTrimming.CharacterEllipsis };
        }

        private void Pick(string? sound)
        {
            Set(sound);
            TimerSoundPlayer.Play(Sound);
            Picked?.Invoke();
        }

        private MenuItem Item(string header, string? sound)
        {
            var item = new MenuItem { Header = header, IsCheckable = false, IsChecked = TimerSounds.Normalize(sound) == Sound };
            item.Click += (_, _) => Pick(sound);
            return item;
        }

        private void OpenMenu()
        {
            var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Bottom };
            menu.Items.Add(Item("No sound", null));
            menu.Items.Add(new Separator());
            foreach (var name in TimerSounds.SystemNames) menu.Items.Add(Item(name, TimerSounds.SystemPrefix + name));
            var windows = WindowsSounds.Value;
            if (windows.Count > 0)
            {
                menu.Items.Add(new Separator());
                foreach (var group in windows.GroupBy(w => w.Group))
                {
                    var sub = new MenuItem { Header = group.Key };
                    foreach (var w in group) sub.Items.Add(Item(w.Name, w.Value));
                    menu.Items.Add(sub);
                }
            }
            menu.Items.Add(new Separator());
            if (TimerSounds.FilePath(Sound) is { } own && !windows.Any(w => w.Value == Sound)) menu.Items.Add(Item(System.IO.Path.GetFileNameWithoutExtension(own) + "  (your file)", Sound));
            var choose = new MenuItem { Header = "Choose a file…" };
            choose.Click += (_, _) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a sound", Filter = "Sound files (*.wav)|*.wav", CheckFileExists = true };
                if (dialog.ShowDialog() == true && TimerSounds.ForFile(dialog.FileName) is { } picked) Pick(picked);
            };
            menu.Items.Add(choose);
            menu.IsOpen = true;
        }
    }

    // ------------------------------------------------------------------ the key box

    /// <summary>A box that records the keys pressed in it. <see cref="Control.Tag"/> holds the key as text (null for none).</summary>
    private sealed class KeyField : TextBox
    {
        public event Action? KeyChosen;
        public void Choose(string? key)
        {
            Tag = key;
            Text = key ?? "";
            KeyChosen?.Invoke();
        }
    }

    private KeyField KeyBox(string? key)
    {
        var box = new KeyField
        {
            Style = (Style)Ui.Theme["Field"], IsReadOnly = true, IsReadOnlyCaretVisible = false, Cursor = Cursors.Hand, Tag = key, Text = key ?? "",
            ToolTip = "Click, then press the keys that should start this timer, such as Ctrl+Alt+1. Escape or Delete empties it.",
        };
        box.GotKeyboardFocus += (_, _) => { if (!_loading) box.Text = "Press the keys…"; };
        box.LostKeyboardFocus += (_, _) => box.Text = (string?)box.Tag ?? "";
        box.PreviewKeyDown += (_, e) =>
        {
            var pressed = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, Key.DeadCharProcessed => e.DeadCharProcessedKey, _ => e.Key };
            if (pressed == Key.Tab) return;   // moving on
            e.Handled = true;
            if (pressed is Key.Escape or Key.Back or Key.Delete)
            {
                box.Choose(null);
                return;
            }
            if (pressed is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;   // waiting for the key itself
            if (KeyName(pressed) is not { } name)
            {
                Say("That key can't be used for a timer. Use a letter, a digit, F1 to F24 or a keypad key.", error: true);
                return;
            }
            var chosen = new Hotkey((HotkeyModifiers)(int)Keyboard.Modifiers, name);
            if (Hotkey.Problem(chosen) is { } problem)
            {
                Say(problem, error: true);
                return;
            }
            Say("");
            box.Choose(chosen.ToString());
        };
        return box;
    }

    private static void SetKey(TextBox box, string? key)
    {
        box.Tag = key;
        box.Text = key ?? "";
    }

    /// <summary>The name <see cref="Hotkey"/> gives a key; null for a key that can't be used.</summary>
    internal static string? KeyName(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return ((char)('A' + (key - Key.A))).ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key is >= Key.F1 and <= Key.F24) return "F" + (key - Key.F1 + 1);
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return "Num" + (key - Key.NumPad0);
        return key switch
        {
            Key.Multiply => "NumMultiply", Key.Add => "NumAdd", Key.Subtract => "NumSubtract", Key.Decimal => "NumDecimal", Key.Divide => "NumDivide", _ => null,
        };
    }

    // ------------------------------------------------------------------ corpses

    private FrameworkElement BuildCorpseSection()
    {
        void Save()
        {
            if (_loading) return;
            var setting = Book.Corpse;
            int? seconds = TimerClock.TryParse(_corpseLength.Text);
            if (seconds is null)
            {
                Say("Type a length such as 90, 1:30, 5m or 1h30m (up to 24h).", error: true);
                _corpseLength.Text = TimerClock.Spell(setting.Seconds);
                return;
            }
            var next = new CorpseTimerSetting
            {
                On = _corpseOn.IsChecked == true, Name = _corpseName.Text, Seconds = seconds.Value, Sound = _corpseSound.Sound,
            }.Cleaned();
            if (next.On == setting.On && next.Name == setting.Name && next.Seconds == setting.Seconds && next.Sound == setting.Sound) return;
            Book.Corpse = next;
            _timers.BookEdited();
            Say("");
            _loading = true;
            _corpseName.Text = next.Name;
            _corpseLength.Text = TimerClock.Spell(next.Seconds);
            _loading = false;
        }

        _corpseOn.Click += (_, _) => Save();
        _corpseName.LostFocus += (_, _) => Save();
        _corpseLength.LostFocus += (_, _) => Save();
        _corpseName.KeyDown += (_, e) => EnterCommits(e, Save);
        _corpseLength.KeyDown += (_, e) => EnterCommits(e, Save);
        _corpseSound.Picked += Save;

        var fields = Columns();
        Put(fields, _corpseName, 0);
        Put(fields, _corpseLength, 1);
        Put(fields, _corpseSound, 3);
        fields.Margin = new Thickness(0, 10, 0, 0);

        var labels = Columns();
        labels.Margin = new Thickness(0, 10, 0, 4);
        Put(labels, Label("Name"), 0);
        Put(labels, Label("Length"), 1);
        Put(labels, Label("Sound at the end"), 3, span: 3);

        var stack = new StackPanel();
        stack.Children.Add(new Border { Margin = new Thickness(0, 8, 0, 0), Child = _corpseOn });
        stack.Children.Add(labels);
        stack.Children.Add(fields);
        return stack;
    }

    // ------------------------------------------------------------------ small things

    private UIElement KeysNote()
    {
        var text = _timers.KeysUnavailable
            ?? "While a key is set here, Windows gives it to Companion, so the game does not receive it: pick combinations the game doesn't use, such as Ctrl+Alt+1. " +
               "Keys are off while this window is open. Companion listens for exactly the keys you set here and sees nothing else you type.";
        return Note(text, top: 8);
    }

    private void Say(string text, bool error = false)
    {
        _status.Text = text;
        _status.Foreground = error ? Palette.Taken : Palette.Muted;
    }

    private static TextBlock Heading(string text, double top = 0) =>
        new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, Margin = new Thickness(0, top, 0, 4) };

    private static TextBlock Label(string text) => new() { Text = text, Style = (Style)Ui.Theme["Label"] };

    private static TextBlock Note(string text, double top = 0) =>
        new() { Text = text, FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
}
