using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LootWindow.Ledger;
using LootWindow.Loot;

namespace LootWindow;

/// <summary>
/// The loot panel's window: borderless, dragged by its title bar, resized from any edge or the grip, on top of the game unless
/// unpinned. The title bar carries the character (a menu of every character with a ledger on this PC), a pin and a close
/// button. It looks at the ledger every couple of seconds and redraws the panel when something changed.
/// </summary>
internal sealed class PanelWindow : Window
{
    private readonly LootStore _store;
    private readonly Settings _settings;
    private readonly LootPanel _panel;
    private readonly Button _character;
    private readonly Button _pin;
    private readonly Button _about;
    private readonly Border _banner = new() { Visibility = Visibility.Collapsed };
    private readonly UpdateController? _updates;
    private readonly DispatcherTimer _timer;
    private bool _busy;

    public event Action<DateOnly, string?, LootSession?>? BreakdownRequested;

    /// <summary>The player chose "Share data with the community" in the ⓘ menu.</summary>
    public event Action? ShareRequested;

    /// <param name="updates">The update checker; null (a picture-taking run) means no update button or banner.</param>
    public PanelWindow(LootStore store, Settings settings, UpdateController? updates = null)
    {
        _store = store;
        _settings = settings;
        _updates = updates;
        Title = "Loot Goblin";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1C));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x90, 0xA0, 0xB0));
        BorderThickness = new Thickness(1);
        Topmost = settings.OnTop;
        MinWidth = 300;
        MinHeight = 240;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var b = settings.Panel ?? new Bounds(double.NaN, double.NaN, 480, 600);
        Width = b.Width;
        Height = b.Height;
        var work = SystemParameters.WorkArea;
        Left = double.IsNaN(b.Left) ? work.Right - Width - 24 : b.Left;
        Top = double.IsNaN(b.Top) ? work.Top + 80 : b.Top;
        KeepOnScreen();

        var caption = new TextBlock { Text = "Loot Goblin", Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        _character = new Button
        {
            Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(0, 2, 0, 2), FontSize = 11,
            ToolTip = "Which character's ledger to read", VerticalAlignment = VerticalAlignment.Stretch,
        };
        _character.Click += (_, _) => OpenCharacterMenu();
        _pin = TitleButton("", "Keep on top of other windows", () => SetOnTop(!Topmost));
        var close = TitleButton("", "Close", Close);
        _about = TitleButton("", "About Loot Goblin, and updates", OpenAboutMenu);
        var title = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)), Height = 24, Cursor = Cursors.SizeAll, LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_pin, Dock.Right);
        DockPanel.SetDock(_about, Dock.Right);
        DockPanel.SetDock(caption, Dock.Left);
        DockPanel.SetDock(_character, Dock.Left);
        title.Children.Add(close);
        title.Children.Add(_pin);
        if (updates is not null) title.Children.Add(_about);
        title.Children.Add(caption);
        title.Children.Add(_character);
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); };

        _panel = new LootPanel(store, settings.View, view => { settings.View = view; settings.Save(); },
            (day, member, session) => BreakdownRequested?.Invoke(day, member, session));
        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(_banner, Dock.Top);
        root.Children.Add(title);
        root.Children.Add(_banner);
        root.Children.Add(_panel);
        Content = root;
        ShowPin();
        ShowCharacter();
        if (updates is not null)
        {
            updates.Changed += ShowBanner;
            ShowBanner();
        }

        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
        Closed += (_, _) =>
        {
            _timer?.Stop();
            if (_updates is not null) _updates.Changed -= ShowBanner;
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            _panel.Refresh();
            await RefreshAsync(force: true);
            _timer.Start();
        };
    }

    public LootPanel Panel => _panel;

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

    private void SetOnTop(bool on)
    {
        Topmost = on;
        _settings.OnTop = on;
        _settings.Save();
        ShowPin();
    }

    private void ShowPin() => _pin.Foreground = Topmost ? new SolidColorBrush(Palette.AccentColor) : new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x88));

    private void ShowCharacter() => _character.Content = (_store.Selected is { } s ? s.Character : "No ledger") + "  ▾";

    /// <summary>Reads the ledger on a worker thread and redraws if it changed (or <paramref name="force"/>).</summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            bool changed = await Task.Run(_store.Refresh);
            if (changed || force) _panel.Refresh();
        }
        catch (Exception ex)
        {
            _panel.Say("Can't read the ledger: " + ex.Message);
        }
        finally { _busy = false; }
    }

    private void OpenCharacterMenu()
    {
        _store.RescanSources();
        var menu = new ContextMenu { PlacementTarget = _character, Placement = PlacementMode.Bottom };
        foreach (var source in _store.Sources)
        {
            var s = source;
            var mi = new MenuItem { Header = s.Label, IsChecked = _store.Selected?.Directory == s.Directory };
            mi.Click += async (_, _) => await Choose(s);
            menu.Items.Add(mi);
        }
        if (_store.Sources.Count == 0) menu.Items.Add(new MenuItem { Header = "No ledgers found", IsEnabled = false });
        menu.Items.Add(new Separator());
        var folder = new MenuItem { Header = "Choose the game's data folder…", ToolTip = $"Now: {_store.LedgerRoot}" };
        folder.Click += async (_, _) => await ChooseRoot();
        menu.Items.Add(folder);
        menu.IsOpen = true;
    }

    private void OpenAboutMenu()
    {
        if (_updates is null) return;
        var menu = new ContextMenu { PlacementTarget = _about, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = $"Loot Goblin {Update.AppVersion.Show(_updates.Current)}", IsEnabled = false });
        menu.Items.Add(new Separator());
        var check = new MenuItem { Header = "Check for updates now", ToolTip = "Asks GitHub whether a newer version has been released. Nothing is installed unless you say so." };
        check.Click += async (_, _) => await _updates.CheckAsync(manual: true);
        menu.Items.Add(check);
        var auto = new MenuItem { Header = "Check for updates automatically", IsCheckable = true, IsChecked = _settings.CheckForUpdates, ToolTip = "When the app starts and every few hours. It only offers; it never installs by itself." };
        auto.Click += (_, _) => { _settings.CheckForUpdates = auto.IsChecked; _settings.Save(); };
        menu.Items.Add(auto);
        menu.Items.Add(new Separator());
        var share = new MenuItem { Header = "Share data with the community…", ToolTip = "Saves your own totals as a small file you can add to the community's open data. Nothing is sent by the app." };
        share.Click += (_, _) => ShareRequested?.Invoke();
        menu.Items.Add(share);
        var page = new MenuItem { Header = "Project page" };
        page.Click += (_, _) => _updates.OpenProjectPage();
        menu.Items.Add(page);
        menu.IsOpen = true;
    }

    /// <summary>The strip under the title bar: an update is ready, being fetched, failed, or the answer to "check now".</summary>
    private void ShowBanner()
    {
        if (_updates is null) return;
        _about.Foreground = new SolidColorBrush(_updates.State == UpdateState.Available ? Palette.AccentColor : Color.FromRgb(0xB0, 0xC0, 0xD0));
        if (!_updates.Visible)
        {
            _banner.Visibility = Visibility.Collapsed;
            return;
        }
        var offer = _updates.Offer;
        var text = new TextBlock
        {
            Text = _updates.State == UpdateState.Available && offer is not null
                ? $"Version {Update.AppVersion.Show(offer.Version)} is ready (you have {Update.AppVersion.Show(_updates.Current)})."
                : _updates.Message,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEE)), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5),
        };
        if (_updates.State == UpdateState.Available && offer is { Notes.Length: > 0 })
            text.ToolTip = new ToolTip { Style = (Style)Ui.Theme["Tip"], Content = offer.Notes.Length > 700 ? offer.Notes[..700] + "…" : offer.Notes };
        var stack = new StackPanel { Margin = new Thickness(10, 7, 10, 7) };
        stack.Children.Add(text);
        if (_updates.State == UpdateState.Downloading)
            stack.Children.Add(new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, Value = _updates.Progress, Foreground = new SolidColorBrush(Palette.AccentColor), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(0) });
        var buttons = new WrapPanel();
        switch (_updates.State)
        {
            case UpdateState.Available:
                buttons.Children.Add(BannerButton(_updates.CanInstall ? "Update now" : "Open the download page", () => _ = _updates.InstallAsync(),
                    _updates.CanInstall ? "Downloads the new version, checks it, and restarts into it." : "This copy can't replace itself; opens the page to download it."));
                buttons.Children.Add(BannerButton("Later", _updates.Dismiss, "Ask again next time."));
                buttons.Children.Add(BannerButton("Skip this version", _updates.Skip, "Don't offer this version again (a newer one still will be)."));
                break;
            case UpdateState.Failed:
                if (offer is not null) buttons.Children.Add(BannerButton("Open the download page", _updates.OpenDownloadPage, "Download it by hand."));
                buttons.Children.Add(BannerButton("Close", _updates.Dismiss, ""));
                break;
            case UpdateState.Notice:
                buttons.Children.Add(BannerButton("OK", _updates.Dismiss, ""));
                break;
        }
        if (buttons.Children.Count > 0) stack.Children.Add(buttons);
        _banner.Child = stack;
        _banner.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x23, 0x33));
        _banner.BorderBrush = new SolidColorBrush(Palette.AccentColor);
        _banner.BorderThickness = new Thickness(0, 0, 0, 1);
        _banner.Visibility = Visibility.Visible;
    }

    private static Button BannerButton(string text, Action click, string tip)
    {
        var b = new Button { Content = text, Style = (Style)Ui.Theme["TextButton"], Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), FontSize = 11 };
        if (tip.Length > 0) b.ToolTip = tip;
        b.Click += (_, _) => click();
        return b;
    }

    private async Task Choose(LedgerSource source)
    {
        _store.Select(source);
        _settings.Server = source.Server;
        _settings.Character = source.Character;
        _settings.Save();
        ShowCharacter();
        _panel.Refresh();
        await RefreshAsync(force: true);
    }

    private async Task ChooseRoot()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "The folder that holds the server folders (…\\LocalLow\\Niche Worlds Cult\\Monsters and Memories)", InitialDirectory = _store.LedgerRoot };
        if (dialog.ShowDialog(this) != true) return;
        _store.ChangeRoot(dialog.FolderName);
        _settings.LedgerRoot = dialog.FolderName;
        _settings.Save();
        if (_store.Sources.Count > 0) await Choose(_store.Sources[0]);
        else
        {
            ShowCharacter();
            _panel.Refresh();
        }
    }

    private void Remember()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.Panel = new Bounds(Left, Top, Width, Height);
        _saveSoon ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _saveSoon!.Stop(); _settings.Save(); }, Dispatcher);
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    private DispatcherTimer? _saveSoon;

    /// <summary>Saved on another monitor layout: pull it back onto the desktop.</summary>
    private void KeepOnScreen()
    {
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, l, Math.Max(l, r - 120));
        Top = Math.Clamp(Top, t, Math.Max(t, b - 60));
    }
}
