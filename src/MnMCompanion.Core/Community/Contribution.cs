using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion.Community;

/// <summary>An item off a monster: in how many of the corpses looted it was taken, and how many units.</summary>
public sealed record ContributionItem(string Item, int Corpses, int Units);

/// <summary>One kind of monster in one zone: corpses looted, how many had a purse and its coin, and what came off them.</summary>
public sealed record ContributionDrop(string Mob, string Zone, int Corpses, int Purses, long Copper, IReadOnlyList<ContributionItem> Items);

/// <summary>
/// What vendors paid (<see cref="Sold"/>) and charged (<see cref="Bought"/>) for one item in one zone, as a tally: the price for one,
/// in copper, mapped to how many stacks went at that price. Vendors' names aren't in the ledger, so the zone is the nearest we get.
/// </summary>
public sealed record ContributionPrice(string Item, string Zone, IReadOnlyDictionary<string, int> Sold, IReadOnlyDictionary<string, int> Bought);

/// <summary>What was gathered of one item in one zone.</summary>
public sealed record ContributionHarvest(string Item, string Zone, int Harvests, int Units);

/// <summary>An item's name with the 24-character id the game gives it.</summary>
public sealed record ContributionName(string Name, string Id);

/// <summary>What one NPC said to the player, as distinct lines with the player's own name replaced by {player}: no times, no characters.</summary>
public sealed record ContributionNpc(string Name, IReadOnlyList<string> Lines);

/// <summary>One item on one side of a trade, and how many.</summary>
public sealed record ContributionTradeItem(string Item, int Qty);

/// <summary>What one side of a trade handed over: coin (copper) and items.</summary>
public sealed record ContributionTradeSide(long Coin, IReadOnlyList<ContributionTradeItem> Items);

/// <summary>
/// One trade with another player (schema 3): the day (UTC) and zone, what the player <see cref="Gave"/> and what they <see cref="Got"/>. It never says who
/// the other player was: the ledger's number for them is left out, and so is the time of day.
/// </summary>
public sealed record ContributionTrade(string Day, string Zone, ContributionTradeSide Gave, ContributionTradeSide Got);

/// <summary>
/// What the player threw away of one item in one zone (schema 3), in units: <see cref="Dropped"/> on the ground, <see cref="Destroyed"/>, and of the dropped ones how many
/// they picked up again soon after (<see cref="TakenBack"/>, never more than were dropped). Dropping and destroying are always the player's own choice.
/// </summary>
public sealed record ContributionDiscard(string Item, string Zone, int Dropped, int Destroyed, int TakenBack);

/// <summary>Headline counts of a contribution, for the preview.</summary>
public sealed record ContributionTotals(int Monsters, int Corpses, int Sales, int Purchases, int Harvests, int Items);

/// <summary>
/// One row of the running counters (schema 2): the same loot, sales and gathering as the lists, counted instead of listed.
/// <see cref="Week"/> is the Monday (UTC, yyyy-MM-dd) that starts the week the row counts, or null for the "before" row that holds everything older.
/// <see cref="Corpses"/> is the kill count: the ledger records no kills, so it is the corpses the player looted. Coin is copper.
/// </summary>
public sealed record CountRow(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Week,
    long Corpses, long Purses, long Copper, long Units, long Harvests, long HarvestUnits,
    long Sold, long SoldCopper, long Bought, long BoughtCopper, long Trades)
{
    [JsonIgnore]
    public bool IsEmpty => Corpses == 0 && Purses == 0 && Copper == 0 && Units == 0 && Harvests == 0 && HarvestUnits == 0
        && Sold == 0 && SoldCopper == 0 && Bought == 0 && BoughtCopper == 0 && Trades == 0;
}

/// <summary>
/// The running counters: at most <see cref="ContributionBuilder.MaxWeeks"/> weekly rows and one row for everything before them, so the
/// file stops growing after a year and never holds one row per event.
/// </summary>
public sealed record ContributionCounts(CountRow Before, IReadOnlyList<CountRow> Weeks);

