using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MnMCompanion.Community;
using MnMCompanion.Data;
using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using MnMCompanion.Update;

namespace MnMCompanion;

public partial class App : Application
{
    private LootStore _store = null!;
    private Settings _settings = null!;
    private PanelWindow? _panel;
    private HistoryWindow? _breakdown;
    private HistoryData? _history;
    private UpdateController? _updates;
    private CommunityClient? _community;
    private SharingController? _sharing;
    private TimersWindow? _timers;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        Resources.MergedDictionaries.Add(Ui.Theme);
        Resources.MergedDictionaries.Add(Ui.Menus);
        var args = Args.Parse(e.Args);

        _settings = Settings.Load();
        // A run pointed at another game folder (a test, a picture, a borrowed copy) keeps its own saved copy and sessions and saves no settings,
        // so what it reads can never end up in the player's own database
        var other = args.Ledger is null ? null : LedgerDb.OtherFolder(args.Ledger);
        if (other is not null) _settings.ReadOnly = true;
        _store = new LootStore(other ?? Settings.Folder, args.Ledger ?? FindUnderWine(_settings) ?? _settings.LedgerRoot);
        if (args.About)
        {
            About(args);
            Shutdown();
            return;
        }
        if (args.Diagnose)
        {
            Diagnose(args);
            Shutdown();
            return;
        }
        if (args.Contribute is not null)
        {
            Contribute(args);
            Shutdown();
            return;
        }
        if (args.Archive)
        {
            Archive(args);
            Shutdown();
            return;
        }
        var saved = _store.Sources.FirstOrDefault(s => args.Character is { } c
                ? s.Character.Equals(c, StringComparison.OrdinalIgnoreCase)
                : s.Character.Equals(_settings.Character, StringComparison.OrdinalIgnoreCase) && s.Server.Equals(_settings.Server, StringComparison.OrdinalIgnoreCase));
        // The small window follows the character being played: it starts on whichever one the game wrote a ledger for last, unless one was named on the command line
        var pick = args.Character is null ? _store.Sources.FirstOrDefault(s => s.HasLedger) ?? saved : saved;
        pick ??= _store.Sources.FirstOrDefault();
        if (pick is not null) _store.Select(pick);

        if (args.Snapshot is { } png)
        {
            Snapshot(png, args);
            Shutdown();
            return;
        }

        _updates = new UpdateController(_settings);
        _history = new HistoryData(() => _store.LedgerRoot, OtherDb(args));
        _community = new CommunityClient(AppVersion.Current());
        _sharing = new SharingController(_settings, _history, _community);
        CommunityAccess.Reader = new CommunityReader(_settings, _history, _community);
        _panel = new PanelWindow(_store, _settings, _updates, _history);
        MainWindow = _panel;
        _panel.Closed += (_, _) => Shutdown();
        _panel.BreakdownRequested += OpenBreakdown;
        _panel.ShareRequested += OpenShare;
        _panel.TimersRequested += () => Timers().Toggle();
        _panel.HistoryPageRequested += OpenHistoryPage;
        _panel.Show();
        if (!_settings.ReadOnly)
        {
            // Made at once, shown only if it was open: the keys that start timers work while it is hidden
            var timers = Timers();
            if (_settings.TimersOpen) timers.Toggle();
        }
        if (args.History) OpenBreakdown(DateOnly.FromDateTime(DateTime.Now), null, null);
        if (args.Share) OpenShare();
        // Asked every time the app starts, and only while the answer is No (not when told not to; a run with no window never gets here)
        if (CommunityPolicy.ShouldAsk(_settings.Sharing, !args.NoSharePrompt)) AskToShare();
        _updates.Start();
        _sharing.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CommunityAccess.Reader = null;
        _sharing?.Dispose();
        _community?.Dispose();
        _history?.Dispose();
        _updates?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// A Linux player (Wine or Proton) has the game in another prefix, so the usual place is empty. When nothing is there and no working folder was chosen,
    /// look through the other prefixes through Wine's Z: drive and remember what is found (a folder the player chose that still works is left alone).
    /// </summary>
    private static string? FindUnderWine(Settings settings)
    {
        var found = GameFolderFinder.Choose(settings.LedgerRoot, root => LedgerLocator.Find(root).Count, LedgerLocator.Find().Count, GameFolderFinder.RunningUnderWine(),
            () => GameFolderFinder.LinuxRoot() is { } linux ? GameFolderFinder.Best(linux)?.Root : null);
        if (found is not null && !settings.ReadOnly)
        {
            settings.LedgerRoot = found;
            settings.Save();
        }
        return found;
    }

