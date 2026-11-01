namespace MnMCompanion.Market;

/// <summary>What kind of thing an item is, for grouping the list. Told from the site's wiki card and its stack sizes.</summary>
public enum MarketKind { Weapons, Armor, Jewelry, Bags, Materials, Other }

/// <summary>
/// One item on one server's market: the book's rows for it (<see cref="Now"/>, one per seller or buyer), every price it has had
/// (<see cref="Versions"/>: the rows now plus the ones a newer price replaced), and what the wiki says it is. Prices are per ONE
/// (a bulk material's stack price is <see cref="Stack"/> times that). A row counts as standing when its post was known up within
/// <see cref="ActiveFor"/>: the book never forgets a seller, but one silent for days no longer sets the price.
/// </summary>
public sealed class MarketItem
{
    public static readonly TimeSpan ActiveFor = TimeSpan.FromDays(3);

    public MarketItem(string key, string server, string name, ItemCard? card, int? stack, IReadOnlyList<MarketListing> now, IReadOnlyList<MarketListing> versions, DateTime readAt)
    {
        Key = key;
        Server = server;
        Name = name;
        Card = card;
        Stack = stack;
        Now = now;
        Versions = versions.OrderBy(v => v.Time).ToList();
        ReadAt = readAt;
        var active = now.Where(l => readAt - l.Seen <= ActiveFor).ToList();
        Asks = active.Where(l => l.Intent == MarketIntent.Sell).OrderBy(l => l.Copper).ToList();
        Bids = active.Where(l => l.Intent == MarketIntent.Buy).OrderByDescending(l => l.Copper).ToList();
        Kind = KindOf(card, stack);
    }

    public string Key { get; }
    public string Server { get; }
    public string Name { get; }
    public ItemCard? Card { get; }
    /// <summary>How many make a stack, for a bulk material; null for things priced one at a time.</summary>
    public int? Stack { get; }
    public MarketKind Kind { get; }
    /// <summary>Every row in the book for the item, standing or not.</summary>
    public IReadOnlyList<MarketListing> Now { get; }
    /// <summary>Every price set for the item, earliest first: the rows now and the ones they replaced.</summary>
    public IReadOnlyList<MarketListing> Versions { get; }
    public DateTime ReadAt { get; }
    /// <summary>Standing asks, cheapest first.</summary>
    public IReadOnlyList<MarketListing> Asks { get; }
    /// <summary>Standing bids, best first.</summary>
    public IReadOnlyList<MarketListing> Bids { get; }

    public long? Low => Asks.Count > 0 ? Asks[0].Copper : null;
    public long? High => Asks.Count > 0 ? Asks[^1].Copper : null;
    /// <summary>The middle standing ask.</summary>
    public long? Median => MarketMath.Median(Asks.Select(a => a.Copper).ToList());
    public long? BestBid => Bids.Count > 0 ? Bids[0].Copper : null;
    /// <summary>What the list shows for it: the middle ask, else the best bid.</summary>
    public long? Headline => Median ?? BestBid;
    public int Sellers => Asks.Select(a => a.Player).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int Buyers => Bids.Select(a => a.Player).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    /// <summary>How many times the site read posts for the item (its current rows).</summary>
    public int Sightings => Now.Sum(l => l.Count);
    public DateTime LastSeen => Now.Count > 0 ? Now.Max(l => l.Seen) : Versions.Count > 0 ? Versions.Max(v => v.Seen) : DateTime.MinValue;
    public DateTime FirstSeen => Versions.Count > 0 ? Versions[0].Time : LastSeen;
    /// <summary>Price changes after a player's first price (what moved, not what appeared).</summary>
    public int Changes => Versions.Count - Versions.Select(v => (v.Player.ToLowerInvariant(), v.Intent)).Distinct().Count();
    public bool Active => Asks.Count > 0 || Bids.Count > 0;

    /// <summary>The latest ask of all, standing or not: what the list shows when nothing stands.</summary>
    public MarketListing? LatestAsk => Now.Where(l => l.Intent == MarketIntent.Sell).OrderByDescending(l => l.Seen).FirstOrDefault();