/// <summary>
/// What a player shares with the community data: aggregates of their OWN loot, sales and gathering, and nothing else. It holds no
/// character or player name, no individual rows, no trade partners and no times beyond the day it was made. <see cref="Contributor"/>
/// is a random id made on this PC (not derived from anything about the player), so sending the file again replaces what they sent
/// before instead of adding to it. See SCHEMA.md in the data repository.
/// </summary>
public sealed record Contribution(
    int Schema, string Contributor, string App, string Generated, string License, ContributionTotals Totals,
    IReadOnlyList<ContributionDrop> Drops, IReadOnlyList<ContributionPrice> Prices, IReadOnlyList<ContributionHarvest> Harvest, IReadOnlyList<ContributionName> Items,
    IReadOnlyList<ContributionNpc> Npcs, ContributionCounts Counts, IReadOnlyList<ContributionTrade> Trades, IReadOnlyList<ContributionDiscard> Discards)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // What is sent: no indentation (it was over 40% of every file) and no needless \u escapes (names with an apostrophe stay as they are)
    private static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Indented = new(Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The whole contribution, indented, dialogue and trades included (a picture of everything in it; what is sent is <see cref="ToCoreJson"/>, <see cref="ToNpcPartJson"/> and <see cref="ToTradesPartJson"/>).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// The file that is sent: everything but the NPC dialogue and the trades, which travel as documents of their own (<see cref="ToNpcPartJson"/>,
    /// <see cref="ToTradesPartJson"/>) so no single upload is large. Compact by default; <paramref name="indented"/> is the same file laid out to be read (the saved copy).
    /// </summary>
    public string ToCoreJson(bool indented = false)
    {
        var node = JsonSerializer.SerializeToNode(this, Options)!.AsObject();
        node.Remove("npcs");
        node.Remove("trades");
        return node.ToJsonString(indented ? Indented : Compact);
    }

    /// <summary>The NPC dialogue as a "part" document: the same id, app, day and licence, and the NPC lines, and nothing else.</summary>
    public string ToNpcPartJson(bool indented = false)
    {
        var part = new JsonObject
        {
            ["schema"] = Schema, ["part"] = NpcPart, ["contributor"] = Contributor, ["app"] = App, ["generated"] = Generated, ["license"] = License,
            ["npcs"] = JsonSerializer.SerializeToNode(Npcs, Options),
        };
        return part.ToJsonString(indented ? Indented : Compact);
    }

    /// <summary>The trades as a "part" document: the same id, app, day and licence, and the trades, and nothing else.</summary>
    public string ToTradesPartJson(bool indented = false)
    {
        var part = new JsonObject
        {
            ["schema"] = Schema, ["part"] = TradesPart, ["contributor"] = Contributor, ["app"] = App, ["generated"] = Generated, ["license"] = License,
            ["trades"] = JsonSerializer.SerializeToNode(Trades ?? [], Options),
        };
        return part.ToJsonString(indented ? Indented : Compact);
    }

    /// <summary>The names of the two parts: the NPC dialogue and the trades.</summary>
    public const string NpcPart = "npcs", TradesPart = "trades";

    public static Contribution? FromJson(string json) => JsonSerializer.Deserialize<Contribution>(json, Options);
}

public static class ContributionBuilder
{
    /// <summary>
    /// The version of the file layout; the community service refuses layouts it doesn't know. 2 added the running counters (<see cref="CountRow"/>); 3 added
    /// the trades with other players (<see cref="ContributionTrade"/>). A player's Yes is tied to the layout they saw (<see cref="CommunityPolicy.Consented"/>),
    /// so raising this asks everyone again.
    /// </summary>
    public const int Schema = 3;

    /// <summary>The weekly rows of the counters: the current week and the 52 before it. Older weeks are folded into the "before" row.</summary>
    public const int MaxWeeks = 53;

    /// <summary>The licence a contributor gives their file under, stated in the file itself.</summary>
    public const string License = "CC-BY-NC-SA-4.0";

