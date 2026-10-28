using System.Globalization;
using System.Text;

namespace MnMCompanion.Loot;

/// <summary>An item and how many of it.</summary>
public sealed record ItemCount(string Item, int Count);

/// <summary>
/// What one person took: <see cref="Items"/> items from how many mobs, and the coin counted for them, <see cref="Copper"/>: the purses they looted that
/// were not divided (whole), plus, for a ledger's owner, the shares of divided purses that reached them (<see cref="Received"/>). A divided purse is not
/// counted for whoever looted it, since they kept only part and the ledger never says how much. <see cref="Purses"/> is the whole of every purse they
/// looted and <see cref="SplitPurses"/> how many of those were divided. <see cref="EstimatedShares"/> counts the shares in <see cref="Received"/> the
/// ledger doesn't show (a ledger's owner's own purses, divided by their group) and so were estimated.
/// </summary>
public sealed record PlayerLoot(string Who, bool You, int Mobs, int Items, long Copper, IReadOnlyList<ItemCount> Taken,
    long Purses = 0, long Received = 0, int SplitPurses = 0, int EstimatedShares = 0);

/// <summary>
/// One corpse one looter took from: the coin and the items, which the ledger gives in separate rows. <see cref="Source"/> is as
/// the ledger names it ("a Dustrend legionary", "your corpse"); <see cref="Mob"/> is false for a player's corpse.
/// </summary>
public sealed record LootedCorpse(string Who, bool You, string Source, bool Mob, DateTime Start, int Items, long Copper, long Split,
    IReadOnlyList<ItemCount>? Taken = null, string Zone = "", bool Shared = false);

/// <summary>One item and who took how many of it.</summary>
public sealed record ItemTakers(string Item, int Count, IReadOnlyList<(string Who, int Count)> By);

/// <summary>A day at a glance. <see cref="Share"/> is the coin the ledger's owner received as their split of the purses.</summary>
public sealed record LootTotals(int Items, int Players, int Mobs, long Copper, long Share);

/// <summary>Views of a day's <see cref="LootEvent"/>s: per looter, per item, totals, and a text form.</summary>
public static class LootSummary
{
    /// <summary>
    /// A looter's coin and items from one corpse come as separate rows (the purse first, then each item a second or so apart),
    /// and the rows name the corpse only by what it was ("a Dustrend legionary"), not which one. So rows by one looter from a
    /// corpse of one name belong to the same corpse while each follows the last within this time, and a second purse starts
    /// another (a corpse has one).
    /// </summary>
    public static readonly TimeSpan SameCorpse = TimeSpan.FromSeconds(6);

    /// <summary>The corpses each looter took from, oldest first.</summary>
    public static IReadOnlyList<LootedCorpse> Corpses(IEnumerable<LootEvent> events)
    {
        var visits = new List<Visit>();
        var open = new Dictionary<(string Who, string Source), Visit>();
        foreach (var e in events.OrderBy(e => e.Time))
        {
            var key = (e.Who.ToLowerInvariant(), e.Source.ToLowerInvariant());
            open.TryGetValue(key, out var v);
            if (v is null || e.Time - v.Last > SameCorpse || e.Kind == LootKind.Coin && v.HasPurse)
            {
                v = new Visit { Who = e.Who, You = e.You, Source = e.Source, Start = e.Time, Zone = e.Zone };
                visits.Add(v);
                open[key] = v;
            }
            v.Last = e.Time;
            v.You |= e.You;
            if (e.Kind == LootKind.Item) { v.Items += e.Count; v.Taken[e.Item] = v.Taken.GetValueOrDefault(e.Item) + e.Count; }
            else { v.HasPurse = true; v.Copper += e.Copper; v.Split += e.Split; v.Shared |= e.Shared; }
        }
        return visits.Select(v => new LootedCorpse(v.Who, v.You, v.Source, IsMob(v.Source), v.Start, v.Items, v.Copper, v.Split,
            v.Taken.Select(t => new ItemCount(t.Key, t.Value)).ToList(), v.Zone, v.Shared)).ToList();
    }

    /// <summary>Your own corpse and other players' ("Brom's corpse") aren't mobs.</summary>
    public static bool IsMob(string source) =>
        !source.Equals("your corpse", StringComparison.OrdinalIgnoreCase) && !source.EndsWith("'s corpse", StringComparison.OrdinalIgnoreCase);

    private sealed class Visit
    {
        public string Who = "", Source = "", Zone = "";
        public bool You, HasPurse, Shared;
        public DateTime Start, Last;
        public int Items;
        public long Copper, Split;
        public readonly Dictionary<string, int> Taken = new(StringComparer.OrdinalIgnoreCase);
    }

