using System.Text.Json;
using System.Text.Json.Serialization;

namespace MnMCompanion.Market;

/// <summary>
/// One player's current price for one item on one server (or one buyer's bid): the row the price book keeps. <see cref="Copper"/> is the
/// price of ONE (what prices are compared in); <see cref="Lot"/> what the post asked for the whole lot (a stack of <see cref="Stack"/> for a bulk
/// material, <see cref="Quantity"/> pieces otherwise). <see cref="Time"/> is when this price was first read, <see cref="Seen"/> when the post was
/// last known to be up: the first read, then the time of every snapshot that found it read again (<see cref="Count"/> grew).
/// </summary>
public sealed record MarketListing(
    string Server, string Player, string Item,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MarketIntent Intent,
    long Copper, long Lot, int? Stack, int? Quantity, DateTime Time, DateTime Seen, int Count, bool Assumed)
{
    [JsonIgnore] public string Key => MarketBook.Key(Server, Player, Item, Intent);
    [JsonIgnore] public string ItemKey => MarketBook.ItemKey(Server, Item);
    [JsonIgnore] public bool Bulk => Stack is > 1;
}

/// <summary>What folding a snapshot into the book came to.</summary>
public sealed record MergeResult(int Added, int Changed, int Refreshed, int Skipped)
{
    public int Touched => Added + Changed + Refreshed;
}

/// <summary>
/// The price book: who sells (or wants) what at what price, one row per server, player, item and side, built from the snapshots of
/// mnm-db.com's feed and kept on this PC (<c>market.json</c>). The feed itself holds only each player's current price, so the history
/// here (<see cref="History"/>: every price a newer one replaced) exists only from the snapshots this PC has taken. Rules, the same as a
/// price tracker's: a post without a price, or naming something the site's wiki doesn't know, makes no row; a changed price replaces the row
/// and keeps the old one as history; the same price read again only notes that the post is still up.
/// </summary>
public sealed class MarketBook
{
    public const int Version = 1;

    /// <summary>A row not seen for this long is dropped from the book (the site itself keeps priced posts 30 days).</summary>
    public static readonly TimeSpan KeepRows = TimeSpan.FromDays(45);

    /// <summary>History older than this is dropped.</summary>
    public static readonly TimeSpan KeepHistory = TimeSpan.FromDays(120);

    public int FileVersion { get; set; } = Version;

    /// <summary>When the latest snapshot folded in was made (UTC); null for an empty book.</summary>
    public DateTime? GeneratedAt { get; set; }

    /// <summary>The site's reader health from the latest snapshot.</summary>
    public FeedCapture? Capture { get; set; }

