using System.Globalization;
using MnMCompanion.Ledger;

namespace MnMCompanion.Loot;

/// <summary>
/// The app's whole data model: one character's ledger (<see cref="LedgerFolder"/>) turned into loot events by day, the item
/// values worked out from every character's vendor sales on this PC, and the sessions the player marked. Everything comes
/// from the ledger files; the only thing written is this app's own sessions file and the text archives of closed sessions.
/// <see cref="Refresh"/> may run on a background thread; the readers (<see cref="Days"/>, <see cref="Events"/>,
/// <see cref="Values"/>) see the last finished snapshot and never block.
/// </summary>
public sealed class LootStore
{
    private sealed record Snapshot(long Version, IReadOnlyList<DateOnly> Days, IReadOnlyDictionary<DateOnly, IReadOnlyList<LootEvent>> ByDay, ItemValues Values);

    private static readonly Snapshot Nothing = new(0, [], new Dictionary<DateOnly, IReadOnlyList<LootEvent>>(), ItemValues.Empty);

    private readonly object _gate = new();
    private readonly string _stateRoot;
    private string _ledgerRoot;
    private LedgerFolder? _folder;
    private List<LedgerFolder> _prices = [];
    private DateTime _pricesChecked;
    private bool _pricesChanged = true;
    private PublicPrices _public = PublicPrices.Empty;
    private bool _dirty;
    private long _version;
    private volatile Snapshot _snap = Nothing;
    private LootSessions _sessions;

    /// <param name="stateRoot">Where this app keeps its own files (sessions and their archives).</param>
    /// <param name="ledgerRoot">The game's per-character folder; the default under LocalLow when null.</param>
    public LootStore(string stateRoot, string? ledgerRoot = null)
    {
        _stateRoot = stateRoot;
        _ledgerRoot = ledgerRoot ?? LedgerLocator.DefaultRoot();
        _sessions = new LootSessions(Path.Combine(stateRoot, "none"));
        Sources = LedgerLocator.Find(_ledgerRoot);
    }

    public string LedgerRoot => _ledgerRoot;

    /// <summary>The characters with a ledger on this PC, the most recently played first.</summary>
    public IReadOnlyList<LedgerSource> Sources { get; private set; }

    public LedgerSource? Selected { get; private set; }

    public LootSessions Sessions => _sessions;

    /// <summary>The selected character's ledger folder (what the folder button opens).</summary>
    public string? LedgerPath => Selected?.LedgerDirectory;

    /// <summary>The folder this app keeps the selected character's sessions in.</summary>
    public string? StateFolder => Selected is null ? null : Path.Combine(_stateRoot, Safe($"{Selected.Server}-{Selected.Character}"));

    /// <summary>What went wrong reading the ledger on the last <see cref="Refresh"/>, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>How many ledger files the selected character has (dated and archived).</summary>
    public int FileCount { get; private set; }

    /// <summary>Changes by one whenever the loot does.</summary>
    public long Version => _snap.Version;

    public ItemValues Values => _snap.Values;

    /// <summary>The public price list in use (empty until one is downloaded).</summary>
    public PublicPrices PublicPrices => _public;

    /// <summary>
    /// Takes in a downloaded public price list: items this PC has never seen sold get a value from it. Values are worked out again
    /// by the next <see cref="Refresh"/>, which then reports a change.
    /// </summary>
    public void UsePublicPrices(PublicPrices list)
    {
        lock (_gate)
        {
            _public = list;
            _pricesChanged = true;
        }
    }

    /// <summary>Looks for characters again (a new one may have logged in since).</summary>
    public void RescanSources() => Sources = LedgerLocator.Find(_ledgerRoot);

    /// <summary>Reads the game's data from another folder; nothing is selected until <see cref="Select"/>.</summary>
    public void ChangeRoot(string root)
    {
        lock (_gate)
        {
            _ledgerRoot = root;
            _folder = null;
            _prices = [];
            Selected = null;
            _pricesChanged = true;
            _snap = new Snapshot(++_version, [], Nothing.ByDay, ItemValues.Empty);
            Error = null;
            Sources = LedgerLocator.Find(root);
        }
    }

    /// <summary>Starts reading <paramref name="source"/>'s ledger. Call <see cref="Refresh"/> after.</summary>
    public void Select(LedgerSource source)
    {
        lock (_gate)
        {
            Selected = source;
            _folder = new LedgerFolder(source.Directory);
            _sessions = new LootSessions(StateFolder!);
            _snap = new Snapshot(++_version, [], Nothing.ByDay, _snap.Values);
            _dirty = true;
            Error = null;
        }
    }

    /// <summary>
    /// Reads whatever changed in the ledger since the last call. Returns true when the loot or the prices changed. Cheap when
    /// nothing did: it looks at the files' sizes and write times. Prices (every character's sales) are checked every 30 seconds.
    /// </summary>
    public bool Refresh()
    {
        lock (_gate)
        {
            if (_folder is null) return false;
            bool changed = _folder.Refresh() | _dirty;
            if (_prices.Count == 0 || DateTime.UtcNow - _pricesChecked > TimeSpan.FromSeconds(30))
            {
                _pricesChecked = DateTime.UtcNow;
                if (_prices.Count == 0) _prices = Sources.Select(s => new LedgerFolder(s.Directory, characterFilesOnly: true)).ToList();
                foreach (var p in _prices) _pricesChanged |= p.Refresh();
            }
            Error = _folder.Error;
            FileCount = _folder.FileCount;
            if (!changed && !_pricesChanged) return false;

            // New values only when a sale changed, so anything caching on the instance can tell
            var values = _pricesChanged ? ItemValues.FromLedger(_prices.SelectMany(p => p.Rows), _public) : _snap.Values;
            _pricesChanged = false;
            _dirty = false;
            var byDay = LootExtractor.Extract(_folder.Rows).GroupBy(e => e.Day)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<LootEvent>)g.ToList());
            _snap = new Snapshot(++_version, byDay.Keys.OrderByDescending(d => d).ToList(), byDay, values);
            return true;
        }
    }

    /// <summary>The days with loot, newest first.</summary>
    public IReadOnlyList<DateOnly> Days() => _snap.Days;

    /// <summary>A day's loot, oldest first; only what happened inside <paramref name="session"/> when one is given.</summary>
    public IReadOnlyList<LootEvent> Events(DateOnly day, LootSession? session = null)
    {
        if (!_snap.ByDay.TryGetValue(day, out var all)) return [];
        return session is null ? all : all.Where(e => session.Contains(e.Time)).ToList();
    }

    /// <summary>
    /// Ends the session in progress and starts a new one. What was looted today in the closed one is saved as a text file
    /// beside the sessions file (nothing is deleted: the ledger keeps everything). Returns the closed session, or null when
    /// it held no loot today or the last marker is too recent.
    /// </summary>
    public LootSession? NewSession(string reason = "manual")
    {
        var sessions = _sessions;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var events = Events(today, sessions.Current);
        if (events.Count == 0) return null;
        var closed = sessions.Start(DateTime.Now, reason);
        if (closed is null) return null;
        try
        {
            var folder = Path.Combine(StateFolder!, "sessions");
            Directory.CreateDirectory(folder);
            var name = $"{events.Min(e => e.Time):yyyy-MM-dd HHmm}-{DateTime.Now:HHmm}.txt";
            File.WriteAllText(Path.Combine(folder, name), LootSummary.Breakdown(today, events, values: Values));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return closed;
    }

    private static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
