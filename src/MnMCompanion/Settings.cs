using System.IO;
using System.Text.Json;

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

    /// <summary>The page of the History window that was open last ("Loot", "Trades", "Vendors", "Changes" or "Harvest").</summary>
    public string? HistoryPage { get; set; }

    /// <summary>
    /// The player's answer to "share your data with the community?". No (false) until they say Yes. While it is No nothing is sent, they are
    /// asked again every time the app starts, and community data isn't used.
    /// </summary>
    public bool ShareWithCommunity { get; set; }

    /// <summary>Look for a newer version on GitHub when the app starts and every few hours. It only ever offers; nothing installs by itself.</summary>
    public bool CheckForUpdates { get; set; } = true;

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
