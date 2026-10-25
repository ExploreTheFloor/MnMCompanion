using MnMCompanion.Data;
using MnMCompanion.History;

namespace MnMCompanion;

/// <summary>
/// Keeps the app's saved database current and hands out what the History pages read from it. A pass copies any new ledger rows
/// on this PC into the database and works the new ones into the typed event tables; one runs at start, then every couple of
/// minutes, and the History window asks for another every few seconds while it is open (a pass over unchanged files is a
/// directory listing). Passes run off the UI thread and never overlap. If the database can't be opened, <see cref="Store"/> is
/// null and <see cref="Status"/> says why; the live panel doesn't need it.
/// </summary>
internal sealed class HistoryData : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(2);

    private readonly Func<string> _root;
    private readonly LedgerDb? _db;
    private readonly object _pass = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task? _loop;
    private long _version;

    /// <param name="ledgerRoot">The game's data folder (it can change while the app runs).</param>
    /// <param name="databasePath">Another file than the usual one (a picture-taking run uses a scratch one).</param>
    /// <param name="automatic">False for a picture-taking or headless run: no background passes, only <see cref="RefreshNow"/>.</param>
    public HistoryData(Func<string> ledgerRoot, string? databasePath = null, bool automatic = true)
    {
        _root = ledgerRoot;
        try
        {
            _db = new LedgerDb(databasePath);
            Store = new HistoryStore(_db);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Status = "the saved copy couldn't be opened: " + ex.Message;
        }
        if (automatic && _db is not null) _loop = Task.Run(Loop);
    }

    public HistoryStore? Store { get; }

    /// <summary>The last pass's outcome, or why there isn't a saved copy.</summary>
    public string Status { get; private set; } = "not run yet";

    /// <summary>Goes up whenever a pass brought something in, so a page can tell it is out of date.</summary>
    public long Version => Interlocked.Read(ref _version);

    private async Task Loop()
    {
        try
        {
            using var timer = new PeriodicTimer(Every);
            do await RefreshAsync(); while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>One pass off the UI thread; false when nothing was new (or a pass was already running).</summary>
    public Task<bool> RefreshAsync() => _db is null ? Task.FromResult(false) : Task.Run(() =>
    {
        if (!Monitor.TryEnter(_pass)) return false;   // a pass is already running
        try { return Pass(); }
        finally { Monitor.Exit(_pass); }
    });

    /// <summary>
    /// The file this player can share with the community, from the saved history brought up to date first. Null when there is no saved
    /// history. Runs off the UI thread.
    /// </summary>
    public Task<Community.Contribution?> BuildContributionAsync(string app) => Task.Run(() =>
    {
        if (_db is null) return null;
        lock (_pass)
        {
            Pass();
            return (Community.Contribution?)Data.CommunityExport.Build(_db, app);
        }
    });

    /// <summary>One pass on this thread (for a run with no window to keep responsive).</summary>
    public bool RefreshNow()
    {
        if (_db is null) return false;
        lock (_pass) return Pass();
    }

    private bool Pass()
    {
        try
        {
            var archived = LedgerArchiver.Run(_db!, _root());
            var built = EventBuilder.Update(_db!);
            Status = archived.ToString();
            bool changed = archived.RowsAdded > 0 || built.Added > 0 || built.Rebuilt;
            if (changed) Interlocked.Increment(ref _version);
            return changed;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Status = "failed: " + ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
        _db?.Dispose();
        _stop.Dispose();
    }
}