    private const int MaxName = 80;

    /// <summary>The service refuses more than this: a file holds the newest <see cref="MaxTrades"/> trades, a side at most <see cref="MaxTradeItems"/> kinds of item and <see cref="MaxTradeQty"/> of one.</summary>
    public const int MaxTrades = 3000, MaxTradeItems = 40, MaxTradeQty = 9999, MaxDiscards = 5000;

    /// <summary>The most coin (copper) a side of a trade may hold in the file.</summary>
    public const long MaxTradeCoin = 1_000_000_000_000;

    private static readonly DateOnly EarliestTrade = new(2025, 1, 1);

    /// <summary>The community service refuses more than this: lines longer than <see cref="MaxLine"/> are left out, an NPC keeps at most <see cref="MaxLinesPerNpc"/>, a file at most <see cref="MaxNpcs"/> NPCs.</summary>
    public const int MaxLine = 2000, MaxLinesPerNpc = 500, MaxNpcs = 5000;

    /// <summary>
    /// Builds a contribution from one player's own rows: the loot they took (items and purses, with the corpse rule of
    /// <see cref="LootSummary.Corpses"/>), what they sold and bought, what they gathered, and the item names with their ids.
    /// Only the player's OWN rows belong here (their Character files), because the same loot is written into every party member's
    /// social file and would otherwise be counted once per member.
    /// </summary>
    public static Contribution Build(IEnumerable<LootEvent> ownLoot, IEnumerable<VendorRow> vendor, IEnumerable<HarvestRow> harvest,
        IEnumerable<ContributionName> names, string contributor, string app, DateOnly generated, IEnumerable<ContributionNpc>? npcs = null,
        IEnumerable<TradeRow>? trades = null, IEnumerable<ItemMoveRow>? itemMoves = null)
    {
        var tradeRows = (trades ?? []).ToList();
        var loot = ownLoot as IReadOnlyList<LootEvent> ?? ownLoot.ToList();
        var drops = DropRates.ByMob(loot)
            .Where(m => Ok(m.Mob))
            .Select(m => new ContributionDrop(Clean(m.Mob), Clean(m.Zone), m.Corpses, m.WithPurse, m.Copper,
                m.Drops.Where(d => Ok(d.Item)).Select(d => new ContributionItem(Clean(d.Item), d.Corpses, d.Units)).ToList()))
            .OrderByDescending(d => d.Corpses).ThenBy(d => d.Mob, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Zone, StringComparer.OrdinalIgnoreCase).ToList();

        var rows = vendor.Where(v => v.TotalCopper > 0 && Ok(v.Item)).ToList();
        var prices = rows.GroupBy(v => (Clean(v.Item).ToLowerInvariant(), Clean(v.Zone).ToLowerInvariant()))
            .Select(g => new ContributionPrice(Clean(g.First().Item), Clean(g.First().Zone), Tally(g, VendorDirection.Sold), Tally(g, VendorDirection.Bought)))
            .OrderBy(p => p.Item, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Zone, StringComparer.OrdinalIgnoreCase).ToList();

        var gathered = harvest.Where(h => Ok(h.Item)).GroupBy(h => (Clean(h.Item).ToLowerInvariant(), Clean(h.Zone).ToLowerInvariant()))
            .Select(g => new ContributionHarvest(Clean(g.First().Item), Clean(g.First().Zone), g.Count(), g.Sum(h => h.Qty)))
            .OrderByDescending(h => h.Units).ThenBy(h => h.Item, StringComparer.OrdinalIgnoreCase).ToList();

        var catalogue = names.Where(n => Ok(n.Name) && IsId(n.Id)).Select(n => new ContributionName(Clean(n.Name), n.Id.ToLowerInvariant()))
            .DistinctBy(n => (n.Name.ToLowerInvariant(), n.Id)).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();

        var totals = new ContributionTotals(drops.Count, drops.Sum(d => d.Corpses), rows.Count(v => v.Direction == VendorDirection.Sold),
            rows.Count(v => v.Direction == VendorDirection.Bought), gathered.Sum(h => h.Harvests), catalogue.Count);
        var dialogue = (npcs ?? []).Where(n => Ok(n.Name)).GroupBy(n => Clean(n.Name).ToLowerInvariant())
            .Select(g => new ContributionNpc(Clean(g.First().Name),
                g.SelectMany(n => n.Lines).Select(l => l.Trim()).Where(l => l.Length > 0 && l.Length <= MaxLine && !l.Any(char.IsControl)).Distinct(StringComparer.Ordinal)
                    .Take(MaxLinesPerNpc).ToList()))   // in the order they were first heard, so a quest's lines stay in sequence
            .Where(n => n.Lines.Count > 0).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Take(MaxNpcs).ToList();

        // The counters count exactly what the lists above list (the same corpses, stacks and harvests, the same name rule), so the service can check they add up
        var looted = LootSummary.Corpses(loot).Where(c => c.Mob && Ok(c.Source)).ToList();
        var gatheredRows = harvest.Where(h => Ok(h.Item)).ToList();
        var counts = CountsOf(looted, rows, gatheredRows, tradeRows, generated);
        var tradeList = TradesOf(tradeRows, generated);
        var discards = DiscardsOf((itemMoves ?? []).ToList());

        return new Contribution(Schema, contributor, app, generated.ToString("yyyy-MM-dd"), License, totals, drops, prices, gathered, catalogue, dialogue, counts, tradeList, discards);
    }

