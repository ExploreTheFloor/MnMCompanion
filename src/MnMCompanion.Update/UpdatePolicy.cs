namespace MnMCompanion.Update;

/// <summary>
/// When a newer version is installed without the player pressing anything. The download itself is always checked against the checksum
/// published with the release (see <see cref="UpdateService.DownloadAsync"/>); this is only the decision of whether to ask first.
/// </summary>
public static class UpdatePolicy
{
    /// <summary>
    /// Whether a quiet check that found <paramref name="offer"/> goes on to install it by itself. It does not when the player switched that
    /// off, when this copy can't replace itself (it is then sent to the download page), when they chose "Skip this version" for it, when
    /// they said "Not now" during this run, or when this program already installed that version once and is still older than it (so a
    /// release that reports the wrong version can never make the program install and restart over and over).
    /// </summary>
    public static bool InstallWithoutAsking(bool installAutomatically, bool canInstall, UpdateOffer offer, string? skippedVersion, string? lastInstalledVersion, bool declinedThisRun)
    {
        if (!installAutomatically || !canInstall || declinedThisRun) return false;
        var version = AppVersion.Show(offer.Version);
        return version != skippedVersion && version != lastInstalledVersion;
    }

    /// <summary>
    /// Whether to restart into the new version now. It waits while something the player started is counting down (a timer's end would be
    /// missed in the seconds the program takes to start again), unless they pressed "Restart now".
    /// </summary>
    public static bool MayRestart(bool somethingRunning, bool restartRequested) => restartRequested || !somethingRunning;
}
