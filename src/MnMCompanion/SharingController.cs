using System.Windows;
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
internal sealed class SharingController(Settings settings, HistoryData history, CommunityClient client) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly string _app = "MnMCompanion " + AppVersion.Show(AppVersion.Current());
    private DateTime? _notBefore;

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
        if (!SharePlan.Due(settings.Sharing, now, settings.LastSharedUtc, _notBefore)) return;
        Contribution? file;
        try { file = await history.BuildContributionAsync(_app); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { file = null; }
        if (file is null)
        {
            _notBefore = now + SharePlan.AfterFailure;
            return;
        }

        switch (SharePlan.Decide(file, settings.SharedFingerprint, now, settings.LastSharedUtc))
        {
            case ShareStep.NothingToShare:
                _notBefore = now + TimeSpan.FromHours(1);
                Remember("Nothing to share yet: loot, sell or gather something first.");
                return;
            case ShareStep.Unchanged:
                _notBefore = now + SharePlan.Every;
                return;
        }

        var result = await client.SendAsync(file.ToJson(), _stop.Token);
        switch (result.Outcome)
        {
            case SendOutcome.Sent:
                _notBefore = null;
                Remember($"Last sent {DateTime.Now:g}.", sent: now, fingerprint: SharePlan.Fingerprint(file));
                break;
            case SendOutcome.TooSoon:
                _notBefore = now + (result.RetryAfter ?? SharePlan.Every);
                Remember(result.Message);
                break;
            case SendOutcome.Busy:
                _notBefore = now + (result.RetryAfter ?? TimeSpan.FromHours(1));
                Remember(result.Message);
                break;
            case SendOutcome.Refused:
                _notBefore = now + SharePlan.AfterRefusal;
                Remember(result.Message);
                break;
            default:
                _notBefore = now + SharePlan.AfterFailure;
                Remember(result.Message);
                break;
        }
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
        if (Application.Current?.Dispatcher is { } d) d.Invoke(Write); else Write();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
