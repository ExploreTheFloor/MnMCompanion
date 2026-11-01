using System.Globalization;
using System.Text.Json;

namespace MnMCompanion.Market;

/// <summary>What a player wants with an item: to sell it, to buy it, to trade it, or to know its price.</summary>
public enum MarketIntent { Sell, Buy, Trade, Inquiry, Unknown }

/// <summary>
/// One listing as mnm-db.com publishes it: one row per server, player and item, the player's current ask (or bid). <see cref="Price"/> is the
/// whole lot in copper (for a bulk material, one stack), <see cref="Unit"/> the price of one, both null when the post named no price.
/// <see cref="Seen"/> is when this price was FIRST read, not last: the site keeps no last-seen. <see cref="Count"/> is how many times the
/// post was read, which grows while the player keeps posting. <see cref="Matched"/> says the name is an item the site's wiki knows.
/// </summary>
public sealed record FeedListing(
    string Server, MarketIntent Intent, string Item, long? Price, long? Unit, string? PriceStr, int? Stack, bool PerStack,
    string Player, DateTime Seen, int Count, bool Matched, int? Qty, bool Assumed);

/// <summary>A gear request read off the auction channel: "+3 Enchants |Head| Int/Wis"; the stat bonus asked for, the stats, the slot.</summary>
public sealed record FeedRequest(string Server, string Player, int[] Plus, string[] Stats, string? Category, string Text, DateTime Seen, DateTime LastSeen, int Count);

/// <summary>How well the site's reader did over the last day: it reads the LiveMNM stream, and a moved chat box leaves it blind.</summary>
public sealed record FeedCapture(int LiveCycles24h, int ReadingCycles24h, int OfflineCycles24h, int LongestBlindMin, int BlindNowMin, DateTime? LastLinesAt);

/// <summary>What the site's wiki knows about an item: what it is and what it does, for the row and the breakdown.</summary>
public sealed class ItemCard
{
    public string Name { get; init; } = "";
    public bool HasPage { get; init; }
    public string? Icon { get; init; }
    public string[] Flags { get; init; } = [];
    public string? Slot { get; init; }
    public string? Handed { get; init; }
    public double? Dmg { get; init; }
    public double? Delay { get; init; }
    public string? Skill { get; init; }
    public double? Ac { get; init; }
    public IReadOnlyDictionary<string, double> Stats { get; init; } = new Dictionary<string, double>();
    public double? Hp { get; init; }
    public double? Mana { get; init; }
    public double? Weight { get; init; }
    public string? Size { get; init; }
    public int? Capacity { get; init; }
    public string? Effect { get; init; }
    public string? Class { get; init; }
    public string? Race { get; init; }
    public string[] Tradeskills { get; init; } = [];
    public long? Vendor { get; init; }

    /// <summary>The slot's words ("PRIMARY SECONDARY BELT" is three), upper case: the wiki writes them in either case and several to an item.</summary>
    private string[] SlotWords => (Slot ?? "").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private bool Worn(params string[] words) => SlotWords.Any(words.Contains);

    public bool IsWeapon => Dmg is > 0 || Worn("PRIMARY", "SECONDARY", "RANGE", "RANGED", "AMMO");
    public bool IsContainer => Capacity is > 0 || Worn("BAG", "BACKPACK", "SADDLEBAG");
    public bool IsJewelry => !IsWeapon && Worn("NECK", "EAR", "EARS", "FINGER", "FINGERS", "RING");
    public bool IsArmor => !IsWeapon && !IsContainer && !IsJewelry && (Ac is > 0 || SlotWords.Length > 0);