    public static MarketKind KindOf(ItemCard? card, int? stack)
    {
        if (card is null) return stack is > 1 ? MarketKind.Materials : MarketKind.Other;
        if (card.IsWeapon) return MarketKind.Weapons;
        if (card.IsContainer) return MarketKind.Bags;
        if (card.IsJewelry) return MarketKind.Jewelry;
        if (card.IsArmor) return MarketKind.Armor;
        if (stack is > 1 || card.Tradeskills.Length > 0) return MarketKind.Materials;
        return MarketKind.Other;
    }
}

/// <summary>Every item on one server's market as read at one moment.</summary>
public sealed record MarketSnapshot(string Server, IReadOnlyList<MarketItem> Items, DateTime ReadAt)
{
    public static MarketSnapshot Empty { get; } = new("", [], DateTime.MinValue);

    private Dictionary<string, MarketItem>? _byName;

    /// <summary>The item by name, any case; null when the market has never listed it.</summary>
    public MarketItem? Named(string name) => (_byName ??= Items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase))
        .TryGetValue(name, out var item) ? item : null;

    /// <summary>The market of <paramref name="server"/> (the busiest server when null) as the book holds it.</summary>
    public static MarketSnapshot From(MarketBook book, string? server, DateTime readAt)
    {
        server ??= book.Servers.FirstOrDefault() ?? "";
        var rows = book.Rows.Values.Where(r => r.Server.Equals(server, StringComparison.OrdinalIgnoreCase)).ToList();
        var history = book.History.Where(h => h.Server.Equals(server, StringComparison.OrdinalIgnoreCase)).ToLookup(h => h.ItemKey);
        var items = new List<MarketItem>();
        foreach (var g in rows.GroupBy(r => r.ItemKey))
        {
            var now = g.ToList();
            var name = now.OrderByDescending(r => r.Seen).First().Item;
            var versions = now.Concat(history[g.Key]).ToList();
            items.Add(new MarketItem(g.Key, server, name, book.Card(name), book.StackOf(name), now, versions, readAt));
        }
        // Items whose every row has gone to the history still have a story to tell
        foreach (var g in history.Where(h => !rows.Any(r => r.ItemKey == h.Key)))
        {
            var versions = g.ToList();
            var name = versions.OrderByDescending(v => v.Seen).First().Item;
            items.Add(new MarketItem(g.Key, server, name, book.Card(name), book.StackOf(name), [], versions, readAt));
        }
        return new MarketSnapshot(server, items, readAt);
    }
}

/// <summary>One day of an item's prices: the asks and bids standing that day, one per person (their latest).</summary>
public sealed record PriceDay(DateOnly Day, int Sellers, long? Low, long? Median, long? High, int Buyers, long? BestBid);

/// <summary>A price someone set: when, what, what they asked before (if anything), and whether it still stands.</summary>
public sealed record PriceChange(DateTime Time, DateTime Seen, string Who, MarketIntent Intent, long Copper, int? Stack, int? Quantity, long? Previous, bool Current);

/// <summary>The sums over an item's versions: by day, every change, the trend.</summary>
public static class MarketMath
{
    /// <summary>A price stands from when it was set until it was last seen plus this (a snapshot's interval), unless the same person set a newer one sooner.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromHours(6);

    public static long? Median(List<long> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(c => c).ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    /// <summary>When each version stopped standing: the same person's next price, else last seen plus <see cref="Grace"/>.</summary>
    public static IReadOnlyList<(MarketListing Price, DateTime Until)> Standing(IReadOnlyList<MarketListing> versions)
    {
        var result = new List<(MarketListing, DateTime)>();
        foreach (var g in versions.GroupBy(v => (v.Player.ToLowerInvariant(), v.Intent)))
        {
            var list = g.OrderBy(v => v.Time).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var until = list[i].Seen + Grace;
                if (i + 1 < list.Count && list[i + 1].Time < until) until = list[i + 1].Time;
                result.Add((list[i], until));
            }
        }
        return result;
    }

