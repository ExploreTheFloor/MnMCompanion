namespace MnMCompanion.History;

/// <summary>One kind of thing gathered: how many times, how many units, where, and when.</summary>
public sealed record HarvestItem(string Item, int Harvests, int Units, IReadOnlyList<string> Zones, DateTime First, DateTime Last);

/// <summary>
/// Gathering over some rows. <see cref="Active"/> is the time actually spent gathering: the gaps between one harvest and the next,
/// leaving out any gap longer than <see cref="HarvestSummary.MaxGap"/> (a break, a trip to town). <see cref="PerHour"/> is null
/// until there is a minute of it to divide by.
/// </summary>
public sealed record HarvestTotals(int Harvests, int Units, int Kinds, TimeSpan Active)
{
    public double? PerHour => Active.TotalMinutes >= 1 ? Units / Active.TotalHours : null;
}

public static class HarvestSummary
{
    /// <summary>A pause longer than this between two harvests is a break, not gathering.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(10);

    public static TimeSpan Active(IEnumerable<HarvestRow> rows)
    {
        var times = rows.Select(r => r.Time).Order().ToList();
        var total = TimeSpan.Zero;
        for (int i = 1; i < times.Count; i++)
        {
            var gap = times[i] - times[i - 1];
            if (gap <= MaxGap) total += gap;
        }
        return total;
    }

    public static HarvestTotals Totals(IEnumerable<HarvestRow> rows)
    {
        var list = rows.ToList();
        return new HarvestTotals(list.Count, list.Sum(r => r.Qty), list.Select(r => r.Item).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Active(list));
    }

    /// <summary>The items, most units first.</summary>
    public static IReadOnlyList<HarvestItem> ByItem(IEnumerable<HarvestRow> rows) =>
        rows.GroupBy(r => r.Item, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HarvestItem(g.First().Item, g.Count(), g.Sum(r => r.Qty),
                g.Select(r => r.Zone).Where(z => z.Length > 0).GroupBy(z => z, StringComparer.OrdinalIgnoreCase).OrderByDescending(z => z.Count()).Select(z => z.Key).ToList(),
                g.Min(r => r.Time), g.Max(r => r.Time)))
            .OrderByDescending(i => i.Units).ThenBy(i => i.Item, StringComparer.OrdinalIgnoreCase).ToList();
}
