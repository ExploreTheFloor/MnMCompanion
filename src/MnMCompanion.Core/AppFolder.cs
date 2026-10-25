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

    private static readonly Lazy<string> InUse = new(() => Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

    public static string Folder => InUse.Value;

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