    // The slots of one row of counters, in the order of CountRow
    private const int KCorpses = 0, KPurses = 1, KCopper = 2, KUnits = 3, KHarvests = 4, KHarvestUnits = 5, KSold = 6, KSoldCopper = 7, KBought = 8, KBoughtCopper = 9, KTrades = 10, Slots = 11;

    /// <summary>The Monday that starts the week a day is in (weeks run Monday to Sunday).</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>
    /// The running counters: every looted corpse, vendor stack, harvest and trade counted into the UTC week it happened in, the last
    /// <see cref="MaxWeeks"/> weeks as rows and everything older folded into one "before" row. A week with nothing in it has no row.
    /// A trade is both sides of it (what was given and what came back) counted once.
    /// </summary>
    private static ContributionCounts CountsOf(IEnumerable<LootedCorpse> corpses, IEnumerable<VendorRow> vendorRows, IEnumerable<HarvestRow> gathered, IEnumerable<TradeRow> trades, DateOnly generated)
    {
        var thisWeek = WeekStart(generated);
        var oldest = thisWeek.AddDays(-7 * (MaxWeeks - 1));
        var weeks = new SortedDictionary<DateOnly, long[]>();
        var before = new long[Slots];

        long[] RowFor(DateTime at)
        {
            var week = WeekStart(DateOnly.FromDateTime(at.Kind == DateTimeKind.Utc ? at : at.ToUniversalTime()));
            if (week > thisWeek) week = thisWeek;   // a clock a little ahead of the file's date
            if (week < oldest) return before;
            if (!weeks.TryGetValue(week, out var row)) weeks[week] = row = new long[Slots];
            return row;
        }

        foreach (var c in corpses)
        {
            var row = RowFor(c.Start);
            row[KCorpses]++;
            if (c.Copper > 0) row[KPurses]++;
            row[KCopper] += c.Copper;
            foreach (var t in c.Taken ?? []) if (Ok(t.Item)) row[KUnits] += t.Count;
        }
        foreach (var v in vendorRows)
        {
            var row = RowFor(v.Time);
            if (v.Direction == VendorDirection.Sold) { row[KSold]++; row[KSoldCopper] += v.TotalCopper; }
            else { row[KBought]++; row[KBoughtCopper] += v.TotalCopper; }
        }
        foreach (var h in gathered)
        {
            var row = RowFor(h.Time);
            row[KHarvests]++;
            row[KHarvestUnits] += h.Qty;
        }
        // A trade is written once for what the player gave and once for what they got, a moment apart: one trade per partner and minute
        foreach (var once in trades.GroupBy(x => (x.Partner, x.Time.Ticks / TimeSpan.TicksPerMinute)).Select(g => g.First()))
            RowFor(once.Time)[KTrades]++;

        return new ContributionCounts(Row(null, before), weeks.Select(w => Row(w.Key.ToString("yyyy-MM-dd"), w.Value)).ToList());

        static CountRow Row(string? week, long[] m) => new(week, m[KCorpses], m[KPurses], m[KCopper], m[KUnits], m[KHarvests], m[KHarvestUnits], m[KSold], m[KSoldCopper], m[KBought], m[KBoughtCopper], m[KTrades]);
    }

