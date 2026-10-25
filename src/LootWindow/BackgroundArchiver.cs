using LootWindow.Data;

namespace LootWindow;

/// <summary>
/// Keeps the app's database up to date while the window is open: one pass over every ledger folder on this PC at start, then another
/// every couple of minutes (a pass over unchanged files is a directory listing). Runs off the UI thread; a failed pass is noted and tried again.
/// </summary>
internal sealed class BackgroundArchiver : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    /// <summary>The last pass's outcome, or the reason it failed.</summary>
    public string Status { get; private set; } = "not run yet";

    public BackgroundArchiver(Func<string> ledgerRoot, string? databasePath = null)
    {
        _loop = Task.Run(async () =>
        {
            LedgerDb? db = null;
            try
            {
                using var timer = new PeriodicTimer(Every);
                do
                {
                    try
                    {
                        db ??= new LedgerDb(databasePath);
                        Status = LedgerArchiver.Run(db, ledgerRoot()).ToString();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Status = "failed: " + ex.Message;
                        db?.Dispose();
                        db = null;
                    }
                }
                while (await timer.WaitForNextTickAsync(_stop.Token));
            }
            catch (OperationCanceledException) { }
            finally { db?.Dispose(); }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
        _stop.Dispose();
    }
}
