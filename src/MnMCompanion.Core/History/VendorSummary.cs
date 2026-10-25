namespace MnMCompanion.History;

/// <summary>What the vendors paid and charged over some rows. <see cref="Net"/> is what came in minus what went out.</summary>
public sealed record VendorTotals(long SoldCopper, int SoldUnits, int SoldStacks, long BoughtCopper, int BoughtUnits, int BoughtStacks, string BestSeller, long BestSellerCopper)
{
    public long Net => SoldCopper - BoughtCopper;
}

/// <summary>One item over some rows of one direction: stacks, units, the coin, the middle price for one, and the last time.</summary>
public sealed record VendorItem(string Item, int Stacks, int Units, long Total, long Each, DateTime Last);

/// <summary>
/// What vendors pay for an item and what they charge, from every sale and purchase on this PC. <see cref="Median"/> is the middle
/// price for one (the figure the item values use), <see cref="Min"/> and <see cref="Max"/> the spread (vendors differ, and items
/// are rounded), <see cref="BoughtMedian"/> what one cost when bought (0 = never bought).
/// </summary>
public sealed record PriceEntry(string Item, int Samples, long Median, long Min, long Max, int Units, DateTime Last, int BoughtSamples, long BoughtMedian);

public static class VendorSummary
{
    public static VendorTotals Totals(IEnumerable<VendorRow> rows)
    {
        var list = rows.ToList();
        var sold = list.Where(r => r.Direction == VendorDirection.Sold).ToList();
        var bought = list.Where(r => r.Direction == VendorDirection.Bought).ToList();
        var best = sold.GroupBy(r => r.Item, StringComparer.OrdinalIgnoreCase).Select(g => (Item: g.First().Item, Total: g.Sum(r => r.TotalCopper)))
            .OrderByDescending(x => x.Total).FirstOrDefault();
        return new VendorTotals(sold.Sum(r => r.TotalCopper), sold.Sum(r => r.Qty), sold.Count, bought.Sum(r => r.TotalCopper), bought.Sum(r => r.Qty), bought.Count,
            best.Item ?? "", best.Total);
    }

    /// <summary>The items of one direction, most coin first.</summary>
    public static IReadOnlyList<VendorItem> ByItem(IEnumerable<VendorRow> rows, VendorDirection direction) =>
        rows.Where(r => r.Direction == direction).GroupBy(r => r.Item, StringComparer.OrdinalIgnoreCase)
            .Select(g => new VendorItem(g.First().Item, g.Count(), g.Sum(r => r.Qty), g.Sum(r => r.TotalCopper), Median(g.Where(r => r.TotalCopper > 0).Select(r => r.Each)), g.Max(r => r.Time)))
            .OrderByDescending(i => i.Total).ThenBy(i => i.Item, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Every item ever sold or bought, with its prices; rows with no coin read carry no price and are left out of the figures.</summary>
    public static IReadOnlyList<PriceEntry> PriceBook(IEnumerable<VendorRow> all)
    {
        var book = new List<PriceEntry>();
        foreach (var g in all.GroupBy(r => r.Item, StringComparer.OrdinalIgnoreCase))
        {
            var sold = g.Where(r => r.Direction == VendorDirection.Sold && r.TotalCopper > 0).ToList();
            var bought = g.Where(r => r.Direction == VendorDirection.Bought && r.TotalCopper > 0).ToList();
            if (sold.Count == 0 && bought.Count == 0) continue;
            book.Add(new PriceEntry(g.First().Item, sold.Count, Median(sold.Select(r => r.Each)), sold.Count == 0 ? 0 : sold.Min(r => r.Each),
                sold.Count == 0 ? 0 : sold.Max(r => r.Each), sold.Sum(r => r.Qty), sold.Count == 0 ? default : sold.Max(r => r.Time),
                bought.Count, Median(bought.Select(r => r.Each))));
        }
        return book.OrderByDescending(p => p.Median).ThenBy(p => p.Item, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The middle of the prices (the upper middle when there is an even number), as the item values take it; 0 for none.</summary>
    public static long Median(IEnumerable<long> prices)
    {
        var sorted = prices.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }
}