    /// <summary>
    /// The trades with other players, newest first, at most <see cref="MaxTrades"/>: each is both sides of it put together (see <see cref="TradeSummary.Pair"/>), on the day it
    /// happened (UTC) and in the zone, with the coin and the items each side handed over. The other player is not in it, and neither is the time of day. A trade with nothing on
    /// either side, or from before 2025, is left out.
    /// </summary>
    private static IReadOnlyList<ContributionTrade> TradesOf(IReadOnlyList<TradeRow> rows, DateOnly generated)
    {
        var list = new List<ContributionTrade>();
        foreach (var entry in TradeSummary.Pair(rows))
        {
            var gave = Side(entry.GaveCoin, entry.GaveItems);
            var got = Side(entry.ReceivedCoin, entry.ReceivedItems);
            if (gave.Coin == 0 && gave.Items.Count == 0 && got.Coin == 0 && got.Items.Count == 0) continue;
            var day = DateOnly.FromDateTime(entry.Time.Kind == DateTimeKind.Utc ? entry.Time : entry.Time.ToUniversalTime());
            if (day > generated) day = generated;   // a clock a little ahead of the file's date
            if (day < EarliestTrade) continue;
            var zone = Clean(entry.Zone);
            if (zone.Length == 0 || zone.Any(char.IsControl)) zone = "unknown";
            list.Add(new ContributionTrade(day.ToString("yyyy-MM-dd"), zone, gave, got));
            if (list.Count == MaxTrades) break;
        }
        return list;

        static ContributionTradeSide Side(long coin, IReadOnlyList<TradeLine> lines) => new(
            Math.Clamp(coin, 0, MaxTradeCoin),
            lines.Where(l => l.Qty > 0 && Ok(l.Name) && !Clean(l.Name).Any(char.IsControl))
                .GroupBy(l => Clean(l.Name).ToLowerInvariant())
                .Select(g => new ContributionTradeItem(Clean(g.First().Name), Math.Min(MaxTradeQty, g.Sum(l => l.Qty))))
                .OrderByDescending(i => i.Qty).ThenBy(i => i.Item, StringComparer.OrdinalIgnoreCase).Take(MaxTradeItems).ToList());
    }

    /// <summary>
    /// What the player dropped and destroyed, per item and zone (schema 3): units dropped, units destroyed, and of the dropped units how many were picked up again within ten
    /// minutes. The most thrown away first, at most <see cref="MaxDiscards"/>. An item that was only picked up is not here, and neither is anything without a name.
    /// </summary>
    private static IReadOnlyList<ContributionDiscard> DiscardsOf(IReadOnlyList<ItemMoveRow> moves)
    {
        var taken = ItemChangeSummary.TakenBackByItemAndZone(moves);
        return moves.Where(m => m.Kind is ItemMoveKind.Dropped or ItemMoveKind.Destroyed && m.Qty > 0 && Ok(m.Item) && !Clean(m.Item).Any(char.IsControl))
            .GroupBy(m => (Clean(m.Item).ToLowerInvariant(), ZoneOf(m.Zone).ToLowerInvariant()))
            .Select(g =>
            {
                int dropped = g.Where(m => m.Kind == ItemMoveKind.Dropped).Sum(m => m.Qty);
                int destroyed = g.Where(m => m.Kind == ItemMoveKind.Destroyed).Sum(m => m.Qty);
                // taken back is counted by the raw zone and item the rows carry, so look it up the same way
                int back = g.Select(m => (m.Item.ToLowerInvariant(), m.Zone.ToLowerInvariant())).Distinct().Sum(k => taken.GetValueOrDefault(k));
                return new ContributionDiscard(Clean(g.First().Item), ZoneOf(g.First().Zone), dropped, destroyed, Math.Min(back, dropped));
            })
            .OrderByDescending(d => d.Dropped + d.Destroyed).ThenBy(d => d.Item, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Zone, StringComparer.OrdinalIgnoreCase)
            .Take(MaxDiscards).ToList();
    }

