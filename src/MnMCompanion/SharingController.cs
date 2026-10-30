using MnMCompanion.Community;
using MnMCompanion.Update;

namespace MnMCompanion;

/// <summary>
/// Sends a sharing player's totals to the project's community service, and only if they answered Yes to the file as it is now (<see cref="Settings.Sharing"/>).
/// It looks every 30 seconds whether it is time (<see cref="SharePlan"/>): the first send comes shortly after start, then one every few
/// hours (the service takes one file per id every 6), a file that has not changed since the last send is not sent again, and after a
/// refusal or a failure it waits instead of trying again at once. It never throws and never blocks the window; what happened last is a
/// sentence in <see cref="Settings.ShareNote"/>, shown in the sharing window.
/// </summary>
/// <param name="onUi">Runs an action on the window's thread (and waits for it), where the settings are written.</param>
internal sealed class SharingController(Settings settings, HistoryData history, CommunityClient client, Action<Action> onUi) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly string _app = "MnMCompanion " + AppVersion.Show(AppVersion.Current());
    private DateTime? _notBefore, _partNotBefore, _tradesNotBefore;

    public void Start() => _ = Loop();

    private async Task Loop()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), _stop.Token);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do await TickAsync(DateTime.UtcNow); while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) { }
    }

    private async Task TickAsync(DateTime now)
    {
        // Three documents, each with its own timing: the totals, the NPC dialogue and the trades (separate, smaller uploads; nothing in them is checked against the totals)
        bool coreDue = SharePlan.Due(settings.Sharing, now, settings.LastSharedUtc, _notBefore);
        bool partDue = SharePlan.Due(settings.Sharing, now, settings.LastNpcSharedUtc, _partNotBefore);
        bool tradesDue = SharePlan.Due(settings.Sharing, now, settings.LastTradesSharedUtc, _tradesNotBefore);
        if (!coreDue && !partDue && !tradesDue) return;
        Contribution? file;
        try { file = await history.BuildContributionAsync(_app); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { file = null; }
        if (file is null)
        {
            if (coreDue) _notBefore = now + SharePlan.AfterFailure;
            if (partDue) _partNotBefore = now + SharePlan.AfterFailure;
            if (tradesDue) _tradesNotBefore = now + SharePlan.AfterFailure;
            return;
        }

        // The service takes dialogue and trades only for an id it already knows, so the totals go first; if they weren't sent this time the id is known from before
        bool known = !coreDue || await SendTotalsAsync(file, now);
        if (partDue && known) await SendDialogueAsync(file, now);
        if (tradesDue && known) await SendTradesAsync(file, now);
    }

    /// <summary>Sends the totals file if there is something new in it. True when the service now has this id's totals (sent, or unchanged since last time).</summary>
    private async Task<bool> SendTotalsAsync(Contribution file, DateTime now)
    {
        switch (SharePlan.Decide(file, settings.SharedFingerprint, now, settings.LastSharedUtc))
        {
            case ShareStep.NothingToShare:
                _notBefore = now + TimeSpan.FromHours(1);
                Remember("Nothing to share yet: loot, sell or gather something first.");
                return false;
            case ShareStep.Unchanged:
                _notBefore = now + SharePlan.Every;
                return true;
        }

        var result = await client.SendAsync(file.ToCoreJson(), _stop.Token);
        switch (result.Outcome)
        {
            case SendOutcome.Sent:
                _notBefore = null;
                Remember($"Last sent {DateTime.Now:g}.", sent: now, fingerprint: SharePlan.Fingerprint(file));
                return true;
            case SendOutcome.TooSoon:
                _notBefore = now + (result.RetryAfter ?? SharePlan.Every);
                Remember(result.Message);
                return true;   // too soon means the service already has this id's totals
            case SendOutcome.Busy:
                _notBefore = now + (result.RetryAfter ?? TimeSpan.FromHours(1));
                Remember(result.Message);
                return false;
            case SendOutcome.Refused:
                _notBefore = now + SharePlan.AfterRefusal;
                Remember(result.Message);
                return false;
            default:
                _notBefore = now + SharePlan.AfterFailure;
                Remember(result.Message);
                return false;
        }
    }

    /// <summary>Sends the NPC dialogue document if there is dialogue and it isn't the one last sent. What goes wrong is told in the note; what goes right needs no words.</summary>
    private async Task SendDialogueAsync(Contribution file, DateTime now)
    {
        switch (SharePlan.DecidePart(file, settings.SharedNpcFingerprint, now, settings.LastNpcSharedUtc))
        {
            case ShareStep.NothingToShare:
                _partNotBefore = now + TimeSpan.FromHours(1);
                return;
            case ShareStep.Unchanged:
                _partNotBefore = now + SharePlan.Every;
                return;
        }

        var result = await client.SendAsync(file.ToNpcPartJson(), _stop.Token);
        switch (result.Outcome)
        {
            case SendOutcome.Sent:
                _partNotBefore = null;
                RememberDialogue(sent: now, fingerprint: SharePlan.NpcFingerprint(file));
                break;
            case SendOutcome.TooSoon:
                _partNotBefore = now + (result.RetryAfter ?? SharePlan.Every);
                break;
            case SendOutcome.Busy:
                _partNotBefore = now + (result.RetryAfter ?? TimeSpan.FromHours(1));
                break;
            case SendOutcome.Refused:
                _partNotBefore = now + SharePlan.AfterRefusal;
                Remember("The dialogue wasn't taken: " + result.Message);
                break;
            default:
                _partNotBefore = now + SharePlan.AfterFailure;
                break;
        }
    }

    /// <summary>Sends the trades document if there are trades and it isn't the one last sent. What goes wrong is told in the note; what goes right needs no words.</summary>
    private async Task SendTradesAsync(Contribution file, DateTime now)
    {
        switch (SharePlan.DecideTrades(file, settings.SharedTradesFingerprint, now, settings.LastTradesSharedUtc))
        {
            case ShareStep.NothingToShare:
                _tradesNotBefore = now + TimeSpan.FromHours(1);
                return;
            case ShareStep.Unchanged:
                _tradesNotBefore = now + SharePlan.Every;
                return;
        }

        var result = await client.SendAsync(file.ToTradesPartJson(), _stop.Token);
        switch (result.Outcome)
        {
            case SendOutcome.Sent:
                _tradesNotBefore = null;
                RememberTrades(sent: now, fingerprint: SharePlan.TradesFingerprint(file));
                break;
            case SendOutcome.TooSoon:
                _tradesNotBefore = now + (result.RetryAfter ?? SharePlan.Every);
                break;
            case SendOutcome.Busy:
                _tradesNotBefore = now + (result.RetryAfter ?? TimeSpan.FromHours(1));
                break;
            case SendOutcome.Refused:
                _tradesNotBefore = now + SharePlan.AfterRefusal;
                Remember("The trades weren't taken: " + result.Message);
                break;
            default:
                _tradesNotBefore = now + SharePlan.AfterFailure;
                break;
        }
    }

    /// <summary>Notes (on the window's thread) that the trades went and what they looked like, so unchanged ones aren't sent again.</summary>
    private void RememberTrades(DateTime sent, string fingerprint)
    {
        void Write()
        {
            settings.LastTradesSharedUtc = sent;
            settings.SharedTradesFingerprint = fingerprint;
            settings.Save();
        }
        onUi(Write);
    }

    /// <summary>Notes (on the window's thread) that the dialogue went and what it looked like, so an unchanged one isn't sent again.</summary>
    private void RememberDialogue(DateTime sent, string fingerprint)
    {
        void Write()
        {
            settings.LastNpcSharedUtc = sent;
            settings.SharedNpcFingerprint = fingerprint;
            settings.Save();
        }
        onUi(Write);
    }

    /// <summary>Notes what happened (and a successful send) in the settings, on the window's thread.</summary>
    private void Remember(string note, DateTime? sent = null, string? fingerprint = null)
    {
        void Write()
        {
            settings.ShareNote = note;
            if (sent is not null) settings.LastSharedUtc = sent;
            if (fingerprint is not null) settings.SharedFingerprint = fingerprint;
            settings.Save();
        }
        onUi(Write);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
