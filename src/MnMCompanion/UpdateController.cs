using System.Diagnostics;
using System.IO;
using MnMCompanion.Update;

namespace MnMCompanion;

internal enum UpdateState { Idle, Checking, Available, Downloading, Waiting, Installing, Failed, Notice }

/// <summary>
/// The app's side of updating: when to look (a few seconds after start and every six hours, if the player allows it, or when they
/// ask), what the banner should say, and the install itself. A quiet check that finds a newer version installs it by itself (download,
/// check against the published checksum, restart) unless the player switched that off in the ⓘ menu, in which case it only offers and
/// they press Update. A check the player asked for only ever offers. See <see cref="UpdatePolicy"/> for when it does not install by itself.
/// All members are for the UI thread; <see cref="Changed"/> fires on it too.
/// </summary>
internal sealed class UpdateController : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    private readonly Settings _settings;
    private readonly UpdateService _service;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _folder = Path.Combine(Settings.Folder, "update");
    private readonly string? _exe = Environment.ProcessPath;
    private bool _checking;
    private bool _declined;         // "Not now": no install by itself for the rest of this run
    private bool _restartRequested; // "Restart now": don't wait for the timers

    public UpdateController(Settings settings, UpdateService? service = null)
    {
        _settings = settings;
        _service = service ?? new UpdateService(AppVersion.Current());
    }

    public event Action? Changed;

    /// <summary>The new program has been put in place and started: this one should close now.</summary>
    public event Action? ExitRequested;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public UpdateOffer? Offer { get; private set; }

    /// <summary>What to tell the player: the offer, the progress, or what went wrong.</summary>
    public string Message { get; private set; } = "";

    /// <summary>0..1 while downloading.</summary>
    public double Progress { get; private set; }

    public Version Current => _service.Current;

    /// <summary>True while something the player started is counting down (a timer): an update that installs by itself waits for it, so no timer's end is missed during the restart. Null: never.</summary>
    public Func<bool>? Busy { get; set; }

    /// <summary>
    /// Only a published single-file program can replace itself; a build run from the source folder (or by <c>dotnet run</c>) cannot,
    /// and is sent to the download page instead.
    /// </summary>
#pragma warning disable IL3000   // an empty Location is exactly how a single-file program tells itself apart from a build run from the source folder
    public bool CanInstall { get; } = string.IsNullOrEmpty(System.Reflection.Assembly.GetEntryAssembly()?.Location) && UpdateService.CanReplace(Environment.ProcessPath);
