using System.Diagnostics;
using System.IO;
using System.Windows;
using LootWindow.Update;

namespace LootWindow;

internal enum UpdateState { Idle, Checking, Available, Downloading, Installing, Failed, Notice }

/// <summary>
/// The app's side of updating: when to look (a few seconds after start and every six hours, if the player allows it, or when they
/// ask), what the banner should say, and the install itself once the player presses Update. It never installs unasked.
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

    public UpdateController(Settings settings, UpdateService? service = null)
    {
        _settings = settings;
        _service = service ?? new UpdateService(AppVersion.Current());
    }

    public event Action? Changed;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public UpdateOffer? Offer { get; private set; }

    /// <summary>What to tell the player: the offer, the progress, or what went wrong.</summary>
    public string Message { get; private set; } = "";

    /// <summary>0..1 while downloading.</summary>
    public double Progress { get; private set; }

    public Version Current => _service.Current;

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
                if (_settings.CheckForUpdates && State is UpdateState.Idle or UpdateState.Failed) await CheckAsync(manual: false);
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
        if (_checking || State is UpdateState.Downloading or UpdateState.Installing) return;
        _checking = true;
        if (manual) Set(UpdateState.Checking, "Looking for a newer version…");
        try
        {
            var result = await _service.CheckAsync(_stop.Token);
            switch (result.Outcome)
            {
                case CheckOutcome.Available when result.Offer is { } offer:
                    if (!manual && _settings.SkippedVersion == AppVersion.Show(offer.Version)) { Set(UpdateState.Idle, ""); break; }
                    Offer = offer;
                    Set(UpdateState.Available, result.Message);
                    break;
                case CheckOutcome.Failed when manual:
                    Set(UpdateState.Failed, result.Message);
                    break;
                case CheckOutcome.Failed:
                    Set(UpdateState.Idle, "");
                    break;
                default:
                    // Up to date, or nothing to compare with: said only when the player asked
                    if (manual) Set(UpdateState.Notice, result.Outcome == CheckOutcome.UpToDate ? $"You have the newest version ({AppVersion.Show(Current)})." : result.Message);
                    else Set(UpdateState.Idle, "");
                    break;
            }
        }
        catch (OperationCanceledException) { }
        finally { _checking = false; }
    }

    /// <summary>"Update": downloads, checks and swaps in the new program, then restarts into it.</summary>
    public async Task InstallAsync()
    {
        if (Offer is not { } offer || State is UpdateState.Downloading or UpdateState.Installing) return;
        if (!CanInstall)
        {
            OpenDownloadPage();
            return;
        }
        try
        {
            Progress = 0;
            Set(UpdateState.Downloading, $"Downloading version {AppVersion.Show(offer.Version)}…");
            var progress = new Progress<double>(p => { Progress = p; Changed?.Invoke(); });
            var file = await _service.DownloadAsync(offer, _folder, progress, _stop.Token);
            Set(UpdateState.Installing, "Installing and restarting…");
            UpdateService.Apply(file, _exe!, exe => Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true }) is not null);
            Application.Current.Shutdown();
        }
        catch (UpdateException ex)
        {
            Set(UpdateState.Failed, ex.Message);
        }
        catch (OperationCanceledException) { }
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