    private static (List<long> Asks, List<long> Bids) At(IReadOnlyList<(MarketListing Price, DateTime Until)> standing, DateTime at)
    {
        var now = standing.Where(s => s.Price.Time <= at && at < s.Until).Select(s => s.Price).ToList();
        return (now.Where(p => p.Intent == MarketIntent.Sell).Select(p => p.Copper).OrderBy(c => c).ToList(),
            now.Where(p => p.Intent == MarketIntent.Buy).Select(p => p.Copper).ToList());
    }

    /// <summary>A row per day from the first price to <paramref name="today"/> that had any: what stood at any time that day.</summary>
    public static List<PriceDay> Days(MarketItem item, DateOnly today)
    {
        var days = new List<PriceDay>();
        var standing = Standing(item.Versions);
        if (standing.Count == 0) return days;
        for (var day = DateOnly.FromDateTime(item.Versions[0].Time); day <= today; day = day.AddDays(1))
        {
            DateTime start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), end = day.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var that = standing.Where(s => s.Price.Time <= end && s.Until >= start)
                .GroupBy(s => (s.Price.Player.ToLowerInvariant(), s.Price.Intent)).Select(g => g.OrderBy(s => s.Price.Time).Last().Price).ToList();
            var asks = that.Where(p => p.Intent == MarketIntent.Sell).Select(p => p.Copper).OrderBy(c => c).ToList();
            var bids = that.Where(p => p.Intent == MarketIntent.Buy).Select(p => p.Copper).ToList();
            if (asks.Count == 0 && bids.Count == 0) continue;
            days.Add(new PriceDay(day, asks.Count, asks.Count > 0 ? asks[0] : null, Median(asks), asks.Count > 0 ? asks[^1] : null, bids.Count, bids.Count > 0 ? bids.Max() : null));
        }
        return days;
    }

    /// <summary>Every price set, latest first, with the same person's previous price.</summary>
    public static List<PriceChange> Changes(MarketItem item)
    {
        var current = item.Now.Select(l => (l.Key, l.Time)).ToHashSet();
        var last = new Dictionary<(string, MarketIntent), long>();
        var changes = new List<PriceChange>();
        foreach (var v in item.Versions)
        {
            var who = (v.Player.ToLowerInvariant(), v.Intent);
            long? prev = last.TryGetValue(who, out var p) ? p : null;
            changes.Add(new PriceChange(v.Time, v.Seen, v.Player, v.Intent, v.Copper, v.Stack, v.Quantity, prev, current.Contains((v.Key, v.Time))));
            last[who] = v.Copper;
        }
        changes.Reverse();
        return changes;
    }

    /// <summary>
    /// How the middle ask moved: now against what it was <paramref name="over"/> ago (or when first seen, if later). Null when either
    /// end had no ask. The fraction is (now − then) / then.
    /// </summary>
    public static (long Then, long Now, double Fraction, DateTime Since)? Trend(MarketItem item, DateTime now, TimeSpan over)
    {
        if (item.Median is not { } current || item.Versions.Count == 0) return null;
        var standing = Standing(item.Versions);
        var since = now - over < item.FirstSeen ? item.FirstSeen : now - over;
        foreach (var t in item.Versions.Select(v => v.Time).Where(t => t >= since).Prepend(since).Distinct().OrderBy(t => t))
        {
            var (asks, _) = At(standing, t);
            if (asks.Count == 0) continue;
            var then = Median(asks)!.Value;
            return (then, current, then == 0 ? 0 : (current - then) / (double)then, t);
        }
        return null;
    }

    public static string Coin(long copper) => Loot.LootSummary.Coin(copper);

    /// <summary>The price as the list shows it: a stack's price for a bulk material ("5sp/stack"), else the price of one.</summary>
    public static string Shown(long perOne, int? stack) => stack is > 1 ? Coin(perOne * stack.Value) + "/stack" : Coin(perOne);
}
