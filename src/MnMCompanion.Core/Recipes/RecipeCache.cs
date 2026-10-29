namespace MnMCompanion.Recipes;

/// <summary>
/// The recipe list as the wiki sent it, kept on this PC (<c>recipes.json</c> in the app's folder) so the Recipes page opens at once and works offline. The file's
/// own time is when it was fetched. It holds only the wiki's public data, nothing from the player's game.
/// </summary>
public static class RecipeCache
{
    /// <summary>The list is fetched again when it is older than this (and only if the player has said the wiki may be asked).</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromDays(7);

    public static string DefaultPath => Path.Combine(AppFolder.Folder, "recipes.json");

    /// <summary>The kept list and when it was fetched (UTC); null when there is none or it can't be read.</summary>
    public static (RecipeBook Book, DateTime FetchedUtc)? Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return null;
            return RecipeBook.TryParse(File.ReadAllText(path)) is { } book ? (book, File.GetLastWriteTimeUtc(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Keeps <paramref name="json"/> as the list. Returns false (and keeps what was there) when it can't be written.</summary>
    public static bool Save(string json, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static bool IsStale(DateTime fetchedUtc, DateTime? nowUtc = null) => (nowUtc ?? DateTime.UtcNow) - fetchedUtc > Stale;

    /// <summary>"today", "yesterday", "3 days ago".</summary>
    public static string Age(DateTime fetchedUtc, DateTime? nowUtc = null)
    {
        int days = (int)Math.Floor(((nowUtc ?? DateTime.UtcNow) - fetchedUtc).TotalDays);
        return days <= 0 ? "today" : days == 1 ? "yesterday" : $"{days} days ago";
    }
}
