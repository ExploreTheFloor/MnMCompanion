namespace MnMCompanion.Recipes;

/// <summary>A stretch of levels and the recipes in it, easiest first.</summary>
public sealed record LevelBand(string Title, int? From, int? To, IReadOnlyList<Recipe> Recipes);

/// <summary>
/// Puts a skill's recipes in order of level and cuts them into stretches of <see cref="Width"/> levels ("Level 1–25", "Level 26–50", ...), so a long
/// list can be read from the start of the skill to its end. A skill with only a handful of recipes stays one list. Recipes the wiki has no level for
/// come last, in a band of their own.
/// </summary>
public static class RecipeLevels
{
    public const int Width = 25;

    /// <summary>A skill with fewer recipes than this is not cut up.</summary>
    public const int FlatBelow = 13;

    /// <summary>How many tiers <see cref="Tier"/> tells apart.</summary>
    public const int Tiers = 6;

    /// <summary>
    /// Which stretch of the skill a level belongs to, for colouring: 0 for levels up to 50, then 51–100, 101–150, 151–200, 201–250 and beyond (5).
    /// -1 when the level isn't known. The same tier always gets the same colour, so a glance at a row says how far into the skill it is.
    /// </summary>
    public static int Tier(int? level) => level switch
    {
        null => -1,
        <= 50 => 0,
        <= 100 => 1,
        <= 150 => 2,
        <= 200 => 3,
        <= 250 => 4,
        _ => 5,
    };

    public static int Compare(Recipe a, Recipe b)
    {
        int c = (a.Level ?? int.MaxValue).CompareTo(b.Level ?? int.MaxValue);
        if (c != 0) return c;
        c = string.Compare(a.Result, b.Result, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<Recipe> Sorted(IEnumerable<Recipe> recipes)
    {
        var list = recipes.ToList();
        list.Sort(Compare);
        return list;
    }

    public static IReadOnlyList<LevelBand> Bands(IEnumerable<Recipe> recipes)
    {
        var sorted = Sorted(recipes);
        if (sorted.Count == 0) return [];
        if (sorted.Count < FlatBelow) return [new LevelBand("All levels", sorted.FirstOrDefault(r => r.Level.HasValue)?.Level, sorted.LastOrDefault(r => r.Level.HasValue)?.Level, sorted)];

        var bands = new List<LevelBand>();
        foreach (var group in sorted.Where(r => r.Level.HasValue).GroupBy(r => Bucket(r.Level!.Value)))
        {
            int from = group.Key * Width + 1, to = from + Width - 1;
            bands.Add(new LevelBand($"Level {from}–{to}", from, to, group.ToList()));
        }
        var unknown = sorted.Where(r => !r.Level.HasValue).ToList();
        if (unknown.Count > 0) bands.Add(new LevelBand("Level not on the wiki yet", null, null, unknown));
        return bands;
    }

    /// <summary>Levels 1–25 are stretch 0, 26–50 stretch 1, and so on; a level 0 goes with the first.</summary>
    private static int Bucket(int level) => level <= 0 ? 0 : (level - 1) / Width;
}