#pragma warning restore IL3000

    /// <summary>Whether the banner should show.</summary>
    public bool Visible => State is not UpdateState.Idle;

    /// <summary>For a picture of the banner (<c>--snapshot --window --preview-update</c>): behaves as if this offer had just arrived.</summary>
    public void Preview(UpdateOffer offer)
    {
        Offer = offer;
        Set(UpdateState.Available, "");
    }

    /// <summary>Tidies up after an update and starts the quiet checking.</summary>
    public void Start()
    {
        UpdateService.CleanUp(_exe, _folder);
        _ = Loop();
    }

    private async Task Loop()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token);
            using var timer = new PeriodicTimer(Every);
            do
            {
                if (_settings.CheckForUpdates && State is UpdateState.Idle or UpdateState.Failed or UpdateState.Notice) await CheckAsync(manual: false);
            }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Looks now. A quiet check says nothing unless there is something to offer; a manual one (<paramref name="manual"/>) also
    /// says "you're up to date" or why it couldn't tell, and offers a version that was skipped.
    /// </summary>
    public async Task CheckAsync(bool manual)
    {
        if (_checking || State is UpdateState.Downloading or UpdateState.Waiting or UpdateState.Installing) return;
        _checking = true;
        bool installByItself = false;
        if (manual) Set(UpdateState.Checking, "Looking for a newer version…");
        try
        {
            var result = await _service.CheckAsync(_stop.Token);
            switch (result.Outcome)
            {
                case CheckOutcome.Available when result.Offer is { } offer:
                    if (!manual && _settings.SkippedVersion == AppVersion.Show(offer.Version)) { QuietlyIdle(); break; }
                    Offer = offer;
                    installByItself = !manual && UpdatePolicy.InstallWithoutAsking(_settings.InstallUpdates, CanInstall, offer, _settings.SkippedVersion, _settings.AutoInstalledVersion, _declined);
                    if (!installByItself) Set(UpdateState.Available, result.Message);   // otherwise the banner goes straight to "downloading"
                    break;
                case CheckOutcome.Failed when manual:
                    Set(UpdateState.Failed, result.Message);
                    break;
                case CheckOutcome.Failed:
                    QuietlyIdle();
                    break;
                default:
                    // Up to date, or nothing to compare with: said only when the player asked
                    if (manual) Set(UpdateState.Notice, result.Outcome == CheckOutcome.UpToDate ? $"You have the newest version ({AppVersion.Show(Current)})." : result.Message);
                    else QuietlyIdle();
                    break;
            }
        }
        catch (OperationCanceledException) { }
        finally { _checking = false; }
        if (installByItself) await InstallAsync(byItself: true);
    }

    /// <summary>A quiet check with nothing to say: the banner goes, except a notice that is still waiting to be read (the one after an update).</summary>
    private void QuietlyIdle()
    {
        if (State is not UpdateState.Notice) Set(UpdateState.Idle, "");
    }

    /// <summary>"Update now": downloads, checks and swaps in the new program, then restarts into it.</summary>
    public Task InstallAsync() => InstallAsync(byItself: false);

    /// <param name="byItself">Nobody pressed anything: the restart waits until no timer is running, and the version is noted so it is never installed this way twice.</param>
    private async Task InstallAsync(bool byItself)
    {
        if (Offer is not { } offer || State is UpdateState.Downloading or UpdateState.Waiting or UpdateState.Installing) return;
        if (!CanInstall)
        {
            OpenDownloadPage();
            return;
        }
        try
        {
            Progress = 0;
            Set(UpdateState.Downloading, $"{(byItself ? "Updating: downloading" : "Downloading")} version {AppVersion.Show(offer.Version)}…");
            var progress = new Progress<double>(p => { Progress = p; Changed?.Invoke(); });
            var file = await _service.DownloadAsync(offer, _folder, progress, _stop.Token);
            if (byItself)
            {
                if (!await WaitForQuietAsync(offer)) return;
                _settings.AutoInstalledVersion = AppVersion.Show(offer.Version);
                _settings.Save();
            }
            Set(UpdateState.Installing, "Installing and restarting…");
            // --updated: the new program waits for this one to exit before it takes the timers' keys
            UpdateService.Apply(file, _exe!, exe => Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true }) is not null);
            ExitRequested?.Invoke();
        }
        catch (UpdateException ex)
        {
            Set(UpdateState.Failed, ex.Message);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// The download is here and checked. Waits (showing why) while a timer is running, then returns true. False if the player pressed "Not now"
    /// or switched automatic updating off meanwhile; the banner then goes and the version is offered the ordinary way at the next check.
    /// </summary>
    private async Task<bool> WaitForQuietAsync(UpdateOffer offer)
    {
        _restartRequested = false;
        if (!UpdatePolicy.MayRestart(Busy?.Invoke() == true, _restartRequested))
            Set(UpdateState.Waiting, WaitingText(offer));
        while (!UpdatePolicy.MayRestart(Busy?.Invoke() == true, _restartRequested))
        {
            await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);
            if (_declined || !_settings.CheckForUpdates || !_settings.InstallUpdates)
            {
                Set(UpdateState.Idle, "");
                return false;
            }
        }
        return true;
    }

    private static string WaitingText(UpdateOffer offer) => $"Version {AppVersion.Show(offer.Version)} is downloaded and checked. Companion restarts into it when your timers have run out.";

    /// <summary>"Restart now": don't wait for the timers.</summary>
    public void RestartNow() => _restartRequested = true;

    /// <summary>"Not now": nothing installs by itself for the rest of this run; the version is offered with the ordinary buttons at the next check.</summary>
    public void NotNow()
    {
        _declined = true;
        Set(UpdateState.Idle, "");
    }

    /// <summary>After a restart into a new version (<c>--updated</c>): says so once, and where to switch it off.</summary>
    public void Announce()
    {
        var off = _settings.CheckForUpdates && _settings.InstallUpdates ? " Updates install by themselves; switch that off under ⓘ → Install updates automatically." : "";
        Set(UpdateState.Notice, $"Companion is now version {AppVersion.Show(Current)}.{off}");
    }

    /// <summary>For a picture of the banner while it waits for the timers (<c>--snapshot --window --preview-waiting</c>).</summary>
    public void PreviewWaiting(UpdateOffer offer)
    {
        Offer = offer;
        Set(UpdateState.Waiting, WaitingText(offer));
    }

    /// <summary>"Skip this version": the banner goes and this version isn't offered again by the quiet checks.</summary>
    public void Skip()
    {
        if (Offer is { } offer)
        {
            _settings.SkippedVersion = AppVersion.Show(offer.Version);
            _settings.Save();
        }
        Dismiss();
    }

    /// <summary>"Later" / close: the banner goes until the next check.</summary>
    public void Dismiss() => Set(UpdateState.Idle, "");

    public void OpenDownloadPage() => OpenPage(Offer?.PageUrl ?? UpdateService.ReleasesUrl);

    public void OpenProjectPage() => OpenPage(UpdateService.ProjectUrl);

    private static void OpenPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private void Set(UpdateState state, string message)
    {
        State = state;
        Message = message;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _service.Dispose();
        _stop.Dispose();
    }
}