    public static LootTotals Totals(IEnumerable<LootEvent> events)
    {
        events = events.ToList();
        int mobs = Corpses(events).Count(c => c.Mob);
        int items = 0;
        long copper = 0, share = 0;
        var who = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events)
        {
            if (e.Kind == LootKind.Item) items += e.Count;
            else { copper += e.Copper; share += e.Split; }
            who.Add(e.Who);
        }
        return new LootTotals(items, who.Count, mobs, copper, share);
    }

    /// <summary>Per looter, most mobs looted first (then most items, then most coin).</summary>
    public static IReadOnlyList<PlayerLoot> ByPlayer(IEnumerable<LootEvent> events)
    {
        events = events.ToList();
        var map = new Dictionary<string, Looter>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in Corpses(events).Where(c => c.Mob))
        {
            if (!map.TryGetValue(c.Who, out var p)) map[c.Who] = p = new Looter();
            p.Mobs++;
        }
        foreach (var e in events)
        {
            if (!map.TryGetValue(e.Who, out var p)) map[e.Who] = p = new Looter();
            p.You |= e.You;
            if (e.Kind == LootKind.Item)
            {
                p.Items += e.Count;
                p.Taken[e.Item] = p.Taken.GetValueOrDefault(e.Item) + e.Count;
            }
            else
            {
                // A purse the looter's autosplit divided isn't theirs whole: it counts for the group, not for them
                p.Purses += e.Copper;
                if (e.Shared) p.SplitPurses++;
                else p.Kept += e.Copper;
            }
            // The only share of a divided purse a ledger shows is its owner's own: that is what they were handed
            foreach (var share in e.Shares ?? Array.Empty<ShareTo>())
            {
                if (!map.TryGetValue(share.Owner, out var owner)) map[share.Owner] = owner = new Looter();
                owner.You = true;
                owner.Received += share.Copper;
                if (share.Estimated) owner.EstimatedShares++;
            }
        }
        return map.Select(kv => new PlayerLoot(kv.Key, kv.Value.You, kv.Value.Mobs, kv.Value.Items, kv.Value.Kept + kv.Value.Received,
                kv.Value.Taken.OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.OrdinalIgnoreCase).Select(t => new ItemCount(t.Key, t.Value)).ToList(),
                kv.Value.Purses, kv.Value.Received, kv.Value.SplitPurses, kv.Value.EstimatedShares))
            .OrderByDescending(p => p.Mobs).ThenByDescending(p => p.Items).ThenByDescending(p => p.Copper).ThenBy(p => p.Who, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed class Looter
    {
        public bool You;
        public int Mobs, Items, SplitPurses, EstimatedShares;
        public long Kept, Purses, Received;
        public readonly Dictionary<string, int> Taken = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Per item, most first, with who took them.</summary>
    public static IReadOnlyList<ItemTakers> ByItem(IEnumerable<LootEvent> events)
    {
        var map = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events.Where(e => e.Kind == LootKind.Item))
        {
            if (!map.TryGetValue(e.Item, out var by)) map[e.Item] = by = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            by[e.Who] = by.GetValueOrDefault(e.Who) + e.Count;
        }
        return map.Select(kv => new ItemTakers(kv.Key, kv.Value.Values.Sum(),
                kv.Value.OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.OrdinalIgnoreCase).Select(b => (b.Key, b.Value)).ToList()))
            .OrderByDescending(i => i.Count).ThenBy(i => i.Item, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>"~3sp 41cp+": a value with "~" when some of it is estimated and "+" when more is unpriced (not counted).</summary>
    public static string Worth(long copper, bool approximate, bool more) => (approximate ? "~" : "") + Coin(copper) + (more ? "+" : "");

    /// <summary>
    /// "12 items"; "12+ items" when the ledger left out the items of some of their corpses (<see cref="LootMember.Unrecorded"/>: a monster with an apostrophe in
    /// its name); "items not recorded" when none of their items were recorded, since "0 items" would say they took none.
    /// </summary>
    public static string ItemsOf(LootMember m) =>
        m.Unrecorded == 0 ? Plural(m.Items, "item") : m.Items == 0 ? "items not recorded" : $"{m.Items:N0}+ items";

    /// <summary>What a vendor would pay for their items (<see cref="Worth"/>); "—" when none of their items were recorded; the + also says some corpses' items are missing.</summary>
    public static string ItemsWorthOf(LootMember m) =>
        m.Items == 0 && m.Unrecorded > 0 ? "—" : Worth(m.ItemValue, m.Estimated > 0, m.Unpriced > 0 || m.Unrecorded > 0);

    /// <summary>"3sp 41cp" (platinum, gold, silver, copper; a hundred of each make the next).</summary>
    public static string Coin(long copper)
    {
        if (copper == 0) return "0cp";
        var sign = copper < 0 ? "-" : "";
        copper = Math.Abs(copper);
        long pp = copper / 1_000_000, gp = copper / 10_000 % 100, sp = copper / 100 % 100, cp = copper % 100;
        var parts = new[] { (pp, "pp"), (gp, "gp"), (sp, "sp"), (cp, "cp") }.Where(p => p.Item1 > 0).Select(p => $"{p.Item1}{p.Item2}");
        return sign + string.Join(" ", parts);
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n:N0} {word}s";

    /// <summary>One event as a line of text: "[Rusty Iron Axe] x2  from a Dustrend legionary".</summary>
    public static string Describe(LootEvent e) => e.Kind switch
    {
        LootKind.Item => $"[{e.Item}]{(e.Count > 1 ? $" x{e.Count}" : "")}  from {e.Source}",
        _ => $"{Coin(e.Copper)}  from {e.Source}{(e.Shared ? (e.Split > 0 ? $"  (split; your share {Coin(e.Split)})" : "  (split)") : "")}",
    };

    /// <summary>One item as a single line for the clipboard: "Rusty Iron Mace ×3  ·  looted by 2 people (Fenwick 2, Aldric)  ·  ~1sp each  ·  ~3sp".</summary>
    public static string ItemLine(ItemWorth i)
    {
        var who = string.Join(", ", i.By.Select(b => b.Count > 1 ? $"{b.Who} {b.Count}" : b.Who));
        var mark = i.Value.Estimated ? "~" : "";
        var price = i.Value.Known ? $"{mark}{Coin(i.Value.Each)} each  ·  {mark}{Coin(i.Total)}" : "no known price";
        var people = i.By.Count == 1 ? "1 person" : $"{i.By.Count} people";
        return $"{i.Item}{(i.Count > 1 ? $" ×{i.Count}" : "")}  ·  looted by {people}{(who.Length > 0 ? $" ({who})" : "")}  ·  {price}";
    }

    /// <summary>The items tab as text: a heading, then <see cref="ItemLine"/> for every kind.</summary>
    public static string ItemsText(DateOnly day, IReadOnlyList<ItemWorth> items, string scope = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("LOOT ITEMS  " + day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture) + (scope.Length > 0 ? "  ·  " + scope : ""));
        if (items.Count == 0) { sb.AppendLine("No items looted."); return sb.ToString(); }
        sb.AppendLine($"{Plural(items.Count, "kind")}  ·  {items.Sum(i => i.Count):N0} taken");
        foreach (var i in items) sb.AppendLine(ItemLine(i));
        return sb.ToString();
    }

    /// <summary>One item one player took, as a line: how many, their share of the group's, where from, and the price.</summary>
    public static string MemberItemLine(MemberItem i)
    {
        var mark = i.Value.Estimated ? "~" : "";
        var price = i.Value.Known ? $"{mark}{Coin(i.Value.Each)} each  ·  {mark}{Coin(i.Total)}" : "no known price";
        return $"{i.Item}{(i.Count > 1 ? $" ×{i.Count}" : "")}  ·  {i.Count} of the group's {i.GroupCount}{(i.MostlyFrom.Length > 0 ? $"  ·  mostly from {i.MostlyFrom}" : "")}  ·  {price}";
    }

    /// <summary>Each looter's name and how many loots they have made, most first, on one line: "Hollis 33, Perrin 20, Aldric 15".</summary>
    public static string LootCounts(IEnumerable<LootMember> members) =>
        string.Join(", ", members.OrderByDescending(m => m.Mobs).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(m => $"{m.Name} {m.Mobs}"));

    /// <summary>What a player looted, to paste anywhere: their name, then a line per item with how many ("Wool Scraps ×59"), most first.</summary>
    public static string MemberText(LootMember m) =>
        string.Join(Environment.NewLine, new[] { m.Name }.Concat(m.Taken.OrderByDescending(i => i.Count).ThenBy(i => i.Item, StringComparer.OrdinalIgnoreCase).Select(i => $"{i.Item} ×{i.Count}")));

    /// <summary>
    /// A day laid out to read, in fixed-width columns: the totals, a table per looter (class and level when the ledger gave
    /// them), the <paramref name="top"/> most-looted items with who took them, and the mobs looted most.
    /// </summary>
    public static string Breakdown(DateOnly day, IReadOnlyList<LootEvent> events, int top = 12, ItemValues? values = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LOOT  " + day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture));
        if (events.Count == 0)
        {
            sb.AppendLine("Nothing looted.");
            return sb.ToString();
        }
        var t = Totals(events);
        sb.AppendLine($"{events.Min(e => e.Time):HH:mm} to {events.Max(e => e.Time):HH:mm}  ·  {Plural(t.Mobs, "mob")} looted  ·  {Plural(t.Items, "item")}  ·  {Plural(t.Players, "looter")}");
        var worth = LootDay.Of(day, events, values);
        sb.AppendLine($"Purses {Coin(t.Copper)}" + (worth.ItemValue > 0 ? $"  ·  items {Worth(worth.ItemValue, worth.Estimated > 0, worth.Unpriced > 0)}  ·  total {Worth(worth.Total, worth.Estimated > 0, worth.Unpriced > 0)}" : "")
            + (t.Share > 0 ? $"  ·  {Coin(t.Share)} reached you as splits" : ""));
        if (worth.Members.Any(m => m.You))
        {
            var ours = worth.Ours;
            sb.AppendLine($"Ours {Worth(ours.Total, ours.Approximate, ours.Unpriced > 0)}  ·  coin {(ours.EstimatedShares > 0 ? "~" : "")}{Coin(ours.Copper)} + items {Worth(ours.ItemValue, ours.Estimated > 0, ours.Unpriced > 0)}");
        }
        if (worth.Unpriced > 0) sb.AppendLine($"Not counted: {Plural(worth.Unpriced, "item")} with no known price (+)" + (worth.Estimated > 0 ? $"; {worth.Estimated:N0} more are valued by their family (~)" : ""));

        // No coin column: of other players the ledger shows only the purses they looted, never what an autosplit gave them
        Table(sb, "PLAYERS", ["PLAYER", "CLASS", "MOBS", "ITEMS", "ITEMS WORTH"],
            worth.Members.Select(m => new[]
            {
                m.Name + (m.You ? " (you)" : ""), m.ClassLevel, m.Mobs.ToString(), m.Items.ToString(),
                m.ItemValue > 0 ? Worth(m.ItemValue, m.Estimated > 0, m.Unpriced > 0) : "-",
            }).ToList(),
            [false, false, true, true, true]);

        var items = ByItem(events);
        Table(sb, items.Count > top ? $"ITEMS (top {top} of {items.Count} kinds)" : "ITEMS", ["ITEM", "QTY", "TAKEN BY"],
            items.Take(top).Select(i => new[]
            {
                i.Item, i.Count.ToString(),
                string.Join(", ", i.By.Take(3).Select(b => b.Count > 1 ? $"{b.Who} {b.Count}" : b.Who)) + (i.By.Count > 3 ? $" +{i.By.Count - 3}" : ""),
            }).ToList(), [false, true, false]);

        var mobs = Corpses(events).Where(c => c.Mob).GroupBy(c => c.Source, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Count: g.Count(), Purses: g.Where(c => c.Copper > 0).ToList(), Items: g.Sum(c => c.Items)))
            .OrderByDescending(m => m.Count).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (mobs.Count > 0)
            Table(sb, mobs.Count > top ? $"MOBS (top {top} of {mobs.Count} kinds)" : "MOBS", ["MOB", "LOOTED", "COIN EACH", "ITEMS EACH"],
                mobs.Take(top).Select(m => new[]
                {
                    m.Name, m.Count.ToString(), m.Purses.Count > 0 ? Coin(m.Purses.Sum(c => c.Copper) / m.Purses.Count) : "-",
                    (m.Items / (double)m.Count).ToString("0.0", CultureInfo.InvariantCulture),
                }).ToList(), [false, true, true, true]);
        return sb.ToString();
    }

    /// <summary>A titled table in fixed-width columns (<paramref name="right"/>: which to right-align); long text is cut with "…".</summary>
    private static void Table(StringBuilder sb, string title, string[] head, IReadOnlyList<string[]> rows, bool[] right, int cap = 48)
    {
        sb.AppendLine().AppendLine(title);
        string Fit(string s) => s.Length > cap ? s[..(cap - 1)] + "…" : s;
        var cells = rows.Select(r => r.Select(Fit).ToArray()).ToList();
        var width = head.Select((h, i) => Math.Max(h.Length, cells.Count == 0 ? 0 : cells.Max(r => r[i].Length))).ToArray();
        string Line(IReadOnlyList<string> row) => string.Join("  ", row.Select((c, i) => right[i] ? c.PadLeft(width[i]) : c.PadRight(width[i]))).TrimEnd();
        sb.AppendLine(Line(head));
        foreach (var r in cells) sb.AppendLine(Line(r));
    }
}