    private static string ZoneOf(string zone)
    {
        var clean = Clean(zone);
        return clean.Length == 0 || clean.Any(char.IsControl) ? "unknown" : clean;
    }

    /// <summary>The counters added up over every row: the player's lifetime figures as the file states them.</summary>
    public static CountRow Lifetime(ContributionCounts counts)
    {
        var rows = counts.Weeks.Prepend(counts.Before).ToList();
        return new CountRow(null, rows.Sum(r => r.Corpses), rows.Sum(r => r.Purses), rows.Sum(r => r.Copper), rows.Sum(r => r.Units), rows.Sum(r => r.Harvests),
            rows.Sum(r => r.HarvestUnits), rows.Sum(r => r.Sold), rows.Sum(r => r.SoldCopper), rows.Sum(r => r.Bought), rows.Sum(r => r.BoughtCopper), rows.Sum(r => r.Trades));
    }

    private static Dictionary<string, int> Tally(IEnumerable<VendorRow> rows, VendorDirection direction) =>
        rows.Where(r => r.Direction == direction).GroupBy(r => r.Each).OrderBy(g => g.Key)
            .ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Count());

    private static bool Ok(string text) => text.Trim().Length > 0;

    private static string Clean(string text)
    {
        text = text.Trim();
        return text.Length <= MaxName ? text : text[..MaxName];
    }

    private static bool IsId(string id) => id.Length == 24 && id.All(char.IsAsciiHexDigit);

    /// <summary>The preview a player reads before saving: what is in the file, in a few lines.</summary>
    public static IReadOnlyList<string> Describe(Contribution c)
    {
        var t = c.Totals;
        var n = Lifetime(c.Counts);
        return
        [
            $"{t.Monsters:N0} kinds of monster, from {t.Corpses:N0} looted corpses: what came off each, how often, and the coin in their purses",
            $"Running totals, counted by week (only which week, never a day or a time): {n.Corpses:N0} corpses looted, {n.Units:N0} items and {LootSummary.Coin(n.Copper)} taken from them, " +
                $"{n.Harvests:N0} harvests, {n.Sold:N0} stacks sold for {LootSummary.Coin(n.SoldCopper)}, {n.Bought:N0} bought for {LootSummary.Coin(n.BoughtCopper)}, {n.Trades:N0} trades with players " +
                $"({c.Counts.Weeks.Count:N0} weekly {(c.Counts.Weeks.Count == 1 ? "row" : "rows")})",
            $"{c.Prices.Count:N0} item prices by zone, from {t.Sales:N0} sales and {t.Purchases:N0} purchases",
            $"{c.Harvest.Count:N0} things gathered by zone, from {t.Harvests:N0} harvests",
            $"{t.Items:N0} item names with their game ids",
            $"{c.Npcs.Count:N0} NPCs with {c.Npcs.Sum(n => n.Lines.Count):N0} lines of what they said to you (your character's name replaced, no times)",
            $"{(c.Trades ?? []).Count:N0} trades with other players: what each side handed over, the items and the coin, with the day and the zone. Never who the other player was",
            $"{(c.Discards ?? []).Count:N0} items you dropped or destroyed, by zone: how many of each, and how many you took back",
        ];
    }
}