    /// <summary>
    /// <c>--diagnose</c>: writes what this program sees of where it is running (is it under Wine or Proton, where it looked for the game's data and what it found,
    /// whether a window can be shown and drawn, whether the map's web view exists) to a report a tester can post, and quits. Counts only: no names, and the
    /// user name in a path is hidden.
    /// </summary>
    private void Diagnose(Args args)
    {
        var report = args.Report ?? Path.Combine(Settings.Folder, "diagnose-report.txt");
        string text;
        try
        {
            var names = new[] { "WINEPREFIX", "STEAM_COMPAT_DATA_PATH", "PROTON_VERSION", "WINELOADERNOEXEC" };
            var env = names.Select(n => (Name: n, Value: Environment.GetEnvironmentVariable(n))).Select(e => (e.Name, Value: string.IsNullOrEmpty(e.Value) ? null : e.Value)).ToList();
            var signs = env.Where(e => e.Value is not null).Select(e => e.Name + " is set").ToList();
            if (File.Exists(@"Z:\proc\version")) signs.Add(@"Z:\proc\version can be read");
            var linux = GameFolderFinder.LinuxRoot();
            var chosen = _settings.LedgerRoot;
            text = GameFolderFinder.Report(new DiagnoseFacts(
                AppVersion.Show(AppVersion.Current()), System.Runtime.InteropServices.RuntimeInformation.OSDescription + " (reports " + Environment.OSVersion.VersionString + ")",
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, GameFolderFinder.RunningUnderWine(), signs.Count == 0 ? "no signs" : string.Join(", ", signs), env, linux,
                _store.LedgerRoot, LedgerLocator.DefaultRoot(), LedgerLocator.Find().Count, chosen, chosen is null ? 0 : LedgerLocator.Find(chosen).Count,
                linux is null ? [] : GameFolderFinder.Find(linux), WindowTest(), WebViewState(), Settings.Folder));
        }
        catch (Exception ex)
        {
            text = "the diagnosis itself failed: " + ex.GetType().Name + ": " + GameFolderFinder.Mask(ex.Message);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, text);
    }

    /// <summary>Shows a small window far off screen and draws it to a picture, which is the least a Wine or Proton run has to manage.</summary>
    private string WindowTest()
    {
        var png = Path.Combine(Path.GetTempPath(), "mnmcompanion-test-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var window = new Window { Width = 220, Height = 90, Left = -20000, Top = 0, ShowInTaskbar = false, ShowActivated = false, Content = new System.Windows.Controls.TextBlock { Text = "Companion", Margin = new Thickness(12) } };
            window.Show();
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            RenderVisual(window, Math.Max(1, (int)window.ActualWidth), Math.Max(1, (int)window.ActualHeight), png);
            window.Close();
            return new FileInfo(png).Length > 0 ? "a window could be shown and drawn" : "a window was shown but the picture of it was empty";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "failed: " + ex.GetType().Name + ": " + GameFolderFinder.Mask(ex.Message);
        }
        finally { try { File.Delete(png); } catch (IOException) { } }
    }

