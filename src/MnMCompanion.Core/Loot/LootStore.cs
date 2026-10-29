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
    private List<LedgerFolder> _folders = [];
    private List<LedgerFolder> _prices = [];
    private DateTime _pricesChecked;
    private bool _pricesChanged = true;
    private PublicPrices _public = PublicPrices.Empty;
    private bool _dirty;
    private long _version;
    private volatile Snapshot _snap = Nothing;
    private LootSessions _sessions;
    private ActiveCharacterWatch _watch = new();
    private DateTime? _joinsSeen;   // party joins up to this time were already looked at (null: none looked at yet)

    /// <summary>A join row that turns up in the ledger later than this after it happened is old news: the ledger is written in bursts, but not that late.</summary>
    public static readonly TimeSpan JoinFreshness = TimeSpan.FromMinutes(30);

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

    /// <summary>The character being read when it is exactly one; null when none is selected or several are.</summary>
    public LedgerSource? Selected { get; private set; }

    /// <summary>Every character being read: one, or several when a History view covers more than one.</summary>
    public IReadOnlyList<LedgerSource> Selection { get; private set; } = [];

    /// <summary>Changes when the set of characters being read does.</summary>
    public string SelectionKey => string.Join("|", Selection.Select(s => s.Directory));

    /// <summary>Where this app keeps its own files (the folder the per-character session folders are in).</summary>
    public string StateRoot => _stateRoot;

    public LootSessions Sessions => _sessions;

    /// <summary>The selected character's ledger folder (what the folder button opens).</summary>
    public string? LedgerPath => Selected?.LedgerDirectory ?? (Selection.Count > 1 ? _ledgerRoot : null);

    /// <summary>The folder this app keeps the selected character's sessions in.</summary>
    public string? StateFolder => Selected is null ? null : Path.Combine(_stateRoot, Safe($"{Selected.Server}-{Selected.Character}"));

    /// <summary>
    /// The zone the selected character is in, as far as the ledger says: the zone on its newest row (its own rows when it has any, the
    /// party's otherwise) and when that row was written. Null before anything was read. The ledger has no row for zoning itself, so
    /// this moves on the first thing written in the new zone.
    /// </summary>
    public ZoneSighting? CurrentZone { get; private set; }

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

    /// <summary>
    /// Looks at the game's folders again and says which character to switch to: the one the game has written a ledger for since the last call,
    /// unless the one being shown (<paramref name="showing"/>, its folder) was written too. Null most of the time. Call it on every tick, even
    /// when not following, so that switching it on later only reacts to something new.
    /// </summary>
    public LedgerSource? DetectPlayed(string? showing)
    {
        lock (_gate)
        {
            Sources = LedgerLocator.Find(_ledgerRoot);
            return _watch.Update(Sources, showing);
        }
    }

    /// <summary>Reads the game's data from another folder; nothing is selected until <see cref="Select"/>.</summary>
    public void ChangeRoot(string root)
    {
        lock (_gate)
        {
            _ledgerRoot = root;
            _folders = [];
            _prices = [];
            Selected = null;
            Selection = [];
            _pricesChanged = true;
            _snap = new Snapshot(++_version, [], Nothing.ByDay, ItemValues.Empty);
            Error = null;
            Sources = LedgerLocator.Find(root);
            _watch = new ActiveCharacterWatch();
        }
    }

    /// <summary>Starts reading <paramref name="source"/>'s ledger. Call <see cref="Refresh"/> after.</summary>
    public void Select(LedgerSource source) => Select([source]);

    /// <summary>
    /// Starts reading these characters' ledgers together: their loot merged, an event more than one of them logged counting once
    /// (<see cref="LootMerge"/>), on one server only. Sessions are the one character's own; with several there are none. Call <see cref="Refresh"/> after.
    /// </summary>
    public void Select(IReadOnlyList<LedgerSource> sources)
    {
        lock (_gate)
        {
            Selection = sources.ToList();
            Selected = Selection.Count == 1 ? Selection[0] : null;
            _folders = Selection.Select(s => new LedgerFolder(s.Directory)).ToList();
            _sessions = new LootSessions(StateFolder ?? Path.Combine(_stateRoot, "everyone"));
            _joinsSeen = null;
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
            if (_folders.Count == 0) return false;
            bool changed = _dirty;
            foreach (var f in _folders) changed |= f.Refresh();
            changed |= _sessions.Reload();
            if (_prices.Count == 0 || DateTime.UtcNow - _pricesChecked > TimeSpan.FromSeconds(30))
            {
                _pricesChecked = DateTime.UtcNow;
                if (_prices.Count == 0) _prices = Sources.Select(s => new LedgerFolder(s.Directory, characterFilesOnly: true)).ToList();
                foreach (var p in _prices) _pricesChanged |= p.Refresh();
            }
            CurrentZone = ZoneOf(_folders.Count == 1 ? _folders[0].Rows : []);
            Error = _folders.Select(f => f.Error).FirstOrDefault(e => e is not null);
            FileCount = _folders.Sum(f => f.FileCount);
            if (!changed && !_pricesChanged) return false;

            // New values only when a sale changed, so anything caching on the instance can tell
            var values = _pricesChanged ? ItemValues.FromLedger(_prices.SelectMany(p => p.Rows), _public) : _snap.Values;
            _pricesChanged = false;
            _dirty = false;
            // Characters on one server share a world, so what two of them logged is merged; two servers are two worlds, so nothing is
            var perCharacter = _folders.Select((f, i) => (Server: Selection[i].Server.ToLowerInvariant(), Events: (IReadOnlyList<LootEvent>)LootExtractor.Extract(f.Rows))).ToList();
            var events = perCharacter.Count == 1 ? perCharacter[0].Events
                : perCharacter.GroupBy(c => c.Server).SelectMany(g => LootMerge.Combine(g.Select(c => c.Events).ToList())).OrderBy(e => e.Time).ToList();
            var byDay = events.GroupBy(e => e.Day)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<LootEvent>)g.ToList());
            _snap = new Snapshot(++_version, byDay.Keys.OrderByDescending(d => d).ToList(), byDay, values);
            return true;
        }
    }

    private static ZoneSighting? ZoneOf(IReadOnlyList<LedgerRow> rows)
    {
        // Rows are oldest first. Own rows (the Character files) say where this character is; fall back to anything with a zone
        for (int i = rows.Count - 1; i >= 0; i--)
            if (rows[i].Zone.Length > 0 && rows[i].File == LedgerFileKind.Character) return new ZoneSighting(rows[i].Zone, rows[i].Time);
        for (int i = rows.Count - 1; i >= 0; i--)
            if (rows[i].Zone.Length > 0) return new ZoneSighting(rows[i].Zone, rows[i].Time);
        return null;
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
    public LootSession? NewSession(string reason = "manual") => NewSession(reason, DateTime.Now);

    /// <summary>
    /// <see cref="NewSession(string)"/> as of <paramref name="at"/> (a player joined then, though the ledger only said so a little later): the loot before that moment is
    /// archived and the new session starts there. Null when the session held no loot before it, or a marker is already that close.
    /// </summary>
    public LootSession? NewSession(string reason, DateTime at)
    {
        var sessions = _sessions;
        var today = DateOnly.FromDateTime(at);
        var events = Events(today, sessions.Current).Where(e => e.Time < at).ToList();
        if (events.Count == 0) return null;
        var closed = sessions.Start(at, reason);
        if (closed is null) return null;
        try
        {
            var folder = Path.Combine(StateFolder!, "sessions");
            Directory.CreateDirectory(folder);
            var name = $"{events.Min(e => e.Time):yyyy-MM-dd HHmm}-{at:HHmm}.txt";
            File.WriteAllText(Path.Combine(folder, name), LootSummary.Breakdown(today, events, values: Values));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return closed;
    }

    /// <summary>
    /// The players who joined the selected character's group (or the character, a group) since the last call, oldest first. The first call after a character is
    /// selected only notes where the ledger stands, so old joins never count. Joins older than <see cref="JoinFreshness"/> when they show up are left out.
    /// Empty unless exactly one character is selected. Call it after <see cref="Refresh"/>.
    /// </summary>
    public IReadOnlyList<PartyJoin> NewJoins(DateTime? now = null)
    {
        lock (_gate)
        {
            if (_folders.Count != 1 || Selected is null) return [];
            var owner = Selected.Character;
            var joins = _folders[0].Rows.Where(r => r.Act == "act_32" && r.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase))
                .Select(r => new PartyJoin(r.Time, r.Actor, r.ByOwner)).OrderBy(j => j.Time).ToList();
            if (_joinsSeen is null)
            {
                _joinsSeen = joins.Count > 0 ? joins[^1].Time : DateTime.MinValue;
                return [];
            }
            var seen = _joinsSeen.Value;
            var fresh = joins.Where(j => j.Time > seen).ToList();
            if (fresh.Count > 0) _joinsSeen = fresh[^1].Time;
            var recent = (now ?? DateTime.Now) - JoinFreshness;
            return fresh.Where(j => j.Time >= recent).ToList();
        }
    }

    private static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
