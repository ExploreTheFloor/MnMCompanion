using MnMCompanion.Loot;

namespace MnMCompanion.History;

/// <summary>
/// One trade: what the player gave and what they got, put together from the ledger's two rows for it (it writes one for each
/// side, in the same second, naming the same partner).
/// </summary>
public sealed record TradeEntry(DateTime Time, string Character, string Partner, long GaveCoin, IReadOnlyList<TradeLine> GaveItems,
    long ReceivedCoin, IReadOnlyList<TradeLine> ReceivedItems)
{
    public int GaveCount => GaveItems.Sum(i => i.Qty);
    public int ReceivedCount => ReceivedItems.Sum(i => i.Qty);
}

public static class TradeSummary
{
    /// <summary>Rows with the same partner this close together are the two sides of one trade.</summary>
    public static readonly TimeSpan SameTrade = TimeSpan.FromSeconds(5);

    /// <summary>The rows of each character and partner joined into trades, newest first.</summary>
    public static IReadOnlyList<TradeEntry> Pair(IEnumerable<TradeRow> rows)
    {
        var entries = new List<TradeEntry>();
        foreach (var group in rows.GroupBy(r => (r.Character.ToLowerInvariant(), r.Partner)))
        {
            var ordered = group.OrderBy(r => r.Time).ToList();
            var current = new List<TradeRow>();
            void Flush()
            {
                if (current.Count == 0) return;
                var gave = current.Where(r => r.Gave).ToList();
                var got = current.Where(r => !r.Gave).ToList();
                entries.Add(new TradeEntry(current[0].Time, current[0].Character, current[0].Partner,
                    gave.Sum(r => r.Coin), Merge(gave.SelectMany(r => r.Items)), got.Sum(r => r.Coin), Merge(got.SelectMany(r => r.Items))));
                current.Clear();
            }
            foreach (var r in ordered)
            {
                if (current.Count > 0 && r.Time - current[^1].Time > SameTrade) Flush();
                current.Add(r);
            }
            Flush();
        }
        return entries.OrderByDescending(e => e.Time).ToList();
    }

    private static IReadOnlyList<TradeLine> Merge(IEnumerable<TradeLine> lines) =>
        lines.GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Select(g => new TradeLine(g.First().Name, g.Sum(l => l.Qty)))
            .OrderByDescending(l => l.Qty).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>"ref_71358" → "Trader #71358": the ledger gives the other player as an id, never a name.</summary>
    public static string Label(string partner) => partner.StartsWith("ref_", StringComparison.OrdinalIgnoreCase) ? "Trader #" + partner[4..] : partner.Length == 0 ? "Unknown trader" : partner;

    /// <summary>"7sp + Rawhide Backpack ×2", or "nothing".</summary>
    public static string Describe(long coin, IReadOnlyList<TradeLine> items)
    {
        var parts = new List<string>();
        if (coin > 0) parts.Add(LootSummary.Coin(coin));
        parts.AddRange(items.Select(i => i.Qty > 1 ? $"{i.Name} ×{i.Qty}" : i.Name));
        return parts.Count == 0 ? "nothing" : string.Join(" + ", parts);
    }
}
