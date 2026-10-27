namespace MnMCompanion;

/// <summary>
/// Where the app keeps its own files (settings, database, downloaded updates): <c>%LOCALAPPDATA%\MnMCompanion</c>. The program used to be
/// called Loot Goblin and kept them in <c>...\LootWindow</c>; the first time anything asks, that folder is moved across, whole, so the
/// settings and the database (which holds ledger rows the game has since thrown away) carry on. If it can't be moved (another copy still has a
/// file open) the old folder is used where it is, rather than starting a second, empty one.
/// </summary>
public static class AppFolder
{
    public const string Name = "MnMCompanion";

    /// <summary>The folder's name before the rename.</summary>
    public const string PreviousName = "LootWindow";

    /// <summary>
    /// Names a folder to use instead, for the whole app: its settings, database and timers. For tests and pictures of the app running, which must never
    /// touch the player's own files. (Setting <c>LOCALAPPDATA</c> does not do this: Windows is asked for that folder directly, not through the variable.)
    /// </summary>
    public const string OverrideVariable = "MNMCOMPANION_DATA";

    private static readonly Lazy<string> InUse = new(() =>
        Choose(Environment.GetEnvironmentVariable(OverrideVariable), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

    public static string Folder => InUse.Value;

    /// <summary>The folder to use: <paramref name="overrideFolder"/> when one was named (the variable above), otherwise the usual one under <paramref name="localAppData"/>.</summary>
    public static string Choose(string? overrideFolder, string localAppData) =>
        string.IsNullOrWhiteSpace(overrideFolder) ? Resolve(localAppData) : Path.GetFullPath(overrideFolder.Trim());

    /// <summary>The folder to use under <paramref name="localAppData"/>, moving the old one in first when only the old one exists.</summary>
    public static string Resolve(string localAppData)
    {
        var current = Path.Combine(localAppData, Name);
        var previous = Path.Combine(localAppData, PreviousName);
        if (Directory.Exists(current) || !Directory.Exists(previous)) return current;
        try
        {
            Directory.Move(previous, current);
            return current;
        }
        catch (IOException) { return previous; }
        catch (UnauthorizedAccessException) { return previous; }
    }
}