    /// <summary>Whether Microsoft's web view (the map page's) exists here; under Wine it normally doesn't, and the map page then offers the browser instead.</summary>
    private static string WebViewState()
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrEmpty(version) ? "not installed (the map page offers to open the browser instead)" : "installed, version " + version;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "not available (" + ex.GetType().Name + "); the map page offers to open the browser instead";
        }
    }

    /// <summary><c>--about</c>: writes the version, whether this copy can update itself, and where its files are, to a report file and quits.</summary>
    private void About(Args args)
    {
        var report = args.Report ?? Path.Combine(Settings.Folder, "about.txt");
        using var updates = new UpdateController(_settings);
        var lines = new[]
        {
            $"Companion {AppVersion.Show(updates.Current)}",
            $"program: {Environment.ProcessPath}",
            $"can update itself: {(updates.CanInstall ? "yes" : "no (not a published single-file program, or its folder is read-only)")}",
            $"checks for updates: {(_settings.CheckForUpdates ? "yes" : "no")}; skipped version: {_settings.SkippedVersion ?? "none"}",
            $"settings and database: {Settings.Folder}",
            $"game data: {_store.LedgerRoot}",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllLines(report, lines);
    }

    /// <summary>
    /// <c>--archive</c>: copies every ledger row on this PC into the database once, writes what it did to a report file
    /// (<c>archive-report.txt</c> beside the settings, or <c>--report</c>) and quits without showing a window.
    /// </summary>
    private void Archive(Args args)
    {
        var report = args.Report ?? Path.Combine(Settings.Folder, "archive-report.txt");
        var lines = new List<string>();
        try
        {
            using var db = new LedgerDb(args.Db ?? OtherDb(args));
            var result = LedgerArchiver.Run(db, _store.LedgerRoot);
            var journal = JournalArchiver.Run(db, _store.LedgerRoot);
            var events = EventBuilder.Update(db);
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {result}; {events.Added:N0} history events {(events.Rebuilt ? "(rebuilt)" : "new")}");
            lines.Add($"  journal: {journal}");
            lines.Add($"{db.FilePath}: {db.RowCount():N0} rows from {db.FileCount():N0} files");
            lines.AddRange(db.RowsByAct().Select(a => $"  {a.Act}  {a.Rows,8:N0}  {a.Meaning}"));
            lines.AddRange(result.Problems.Concat(journal.Problems).Select(p => "  to retry: " + p));
        }
        catch (Exception ex)
        {
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  failed: {ex.Message}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllLines(report, lines);
    }

    /// <summary>The "share your data with the community?" question: Yes turns sharing on and it stops asking; No, or closing it, asks again next start.</summary>
    private void AskToShare()
    {
        var prompt = new ShareWindow(_history!, _settings, "MnMCompanion " + AppVersion.Show(AppVersion.Current()), ShareMode.Ask,
            yes: () => { _settings.SetSharing(true); _settings.Save(); }) { Owner = _panel };
        prompt.Show();
        prompt.Activate();
    }

    /// <summary>The timers window (made when the app starts, so its keys work while it is hidden). It follows the character the panel follows.</summary>
    private TimersWindow Timers() => _timers ??= new TimersWindow(_settings, () => _store.StateFolder, RecentCorpses);

    /// <summary>
    /// What the pictures of the timers show: timers in each stage with keys on some, and a dozen corpses looted at different times (the first few nearly out of time).
    /// The book is written to <paramref name="folder"/>; the corpses are returned.
    /// </summary>
    private static IReadOnlyList<LootedCorpse> TimersSample(string folder)
    {
        var now = DateTime.UtcNow;
        var book = new Timers.TimerBook();
        book.AddPreset("Haste", 90, "Ctrl+Alt+1", "system:Asterisk");
        book.AddPreset("Regeneration", 300, "Ctrl+Alt+2", @"file:C:\Windows\Media\Alarm02.wav");
        book.AddPreset("Bind Wound", 30, "Ctrl+Alt+3", "system:Beep");
        book.AddPreset("Shield", 600, null, "system:Exclamation");
        book.AddPreset("Food", 1800, "Ctrl+Alt+F");
        book.Corpse = new Timers.CorpseTimerSetting { On = true, Name = "Corpse", Seconds = 600, Sound = "system:Beep" };
        book.Start("Regeneration", 300, now.AddSeconds(-40));
        book.Start("Haste", 90, now.AddSeconds(-64));
        book.Start("Bind Wound", 30, now.AddSeconds(-24));
        book.Start("Shield", 600, now.AddSeconds(-614));
        book.Save(folder);

        var local = DateTime.Now;
        string[] monsters = ["a bog crawler", "a marsh wolf", "a reed stalker", "a moss beetle"];
        var corpses = new List<LootedCorpse>();
        for (int i = 0; i < 12; i++)
            corpses.Add(new LootedCorpse("Aldric", true, monsters[i % monsters.Length], true, local.AddSeconds(-(590 - i * 47)), 2, 14, 0, null, "bogmoor"));
        return corpses;
    }

    private long _corpseVersion = -1;
    private string _corpseSelection = "";
    private DateOnly _corpseDay;
    private IReadOnlyList<LootedCorpse> _corpseCache = [];

    /// <summary>
    /// The corpses looted yesterday and today by the character being played (and the group), for the corpse timers. Worked out again only when the
    /// loot, the character or the day changes, so asking for it every few moments costs nothing.
    /// </summary>
    private IReadOnlyList<LootedCorpse> RecentCorpses()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_store.Version != _corpseVersion || _store.SelectionKey != _corpseSelection || today != _corpseDay)
        {
            _corpseCache = LootSummary.Corpses(_store.Events(today.AddDays(-1)).Concat(_store.Events(today)));
            _corpseVersion = _store.Version;
            _corpseSelection = _store.SelectionKey;
            _corpseDay = today;
        }
        return _corpseCache;
    }

    /// <summary>Opens the window where the player previews and saves the file they can share with the community data.</summary>
    private void OpenShare()
    {
        if (_history is null) return;
        var window = new ShareWindow(_history, _settings, "MnMCompanion " + AppVersion.Show(AppVersion.Current()), ShareMode.Settings) { Owner = _panel };
        window.Show();
    }

    /// <summary>
    /// <c>--contribute &lt;file&gt;</c>: saves the file a player can share with the community data, without a window, and says what is
    /// in it in the report. Works from the saved history (brought up to date first).
    /// </summary>
    private void Contribute(Args args)
    {
        var report = args.Report ?? Path.Combine(Settings.Folder, "contribute-report.txt");
        var lines = new List<string>();
        try
        {
            using var db = new LedgerDb(args.Db ?? OtherDb(args));
            LedgerArchiver.Run(db, _store.LedgerRoot);
            JournalArchiver.Run(db, _store.LedgerRoot);
            EventBuilder.Update(db);
            var contribution = CommunityExport.Build(db, "MnMCompanion " + AppVersion.Show(AppVersion.Current()));
            var path = Path.GetFullPath(args.Contribute!);
            // A folder (or a name with no .json) gets the file inside it, named by the contributor id as the data repository wants
            if (Directory.Exists(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, contribution.Contributor + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Exactly what sharing sends, laid out to be read: the totals, and (when there is any) the NPC dialogue as its own second file
            File.WriteAllText(path, contribution.ToCoreJson(indented: true));
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  wrote {path} ({new FileInfo(path).Length:N0} bytes; sent without the indentation it is {contribution.ToCoreJson().Length:N0})");
            if (contribution.Npcs.Count > 0)
            {
                var partPath = Path.ChangeExtension(path, null) + ".npcs.json";
                File.WriteAllText(partPath, contribution.ToNpcPartJson(indented: true));
                lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  wrote {partPath} ({new FileInfo(partPath).Length:N0} bytes; sent compact it is {contribution.ToNpcPartJson().Length:N0})");
            }
            if ((contribution.Trades ?? []).Count > 0)
            {
                var tradesPath = Path.ChangeExtension(path, null) + ".trades.json";
                File.WriteAllText(tradesPath, contribution.ToTradesPartJson(indented: true));
                lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  wrote {tradesPath} ({new FileInfo(tradesPath).Length:N0} bytes; sent compact it is {contribution.ToTradesPartJson().Length:N0})");
            }
            lines.AddRange(Community.ContributionBuilder.Describe(contribution).Select(l => "  " + l));
        }
        catch (Exception ex)
        {
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  failed: {ex.Message}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllLines(report, lines);
    }

    /// <summary>The database file for a run pointed at another game folder with <c>--ledger</c>: its own, never the player's. Null for a normal run.</summary>
    private static string? OtherDb(Args args) => args.Ledger is null ? null : Path.Combine(LedgerDb.OtherFolder(args.Ledger), "ledger.db");

    /// <summary>Opens the History window (or brings it forward) on a day of loot, optionally one player and one session.</summary>
    private HistoryWindow EnsureBreakdown()
    {
        if (_breakdown is null)
        {
            _breakdown = new HistoryWindow(_store, _settings, _history!);
            if (_settings.Breakdown is null && _panel is not null) _breakdown.PlaceBeside(_panel);
            _breakdown.Closed += (_, _) => _breakdown = null;
            _breakdown.Show();
        }
        if (_breakdown.WindowState == WindowState.Minimized) _breakdown.WindowState = WindowState.Normal;
        return _breakdown;
    }

    private void OpenBreakdown(DateOnly day, string? member, LootSession? session)
    {
        var window = EnsureBreakdown();
        window.ShowDay(day, member, session);
        window.Activate();
    }

    /// <summary>Opens the History window on one page, for the character the small window follows.</summary>
    private void OpenHistoryPage(string key)
    {
        var window = EnsureBreakdown();
        window.ShowPageFor(key);
        window.Activate();
    }

    /// <summary>Draws the panel to a PNG and quits: for checking the layout without sitting in front of it. Writes no settings.</summary>
    private void Snapshot(string path, Args args)
    {
        _store.Refresh();
        if (args.Breakdown)
        {
            // The History window, with the saved history built into a scratch file for the picture (not the player's own)
            var scratch = Path.Combine(Path.GetTempPath(), "mnmcompanion-snapshot-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var history = new HistoryData(() => _store.LedgerRoot, scratch, automatic: false);
                history.RefreshNow();
                var page = args.Page ?? "Loot";
                var window = new HistoryWindow(_store, new Settings { ReadOnly = true, HistoryPage = page }, history) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
                window.Left = -20000;
                window.Top = 0;
                window.Width = args.Width;
                window.Height = args.Height;
                window.Show();
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (page.Equals("Loot", StringComparison.OrdinalIgnoreCase))
                {
                    window.ShowDay(args.Day ?? _store.Days().FirstOrDefault(), args.Member);
                    if (args.Tab is { } tab) window.PickTab(tab);
                    if (args.Drop is { } drop) window.PickDrop(drop);
                }
                else
                {
                    window.ShowPage(page);   // the window picks its page only once it has loaded; a picture can't wait for that
                    if (args.Period is { } period) window.SetPeriod(period);
                    window.DrawNow();
                    if (args.Tab is { } tab) window.PickTab(tab);
                }
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                RenderVisual(window, (int)window.ActualWidth, (int)window.ActualHeight, path);
                window.Close();
            }
            finally
            {
                foreach (var file in new[] { scratch, scratch + "-wal", scratch + "-shm" })
                    try { File.Delete(file); } catch (IOException) { }
            }
            return;
        }
        if (args.Share)
        {
            // The sharing window as a player who said Yes sees it, with what would be shared worked out from the ledger it was pointed at
            var scratch = Path.Combine(Path.GetTempPath(), "mnmcompanion-snapshot-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var history = new HistoryData(() => _store.LedgerRoot, scratch, automatic: false);
                history.RefreshNow();
                var settings = new Settings { ReadOnly = true, ShareWithCommunity = true, ShareConsentSchema = Community.ContributionBuilder.Schema };
                var window = new ShareWindow(history, settings, "MnMCompanion " + AppVersion.Show(AppVersion.Current()), ShareMode.Settings)
                    { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0 };
                window.Show();
                var until = DateTime.UtcNow.AddSeconds(15);
                while (!window.PreviewReady && DateTime.UtcNow < until)
                {
                    Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Thread.Sleep(30);
                }
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                RenderVisual(window, (int)window.ActualWidth, (int)window.ActualHeight, path);
                window.Close();
            }
            finally
            {
                foreach (var file in new[] { scratch, scratch + "-wal", scratch + "-shm" })
                    try { File.Delete(file); } catch (IOException) { }
            }
            return;
        }
        if (args.Timers || args.TimersSetup)
        {
            // The timers window (or its setup window) with a made-up set of timers, keys and corpses (in a scratch folder, not the player's own), shown far off screen
            var folder = Path.Combine(Path.GetTempPath(), "mnmcompanion-timers-" + Guid.NewGuid().ToString("N"));
            try
            {
                var corpses = TimersSample(folder);
                var settings = new Settings { ReadOnly = true, TimersOpacity = int.TryParse(args.Opacity, out var opacity) ? opacity : 100, TimersButtons = args.Buttons };
                var timers = new TimersWindow(settings, () => folder, () => corpses);
                Window window = args.TimersSetup
                    ? new TimerSetupWindow(timers, settings) { WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false }
                    : timers;
                window.ShowActivated = false;
                window.Width = args.Width;
                window.Height = args.Height;
                window.Left = -20000;
                window.Top = 0;
                window.Show();
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                RenderVisual(window, (int)window.ActualWidth, (int)window.ActualHeight, path);
                window.Close();
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            }
            return;
        }
        if (args.Window)
        {
            // The whole window with its title bar, shown far off screen so nothing appears on the desktop
            // The same window the player sees, update button included; nothing is checked or downloaded
            var updates = new UpdateController(new Settings { ReadOnly = true });
            if (args.PreviewUpdate)
                updates.Preview(new UpdateOffer(new Version(0, 4, 0), "v0.4.0", "A few fixes and a cleaner look.", UpdateService.ReleasesUrl, "", "", 0));
            // The saved history for the Vendor, Trades, Items and Harvest pages is built into a scratch file for the picture (not the player's own)
            var scratch = Path.Combine(Path.GetTempPath(), "mnmcompanion-snapshot-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var history = new HistoryData(() => _store.LedgerRoot, scratch, automatic: false);
                history.RefreshNow();
                var window = new PanelWindow(_store, new Settings { View = args.View ?? "Value", MiniPage = args.Mini ?? "Loot", ReadOnly = true }, updates, history)
                    { ShowActivated = false, ShowInTaskbar = false, Width = args.Width, Height = args.Height };
                window.Left = -20000;
                window.Show();
                if (args.Day is { } shown) window.Panel.ShowDay(shown);
                else window.Panel.Refresh();
                window.DrawPageNow();
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var content = (FrameworkElement)window.Content;
                File.WriteAllText(path + ".txt", $"window {window.ActualWidth}x{window.ActualHeight} content {content.ActualWidth}x{content.ActualHeight} dpi {VisualTreeHelper.GetDpi(window).PixelsPerDip}");
                RenderVisual(window, (int)window.ActualWidth, (int)window.ActualHeight, path);
                window.Close();
            }
            finally
            {
                foreach (var file in new[] { scratch, scratch + "-wal", scratch + "-shm" })
                    try { File.Delete(file); } catch (IOException) { }
            }
            return;
        }
        var panel = new LootPanel(_store, new SessionScope(_store), args.View ?? "Value", _ => { }, (_, _, _) => { });
        if (args.Day is { } day) panel.ShowDay(day);
        else panel.Refresh();
        Render(panel, args.Width, args.Height, path);
    }

    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        RenderVisual(element, width, height, path);
    }

    private static void RenderVisual(Visual visual, int width, int height, string path)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

/// <summary>Command line: <c>--no-share-prompt</c> to start without asking about sharing (for scripts and pictures); <c>--share</c> to open the "Share data with the community" window as the app starts; <c>--contribute &lt;file or folder&gt;</c> to save the file you can share with the community data and quit; <c>--history</c> to open the History window as the app starts; <c>--about [--report &lt;file&gt;]</c> to write the version and paths to a file and quit; <c>--archive [--db &lt;file&gt;] [--report &lt;file&gt;]</c> to copy every ledger row into the database and quit; <c>--ledger &lt;folder&gt;</c> <c>--character &lt;name&gt;</c> and, for a picture instead of a window, <c>--snapshot &lt;png&gt; [--view Value|Items|Mobs|Feed] [--day yyyy-MM-dd] [--size WxH] [--window [--preview-update]]</c>, or <c>--snapshot &lt;png&gt; --breakdown [--page Loot|Trades|Vendors|Changes|Harvest] [--member name] [--tab Looters|Items|Mobs|"Drop rates"|"Loot log"|Sales|Purchases|"Price book"|Log|...] [--drop "a monster"] [--period "7 days"] [--size WxH]</c> for the History window.</summary>
internal sealed record Args(string? Ledger, string? Character, string? Snapshot, string? View, DateOnly? Day, int Width, int Height, bool Window, bool Breakdown, string? Member, string? Tab,
    bool Archive = false, string? Db = null, string? Report = null, bool About = false, bool PreviewUpdate = false, string? Page = null, string? Drop = null, string? Period = null, bool History = false, string? Contribute = null, bool Share = false, bool NoSharePrompt = false, string? Mini = null, bool Diagnose = false, bool Timers = false, bool TimersSetup = false, string? Opacity = null, bool Buttons = false)
{
    public static Args Parse(string[] args)
    {
        string? Value(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        int w = 390, h = 600;
        if (Value("--size") is { } size && size.Split('x') is [var sw, var sh] && int.TryParse(sw, out var pw) && int.TryParse(sh, out var ph)) (w, h) = (pw, ph);
        DateOnly? day = DateOnly.TryParse(Value("--day"), out var d) ? d : null;
        return new Args(Value("--ledger"), Value("--character"), Value("--snapshot"), Value("--view"), day, w, h, args.Contains("--window"), args.Contains("--breakdown"), Value("--member"), Value("--tab"),
            args.Contains("--archive"), Value("--db"), Value("--report"), args.Contains("--about"), args.Contains("--preview-update"), Value("--page"), Value("--drop"), Value("--period"), args.Contains("--history"), Value("--contribute"), args.Contains("--share"), args.Contains("--no-share-prompt"), Value("--mini"), args.Contains("--diagnose"), args.Contains("--timers"), args.Contains("--timers-setup"), Value("--opacity"), args.Contains("--buttons"));
    }
}