    /// <summary>"Primary · 1H Slashing · 6 dmg 30 dly · Str +3 Sta +2 · 3.5 wt", the facts in one line; empty when the card has none.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (SlotWords.Length > 0) parts.Add(string.Join(" ", SlotWords.Select(Title)) + (Handed is { Length: > 0 } h ? " · " + Title(h) : ""));
        if (Skill is { Length: > 0 } skill) parts.Add(skill);
        if (Dmg is { } d && Delay is { } dl) parts.Add($"{d:0.#} dmg {dl:0.#} dly");
        else if (Dmg is { } d2) parts.Add($"{d2:0.#} dmg");
        if (Ac is { } ac) parts.Add($"{ac:0.#} AC");
        if (Stats.Count > 0) parts.Add(string.Join(" ", Stats.OrderBy(s => StatOrder(s.Key)).Select(s => $"{Capital(s.Key)} {s.Value:+0.#;-0.#}")));
        if (Hp is { } hp && hp != 0) parts.Add($"HP {hp:+0;-0}");
        if (Mana is { } mana && mana != 0) parts.Add($"Mana {mana:+0;-0}");
        if (Effect is { Length: > 0 } effect) parts.Add("Effect: " + effect);
        if (Capacity is { } cap) parts.Add($"{cap} slots" + (Size is { Length: > 0 } sz ? $" · {FirstLine(sz).ToLowerInvariant()}" : ""));
        if (Weight is { } wt) parts.Add($"{wt:0.##} wt");
        if (Class is { Length: > 0 } cls && cls != "ALL") parts.Add(cls);
        if (Race is { Length: > 0 } race && race != "ALL") parts.Add(race);
        return string.Join(" · ", parts);
    }

    /// <summary>"PRIMARY" and "primary" as "Primary"; "Two Handed" and "1H Slashing" as they are.</summary>
    private static string Title(string words) => string.Join(" ", words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w =>
        w.Length > 1 && (w.All(char.IsUpper) || w.All(char.IsLower)) ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant() : w));

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();

    private static int StatOrder(string key) => Array.IndexOf(["str", "sta", "agi", "dex", "wis", "int", "cha"], key.ToLowerInvariant()) is var i and >= 0 ? i : 99;
    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>
/// The file mnm-db.com publishes (<c>auctions.json</c>): every current listing read off the auction channel of the LiveMNM stream, the gear
/// requests, a wiki card for every item listed, and the stack size of every bulk material. It reads what it can and skips what it can't; a
/// document without a <c>listings</c> list is not the feed.
/// </summary>
public sealed class MarketFeed
{
    public DateTime GeneratedAt { get; init; }
    public FeedCapture? Capture { get; init; }
    public IReadOnlyList<FeedListing> Listings { get; init; } = [];
    public IReadOnlyList<FeedRequest> Requests { get; init; } = [];
    /// <summary>By the item's name in lower case.</summary>
    public IReadOnlyDictionary<string, ItemCard> Cards { get; init; } = new Dictionary<string, ItemCard>();
    /// <summary>By the item's name in lower case: how many make a stack.</summary>
    public IReadOnlyDictionary<string, int> Stacks { get; init; } = new Dictionary<string, int>();

    /// <summary>The servers the listings are from, the busiest first.</summary>
    public IReadOnlyList<string> Servers => Listings.GroupBy(l => l.Server).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();

