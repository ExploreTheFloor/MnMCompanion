using System.IO;
using System.Text.Json;
using MnMCompanion.Community;

namespace MnMCompanion;

/// <summary>Where a window sits; NaN Left/Top = not placed yet.</summary>
public sealed record Bounds(double Left, double Top, double Width, double Height);

/// <summary>
/// What the app remembers between runs, in <c>%LOCALAPPDATA%\MnMCompanion\settings.json</c>: which character, the ledger folder
/// when it isn't in the default place, the panel's tab, and where the windows were. It holds nothing from the game.
/// </summary>
public sealed class Settings
{
    public string? Server { get; set; }
    public string? Character { get; set; }

    /// <summary>The game's per-character folder when it isn't in the default place under LocalLow.</summary>
    public string? LedgerRoot { get; set; }

    public string View { get; set; } = "Value";
    public bool OnTop { get; set; } = true;

    /// <summary>The page the small window was left on: Map, Loot, Vendor, Trades, Items or Harvest.</summary>
    public string MiniPage { get; set; } = "Loot";

    /// <summary>
    /// The player's choice to show the map of their zone live from the community atlas (mnmatlas.com). No until they say so: showing it makes the
    /// atlas's site see which zone they are in, and their address, like opening it in a browser.
    /// </summary>
    public bool MapsOnline { get; set; }

    /// <summary>
    /// What the History window shows, once the player has chosen: a server and a character, a server alone (everyone on it), or neither
    /// (everyone). Until <see cref="HistoryScopeChosen"/> it shows the character the small window follows.
    /// </summary>
    public string? HistoryServer { get; set; }
    public string? HistoryCharacter { get; set; }
    public bool HistoryScopeChosen { get; set; }
    public Bounds? Panel { get; set; }
    public Bounds? Breakdown { get; set; }

    /// <summary>The timers window's place, and whether it was open when the app closed (it comes back the same way).</summary>
    public Bounds? Timers { get; set; }
    public bool TimersOpen { get; set; }

    /// <summary>The timers window's own pin (on top of the game or not; off until the player pins it) and how solid its background is, in percent (see <c>TimerLook</c>; 100 is no fading).</summary>
    public bool TimersOnTop { get; set; }
    public int TimersOpacity { get; set; } = 100;

    /// <summary>Whether the timers window shows each timer as a button across its top, to start and stop it with a click.</summary>
    public bool TimersButtons { get; set; }

    /// <summary>The page of the History window that was open last ("Loot", "Trades", "Vendors", "Changes" or "Harvest").</summary>
    public string? HistoryPage { get; set; }

    /// <summary>
    /// The player's answer to "share your data with the community?". No (false) until they say Yes. While it is No nothing is sent, they are
    /// asked again every time the app starts, and community data isn't used.
    /// </summary>
    public bool ShareWithCommunity { get; set; }

    /// <summary>
    /// The file layout (<see cref="ContributionBuilder.Schema"/>) the player's Yes was given to; 0 where it was never recorded, which is a Yes from
    /// layout 1. A Yes counts only for the layout the player was shown: when the shared file grows, everyone is asked again
    /// (<see cref="Sharing"/> is false until they say Yes to the new one).
    /// </summary>
    public int ShareConsentSchema { get; set; }

    /// <summary>Whether sharing is on: a Yes that covers the file as it is now. Everything that sends, asks or uses community data goes by this, not by the raw answer.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Sharing => CommunityPolicy.Consented(ShareWithCommunity, ShareConsentSchema, ContributionBuilder.Schema);

    /// <summary>A Yes given to an older file layout: sharing is paused and the question says what has been added.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ConsentNeedsRenewal => CommunityPolicy.NeedsRenewal(ShareWithCommunity, ShareConsentSchema, ContributionBuilder.Schema);

    /// <summary>Records the player's answer: a Yes is a Yes to the file as it is now.</summary>
    public void SetSharing(bool yes)
    {
        ShareWithCommunity = yes;
        if (yes) ShareConsentSchema = ContributionBuilder.Schema;
    }

    /// <summary>When the player's totals were last sent to the community service (UTC), a fingerprint of that file (so an unchanged one is not sent again), and a sentence about how the last try went.</summary>
    public DateTime? LastSharedUtc { get; set; }
    public string? SharedFingerprint { get; set; }

    /// <summary>The same two for the NPC dialogue, which is sent as a second, separate document.</summary>
    public DateTime? LastNpcSharedUtc { get; set; }
    public string? SharedNpcFingerprint { get; set; }

    /// <summary>The same two for the trades with other players, which are sent as a third, separate document.</summary>
    public DateTime? LastTradesSharedUtc { get; set; }
    public string? SharedTradesFingerprint { get; set; }
    public string ShareNote { get; set; } = "";

    /// <summary>
    /// A player joining the group (or you joining a group) starts a new session by itself: the loot so far is archived and every page starts again from the join.
    /// On by default; off, a session starts only when the player presses New session.
    /// </summary>
    public bool NewSessionOnJoin { get; set; } = true;

    /// <summary>Look for a newer version on GitHub when the app starts and every few hours. Off: the app never asks GitHub by itself.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Install a newer version without asking: download it, check it against the checksum published with it, and restart into it once no timer is running.
    /// On by default; off, a newer version is only offered and the player presses Update now. Needs <see cref="CheckForUpdates"/>.
    /// </summary>
    public bool InstallUpdates { get; set; } = true;

    /// <summary>The version this program last installed by itself. If the program that starts afterwards is still older than that, it is never installed unasked again (no restart loop).</summary>
    public string? AutoInstalledVersion { get; set; }

    /// <summary>A version the player chose "Skip this version" for: not offered again (a newer one still is).</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>A picture-taking run: nothing is written.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReadOnly { get; set; }

    public static string Folder => AppFolder.Folder;

    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) is { } s) return s;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new Settings();
    }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
