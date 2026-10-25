namespace LootWindow.History;

/// <summary>A sale to a vendor or a purchase from one.</summary>
public enum VendorDirection : byte { Sold, Bought }

/// <summary>
/// One stack sold to or bought from a vendor (the ledger's act_24 and act_28). <see cref="TotalCopper"/> is for the whole stack;
/// the vendor's name isn't in the ledger. <see cref="Each"/> is what one fetched or cost, worked out the way the item values
/// work it out (the stack's coin divided by its count), so the two always agree.
/// </summary>
public sealed record VendorRow(DateTime Time, string Character, VendorDirection Direction, string Item, int Qty, long TotalCopper, string Zone = "")
{
    public long Each => TotalCopper / Math.Max(1, Qty);
}

/// <summary>Something gathered (act_27). The ledger names the item and the zone, not the node.</summary>
public sealed record HarvestRow(DateTime Time, string Character, string Item, int Qty, string Zone = "");

/// <summary>What happened to an item the player held: put on the ground, picked up off it, or deleted.</summary>
public enum ItemMoveKind : byte { Dropped, PickedUp, Destroyed }

/// <summary>An item dropped, picked up or destroyed (act_11, act_12, act_15).</summary>
public sealed record ItemMoveRow(DateTime Time, string Character, ItemMoveKind Kind, string Item, int Qty, string Zone = "");

/// <summary>One item line of a trade.</summary>
public sealed record TradeLine(string Name, int Qty);

/// <summary>
/// One side of a completed trade as the ledger writes it. <see cref="Gave"/> is what the player handed over (act_16);
/// otherwise it is what they got (act_20). <see cref="Partner"/> is an id the game uses ("ref_71358"), not a name.
/// </summary>
public sealed record TradeRow(DateTime Time, string Character, bool Gave, string Partner, long Coin, IReadOnlyList<TradeLine> Items, string Zone = "");

/// <summary>
/// A stretch of time to look at, as local clock times; null on either end means open. <see cref="To"/> is exclusive.
/// </summary>
public sealed record Period(string Label, DateTime? From, DateTime? To)
{
    public bool Contains(DateTime t) => (From is null || t >= From) && (To is null || t < To);

    public static Period Of(DateOnly day, DateOnly today)
    {
        var from = day.ToDateTime(TimeOnly.MinValue);
        var label = day == today ? "Today" : day == today.AddDays(-1) ? "Yesterday" : day.ToString("ddd, MMM d");
        return new Period(label, from, from.AddDays(1));
    }

    /// <summary>The quick choices: today, yesterday, the last 7 and 30 days (including today), and everything.</summary>
    public static IReadOnlyList<Period> Presets(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var start = today.ToDateTime(TimeOnly.MinValue);
        return
        [
            Of(today, today),
            Of(today.AddDays(-1), today),
            new Period("7 days", start.AddDays(-6), start.AddDays(1)),
            new Period("30 days", start.AddDays(-29), start.AddDays(1)),
            new Period("All time", null, null),
        ];
    }
}
