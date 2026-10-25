namespace MnMCompanion.Loot;

/// <summary>
/// Puts the loot of several characters of one player together. When two of them were in the same group, both ledgers record what the others
/// looted (and each records its own), so the same event is in both; it must count once. An event has one timestamp whichever client wrote it,
/// so two events are the same one when they agree on when, who, what and how many. If one ledger holds an event three times (three identical
/// items off one corpse in the same second) and another holds it twice, that is three events, not five: for each kind of event the most any
/// one ledger saw is how many there were.
/// </summary>
public static class LootMerge
{
    private readonly record struct Key(DateTime Time, LootKind Kind, string Who, string Source, string Item, int Count, long Copper);

    private static Key KeyOf(LootEvent e) =>
        new(e.Time, e.Kind, e.Who.ToLowerInvariant(), e.Source.ToLowerInvariant(), e.Item.ToLowerInvariant(), e.Count, e.Copper);

    /// <summary>One list of events per character, merged oldest first. A single list is returned as it is.</summary>
    public static List<LootEvent> Combine(IReadOnlyList<IReadOnlyList<LootEvent>> perCharacter)
    {
        if (perCharacter.Count == 1) return perCharacter[0].ToList();
        var groups = new Dictionary<Key, List<List<LootEvent>>>();
        for (int i = 0; i < perCharacter.Count; i++)
            foreach (var g in perCharacter[i].GroupBy(KeyOf))
            {
                if (!groups.TryGetValue(g.Key, out var lists)) groups[g.Key] = lists = [];
                lists.Add(g.ToList());
            }

        var merged = new List<LootEvent>();
        foreach (var lists in groups.Values)
            for (int k = 0, n = lists.Max(l => l.Count); k < n; k++)
                merged.Add(One(lists.Where(l => l.Count > k).Select(l => l[k]).ToList()));
        return merged.OrderBy(e => e.Time).ToList();
    }

    /// <summary>
    /// The same event as several characters saw it. The looter's own ledger calls the class "None" and the others give the real one, so the
    /// real one is kept; it is "you" if it was any of theirs; and the purse shares add up, since each character was handed its own.
    /// </summary>
    private static LootEvent One(List<LootEvent> seen)
    {
        var best = seen.FirstOrDefault(e => e.Class.Length > 0 && !e.Class.Equals("None", StringComparison.OrdinalIgnoreCase)) ?? seen[0];
        return best with
        {
            You = seen.Any(e => e.You),
            Split = seen.Sum(e => e.Split),
            Zone = seen.Select(e => e.Zone).FirstOrDefault(z => z.Length > 0) ?? "",
            Level = seen.Max(e => e.Level),
            ItemId = seen.Select(e => e.ItemId).FirstOrDefault(z => z.Length > 0) ?? "",
        };
    }
}
