namespace MnMCompanion.History;

/// <summary>Units dropped, picked up and destroyed, and how many of the picked-up ones were the player taking back what they had just dropped.</summary>
public sealed record ItemChangeTotals(int Dropped, int PickedUp, int Destroyed, int TakenBack);

/// <summary>One item's fate: how much was dropped, destroyed and picked up. <see cref="Lost"/> is what is gone for good as far as these rows say.</summary>
public sealed record DiscardedItem(string Item, int Dropped, int Destroyed, int PickedUp)
{
    public int Lost => Math.Max(0, Dropped + Destroyed - PickedUp);
}

public static class ItemChangeSummary
{
    /// <summary>A pickup this soon after dropping the same item, by the same character, is them taking it back.</summary>
    public static readonly TimeSpan TakeBackWindow = TimeSpan.FromMinutes(10);

    public static ItemChangeTotals Totals(IEnumerable<ItemMoveRow> rows)
    {
        var list = rows.OrderBy(r => r.Time).ToList();
        return new ItemChangeTotals(Units(list, ItemMoveKind.Dropped), Units(list, ItemMoveKind.PickedUp), Units(list, ItemMoveKind.Destroyed), TakenBackByItemAndZone(list).Values.Sum());
    }

    /// <summary>
    /// Units taken back, each counted against the item and zone it was dropped in (both in lower case): a pickup within <see cref="TakeBackWindow"/> of a drop of the
    /// same item by the same character takes back what is left of the earliest such drop first.
    /// </summary>
    public static IReadOnlyDictionary<(string Item, string Zone), int> TakenBackByItemAndZone(IEnumerable<ItemMoveRow> rows)
    {
        var taken = new Dictionary<(string Item, string Zone), int>();
        // Drops still waiting to be matched by a pickup, per character and item
        var open = new Dictionary<(string, string), List<(DateTime Time, int Left, string Zone)>>();
        foreach (var r in rows.OrderBy(r => r.Time))
        {
            var key = (r.Character.ToLowerInvariant(), r.Item.ToLowerInvariant());
            if (r.Kind == ItemMoveKind.Dropped)
            {
                if (!open.TryGetValue(key, out var drops)) open[key] = drops = [];
                drops.Add((r.Time, r.Qty, r.Zone.ToLowerInvariant()));
            }
            else if (r.Kind == ItemMoveKind.PickedUp && open.TryGetValue(key, out var waiting))
            {
                int need = r.Qty;
                for (int i = 0; i < waiting.Count && need > 0; i++)
                {
                    if (r.Time - waiting[i].Time > TakeBackWindow) continue;
                    int take = Math.Min(need, waiting[i].Left);
                    need -= take;
                    var zoneKey = (key.Item2, waiting[i].Zone);
                    taken[zoneKey] = taken.GetValueOrDefault(zoneKey) + take;
                    waiting[i] = (waiting[i].Time, waiting[i].Left - take, waiting[i].Zone);
                }
            }
        }
        return taken;
    }

    private static int Units(List<ItemMoveRow> rows, ItemMoveKind kind) => rows.Where(r => r.Kind == kind).Sum(r => r.Qty);

    /// <summary>Per item, the most lost first.</summary>
    public static IReadOnlyList<DiscardedItem> MostDiscarded(IEnumerable<ItemMoveRow> rows) =>
        rows.GroupBy(r => r.Item, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DiscardedItem(g.First().Item, Sum(g, ItemMoveKind.Dropped), Sum(g, ItemMoveKind.Destroyed), Sum(g, ItemMoveKind.PickedUp)))
            .OrderByDescending(i => i.Lost).ThenByDescending(i => i.Dropped + i.Destroyed).ThenBy(i => i.Item, StringComparer.OrdinalIgnoreCase).ToList();

    private static int Sum(IEnumerable<ItemMoveRow> rows, ItemMoveKind kind) => rows.Where(r => r.Kind == kind).Sum(r => r.Qty);
}