    public Dictionary<string, MarketListing> Rows { get; set; } = new(StringComparer.Ordinal);
    public List<MarketListing> History { get; set; } = [];
    public List<FeedRequest> Requests { get; set; } = [];
    public Dictionary<string, ItemCard> Cards { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Stacks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string ItemKey(string server, string item) => server.ToLowerInvariant() + "|" + item.ToLowerInvariant();

    public static string Key(string server, string player, string item, MarketIntent intent) =>
        $"{ItemKey(server, item)}|{player.ToLowerInvariant()}|{(intent == MarketIntent.Buy ? "b" : "s")}";

    /// <summary>The listing a feed row makes, or null when it makes none: no price, a name the wiki doesn't know, or neither a sale nor a bid.</summary>
    public static MarketListing? From(FeedListing l)
    {
        if (l.Price is not { } lot || l.Unit is not { } unit || unit <= 0 || !l.Matched) return null;
        if (l.Intent is not (MarketIntent.Sell or MarketIntent.Buy)) return null;
        int? stack = l.PerStack && l.Stack is > 1 ? l.Stack : null;
        int? qty = stack is null && l.Qty is > 1 ? l.Qty : null;
        return new MarketListing(l.Server, l.Player, l.Item, l.Intent, unit, lot, stack, qty, l.Seen, l.Seen, l.Count, l.Assumed);
    }

    /// <summary>
    /// Folds a snapshot of the feed into the book. A snapshot no newer than the last one changes nothing. Otherwise: a row the book doesn't
    /// have is added; a row with a changed price (or lot) replaces the book's and the old row goes to the history; the same price again with
    /// a higher read count means the post was seen again since the last snapshot, so the row is marked seen at the snapshot's time.
    /// </summary>
    public MergeResult Merge(MarketFeed feed)
    {
        if (GeneratedAt is { } have && feed.GeneratedAt <= have) return new MergeResult(0, 0, 0, feed.Listings.Count);
        int added = 0, changed = 0, refreshed = 0, skipped = 0;
        foreach (var l in feed.Listings)
        {
            if (From(l) is not { } now) { skipped++; continue; }
            var key = now.Key;
            if (!Rows.TryGetValue(key, out var cur)) { Rows[key] = now; added++; continue; }
            if (now.Time < cur.Time) { skipped++; continue; }   // an older price than the one we hold: the site's file went backwards; ignore it
            if (now.Copper != cur.Copper || now.Lot != cur.Lot || now.Stack != cur.Stack || now.Quantity != cur.Quantity)
            {
                History.Add(cur);
                Rows[key] = now;
                changed++;
                continue;
            }
            if (now.Count > cur.Count || now.Seen > cur.Seen)
            {
                Rows[key] = cur with { Seen = now.Count > cur.Count ? Max(feed.GeneratedAt, now.Seen) : now.Seen, Count = Math.Max(now.Count, cur.Count) };
                refreshed++;
            }
        }
        foreach (var (name, card) in feed.Cards) Cards[name] = card;
        foreach (var (name, size) in feed.Stacks) Stacks[name] = size;
        if (feed.Requests.Count > 0 || Requests.Count == 0) Requests = feed.Requests.ToList();
        Capture = feed.Capture ?? Capture;
        GeneratedAt = feed.GeneratedAt;
        Prune(feed.GeneratedAt);
        return new MergeResult(added, changed, refreshed, skipped);
    }

    /// <summary>Drops rows silent for <see cref="KeepRows"/> (into the history) and history older than <see cref="KeepHistory"/>.</summary>
    public void Prune(DateTime nowUtc)
    {
        foreach (var (key, row) in Rows.ToList())
            if (nowUtc - row.Seen > KeepRows) { Rows.Remove(key); History.Add(row); }
        History.RemoveAll(h => nowUtc - h.Seen > KeepHistory);
    }

    /// <summary>Every price ever held for an item, current and replaced, oldest first.</summary>
    public IReadOnlyList<MarketListing> Versions(string itemKey) =>
        Rows.Values.Where(r => r.ItemKey == itemKey).Concat(History.Where(h => h.ItemKey == itemKey)).OrderBy(v => v.Time).ToList();

    public ItemCard? Card(string item) => Cards.TryGetValue(item, out var card) ? card : null;

    public int? StackOf(string item) => Stacks.TryGetValue(item, out var n) ? n : null;

    /// <summary>The servers in the book, the one with most rows first.</summary>
    public IReadOnlyList<string> Servers => Rows.Values.GroupBy(r => r.Server, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    // ------------------------------------------------------------------ the file

    public static string DefaultPath => Path.Combine(AppFolder.Folder, "market.json");

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The book kept on this PC; null when there is none or it can't be read (the next snapshot starts a new one).</summary>
    public static MarketBook? Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return null;
            var book = JsonSerializer.Deserialize<MarketBook>(File.ReadAllText(path), Json);
            if (book is null || book.FileVersion > Version) return null;
            book.Rows = new Dictionary<string, MarketListing>(book.Rows, StringComparer.Ordinal);
            book.Cards = new Dictionary<string, ItemCard>(book.Cards, StringComparer.OrdinalIgnoreCase);
            book.Stacks = new Dictionary<string, int>(book.Stacks, StringComparer.OrdinalIgnoreCase);
            return book;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }

    /// <summary>Writes the book. False (and the old file kept) when it can't be written.</summary>
    public bool Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}

/// <summary>
/// What Companion remembers about asking mnm-db.com, kept beside the book (<c>market.meta.json</c>) so the asking rules hold across restarts: the
/// feed's version tag (the next question is "has it changed?", answered with a few bytes when it hasn't), when the kept book was last confirmed
/// current, when the site was last asked at all, and a time before which it must not be asked again.
/// </summary>
public sealed class MarketMeta
{
    public string? ETag { get; set; }
    public DateTime? CheckedUtc { get; set; }
    public DateTime? TriedUtc { get; set; }
    public DateTime? NotBeforeUtc { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PathFor(string? bookPath = null) => Path.ChangeExtension(bookPath ?? MarketBook.DefaultPath, ".meta.json");

    public static MarketMeta Load(string? bookPath = null)
    {
        try
        {
            var path = PathFor(bookPath);
            if (File.Exists(path) && JsonSerializer.Deserialize<MarketMeta>(File.ReadAllText(path)) is { } meta) return meta;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new MarketMeta();
    }

    public bool Save(string? bookPath = null)
    {
        try
        {
            var path = PathFor(bookPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The site said to wait (or is answering with errors): do not ask again before <paramref name="wait"/> has passed, at most a day.</summary>
    public void BackOff(TimeSpan? wait, DateTime nowUtc) =>
        NotBeforeUtc = nowUtc + Clamp(wait ?? MarketRefresh.DefaultBackoff, TimeSpan.FromMinutes(1), MarketRefresh.MaxBackoff);

    private static TimeSpan Clamp(TimeSpan value, TimeSpan low, TimeSpan high) => value < low ? low : value > high ? high : value;
}
