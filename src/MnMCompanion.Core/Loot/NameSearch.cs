namespace MnMCompanion.Loot;

/// <summary>
/// The search box over a list of items: a name matches when it has every word that was typed, in any case and in any order, so
/// "iron ore" finds "Iron Ore" and "Ore of Iron", and a part of a word is enough ("rawh back"). An empty search matches everything.
/// </summary>
public static class NameSearch
{
    /// <summary>The words of a search, with the blanks between them dropped.</summary>
    public static string[] Words(string? query) =>
        (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Matches(string? name, string? query)
    {
        var words = Words(query);
        if (words.Length == 0) return true;
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var word in words)
            if (!name.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// A search over several columns of one row: every word has to be in at least one of the <paramref name="fields"/> (not all in the same
    /// one), so "orrin scraps" finds a row whose character is Orrin and whose item is Iron Scraps.
    /// </summary>
    public static bool MatchesAny(string? query, params string?[] fields)
    {
        foreach (var word in Words(query))
        {
            var found = false;
            foreach (var field in fields)
                if (field is not null && field.Contains(word, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>The rows whose name matches, in their own order.</summary>
    public static IReadOnlyList<T> Filter<T>(IReadOnlyList<T> rows, Func<T, string?> name, string? query) =>
        Words(query).Length == 0 ? rows : rows.Where(r => Matches(name(r), query)).ToList();
}