    public static MarketFeed? TryParse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("listings", out var listings) || listings.ValueKind != JsonValueKind.Array) return null;
            var generated = Time(root, "generatedAt") ?? DateTime.UtcNow;
            var rows = new List<FeedListing>(listings.GetArrayLength());
            foreach (var l in listings.EnumerateArray())
            {
                if (l.ValueKind != JsonValueKind.Object) continue;
                var item = Str(l, "item").Trim();
                var player = Str(l, "player").Trim();
                if (item.Length == 0 || player.Length == 0) continue;
                rows.Add(new FeedListing(
                    Str(l, "server"), Intent(Str(l, "intent")), item, Long(l, "price"), Long(l, "unit"), StrOrNull(l, "priceStr"),
                    Int(l, "stack"), Bool(l, "perStack"), player, Time(l, "seen") ?? generated, Int(l, "count") ?? 1,
                    Bool(l, "matched"), Int(l, "qty"), Bool(l, "assumed")));
            }
            var requests = new List<FeedRequest>();
            if (root.TryGetProperty("requests", out var reqs) && reqs.ValueKind == JsonValueKind.Array)
                foreach (var r in reqs.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) continue;
                    var seen = Time(r, "seen") ?? generated;
                    requests.Add(new FeedRequest(Str(r, "server"), Str(r, "player"), Ints(r, "plus"), Strs(r, "stats"), StrOrNull(r, "category"), Str(r, "text"), seen, Time(r, "lastSeen") ?? seen, Int(r, "count") ?? 1));
                }
            var cards = new Dictionary<string, ItemCard>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
                foreach (var p in stats.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Object && Card(p.Value, p.Name) is { } card) cards[p.Name] = card;
            var stacks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("stacks", out var st) && st.ValueKind == JsonValueKind.Object)
                foreach (var p in st.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n) && n > 0) stacks[p.Name] = n;
            FeedCapture? capture = null;
            if (root.TryGetProperty("capture", out var cap) && cap.ValueKind == JsonValueKind.Object)
                capture = new FeedCapture(Int(cap, "liveCycles24h") ?? 0, Int(cap, "readingCycles24h") ?? 0, Int(cap, "offlineCycles24h") ?? 0, Int(cap, "longestBlindMin") ?? 0, Int(cap, "blindNowMin") ?? 0, Time(cap, "lastLinesAt"));
            return new MarketFeed { GeneratedAt = generated, Capture = capture, Listings = rows, Requests = requests, Cards = cards, Stacks = stacks };
        }
        catch (JsonException) { return null; }
    }

    private static ItemCard? Card(JsonElement e, string key)
    {
        var name = Str(e, "name").Trim();
        if (name.Length == 0) name = key;
        var statDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (e.TryGetProperty("stats", out var s) && s.ValueKind == JsonValueKind.Object)
            foreach (var p in s.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var v) && v != 0) statDict[p.Name] = v;
        int? capacity = null;
        if (e.TryGetProperty("container", out var c) && c.ValueKind == JsonValueKind.Object) capacity = Int(c, "capacity");
        return new ItemCard
        {
            Name = name, HasPage = Bool(e, "hasPage"), Icon = StrOrNull(e, "icon"), Flags = Strs(e, "flags"), Slot = StrOrNull(e, "slot"), Handed = StrOrNull(e, "handed"),
            Dmg = Dbl(e, "dmg"), Delay = Dbl(e, "delay"), Skill = StrOrNull(e, "skill"), Ac = Dbl(e, "ac"), Stats = statDict, Hp = Dbl(e, "hp"), Mana = Dbl(e, "mana"),
            Weight = Dbl(e, "weight"), Size = StrOrNull(e, "size"), Capacity = capacity, Effect = StrOrNull(e, "effect"), Class = StrOrNull(e, "class"), Race = StrOrNull(e, "race"),
            Tradeskills = Strs(e, "tradeskills"), Vendor = Long(e, "vendor"),
        };
    }

    public static MarketIntent Intent(string s) => s.ToLowerInvariant() switch
    {
        "sell" => MarketIntent.Sell,
        "buy" => MarketIntent.Buy,
        "trade" => MarketIntent.Trade,
        "inquiry" => MarketIntent.Inquiry,
        _ => MarketIntent.Unknown,
    };

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static string? StrOrNull(JsonElement e, string name) => Str(e, name) is { Length: > 0 } s ? s : null;
    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static long? Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? (long)Math.Round(d) : null;
    private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? (int)Math.Round(d) : null;
    private static double? Dbl(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
    private static string[] Strs(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
        ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : [];
    private static int[] Ints(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
        ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToArray() : [];
    private static DateTime? Time(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null;
}
