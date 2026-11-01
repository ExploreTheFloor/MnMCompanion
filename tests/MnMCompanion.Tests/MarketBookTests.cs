using MnMCompanion.Market;

namespace MnMCompanion.Tests;

/// <summary>
/// The price book built from the feed's snapshots: what makes a row, how a later snapshot changes the book (a changed price replaces the row and
/// keeps the old one as history; the same price read again marks the post still up), and the file it is kept in.
/// </summary>
public class MarketBookTests
{
    private static readonly DateTime Made = new(2026, 10, 31, 19, 0, 4, 996, DateTimeKind.Utc);

    private static MarketFeed Feed(string json = MarketFixture.Json) => MarketFeed.TryParse(json) ?? throw new Xunit.Sdk.XunitException("the feed didn't parse");

    private static string Listing(string player, string item, long price, long unit, string seen, int count, string intent = "sell", int? stack = null, int? qty = null) =>
        $$"""{ "server": "Trem", "intent": "{{intent}}", "item": "{{item}}", "price": {{price}}, "unit": {{unit}}, "player": "{{player}}", "seen": "{{seen}}", "count": {{count}}, "matched": true{{(stack is { } s ? $", \"stack\": {s}, \"perStack\": true" : "")}}{{(qty is { } q ? $", \"qty\": {q}" : "")}} }""";

    private static string FeedJson(string generatedAt, params string[] listings) =>
        $$"""{ "generatedAt": "{{generatedAt}}", "listings": [ {{string.Join(",\n", listings)}} ] }""";

    [Fact]
    public void Only_a_priced_sale_or_bid_of_an_item_the_wiki_knows_makes_a_row()
    {
        var book = new MarketBook();
        var result = book.Merge(Feed());

        Assert.Equal(7, book.Rows.Count);
        Assert.Equal((7, 0, 0, 4), (result.Added, result.Changed, result.Refreshed, result.Skipped));   // Flux (no price), TB (unknown), Jorah (no intent), Kestrel (trade)
        Assert.DoesNotContain(book.Rows.Values, r => r.Item is "Flux" or "TB" or "Wolf Pelt");
        Assert.Equal(Made, book.GeneratedAt);
        Assert.Empty(book.History);
    }

    [Fact]
    public void A_row_holds_the_price_of_one_the_lot_and_the_stack_or_quantity()
    {
        var book = new MarketBook();
        book.Merge(Feed());

        var ore = book.Rows[MarketBook.Key("Trem", "Avelin", "Copper Ore", MarketIntent.Sell)];
        Assert.Equal((25L, 500L, 20, null), (ore.Copper, ore.Lot, ore.Stack, ore.Quantity));
        Assert.True(ore.Bulk);
        Assert.Equal(ore.Time, ore.Seen);   // first read: the only time known
        Assert.Equal(new DateTime(2026, 10, 31, 18, 57, 49, 31, DateTimeKind.Utc), ore.Time);

        var packs = book.Rows[MarketBook.Key("Trem", "Elowen", "Rawhide Backpack", MarketIntent.Sell)];
        Assert.Equal((600L, 1200L, null, 2), (packs.Copper, packs.Lot, packs.Stack, packs.Quantity));

        var bid = book.Rows[MarketBook.Key("Trem", "Faelan", "Barnacle Bracer", MarketIntent.Buy)];
        Assert.Equal(MarketIntent.Buy, bid.Intent);
        Assert.True(book.Rows[MarketBook.Key("Trem", "Gwil", "Iron Longsword", MarketIntent.Sell)].Assumed);
    }

    [Fact]
    public void The_same_snapshot_again_or_an_older_one_changes_nothing()
    {
        var book = new MarketBook();
        book.Merge(Feed());
        var before = book.Rows.Values.OrderBy(r => r.Key).ToList();

        Assert.Equal(0, book.Merge(Feed()).Touched);
        Assert.Equal(0, book.Merge(Feed(FeedJson("2026-10-31T13:00:00Z", Listing("Avelin", "Copper Ore", 900, 45, "2026-10-31T12:00:00Z", 1, stack: 20)))).Touched);
        Assert.Equal(before, book.Rows.Values.OrderBy(r => r.Key).ToList());
        Assert.Equal(Made, book.GeneratedAt);
    }

    [Fact]
    public void A_post_read_again_since_the_last_snapshot_is_marked_seen_at_the_snapshots_time()
    {
        var book = new MarketBook();
        book.Merge(Feed(FeedJson("2026-10-31T13:00:00Z", Listing("Avelin", "Copper Ore", 500, 25, "2026-10-31T12:00:00Z", 2, stack: 20))));
        var later = Feed(FeedJson("2026-10-31T19:00:00Z", Listing("Avelin", "Copper Ore", 500, 25, "2026-10-31T12:00:00Z", 5, stack: 20)));

        var result = book.Merge(later);

        var row = Assert.Single(book.Rows.Values);
        Assert.Equal((0, 0, 1), (result.Added, result.Changed, result.Refreshed));
        Assert.Equal(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), row.Time);    // the price is as old as ever
        Assert.Equal(new DateTime(2026, 10, 31, 19, 0, 0, DateTimeKind.Utc), row.Seen);    // but it was up within the last six hours
        Assert.Equal(5, row.Count);
        Assert.Empty(book.History);
    }

    [Fact]
    public void A_post_not_read_again_keeps_its_old_seen_time()
    {
        var book = new MarketBook();
        book.Merge(Feed(FeedJson("2026-10-31T13:00:00Z", Listing("Avelin", "Copper Ore", 500, 25, "2026-10-31T12:00:00Z", 2, stack: 20))));

        var result = book.Merge(Feed(FeedJson("2026-10-31T19:00:00Z", Listing("Avelin", "Copper Ore", 500, 25, "2026-10-31T12:00:00Z", 2, stack: 20))));

        Assert.Equal(0, result.Touched);
        Assert.Equal(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), Assert.Single(book.Rows.Values).Seen);
    }

    [Fact]
    public void A_changed_price_replaces_the_row_and_keeps_the_old_one_as_history()
    {
        var book = new MarketBook();
        book.Merge(Feed(FeedJson("2026-10-31T13:00:00Z", Listing("Avelin", "Copper Ore", 500, 25, "2026-10-31T12:00:00Z", 2, stack: 20))));

        var result = book.Merge(Feed(FeedJson("2026-10-31T19:00:00Z", Listing("Avelin", "Copper Ore", 400, 20, "2026-10-31T15:30:00Z", 1, stack: 20))));

        Assert.Equal((0, 1, 0), (result.Added, result.Changed, result.Refreshed));
        var now = Assert.Single(book.Rows.Values);
        Assert.Equal((20L, 400L, new DateTime(2026, 10, 31, 15, 30, 0, DateTimeKind.Utc)), (now.Copper, now.Lot, now.Time));
        var old = Assert.Single(book.History);
        Assert.Equal(25L, old.Copper);
        var versions = book.Versions(MarketBook.ItemKey("Trem", "Copper Ore"));
        Assert.Equal(new long[] { 25, 20 }, versions.Select(v => v.Copper));
    }

    [Fact]
    public void A_buyer_and_a_seller_of_the_same_item_are_two_rows_and_a_name_is_matched_in_any_case()
    {
        var book = new MarketBook();
        book.Merge(Feed(FeedJson("2026-10-31T13:00:00Z",
            Listing("Avelin", "Copper Ore", 500, 25, "2026-10-31T12:00:00Z", 1, stack: 20),
            Listing("Avelin", "copper ore", 200, 10, "2026-10-31T12:00:00Z", 1, intent: "buy", stack: 20))));
        Assert.Equal(2, book.Rows.Count);
        Assert.Single(book.Rows.Values.GroupBy(r => r.ItemKey));
    }

    [Fact]
    public void Rows_silent_for_long_go_to_the_history_and_very_old_history_is_dropped()
    {
        var book = new MarketBook();
        book.Merge(Feed(FeedJson("2026-09-01T13:00:00Z", Listing("Avelin", "Copper Ore", 500, 25, "2026-09-01T12:00:00Z", 1, stack: 20))));
        book.History.Add(book.Rows.Values.First() with { Player = "Old", Seen = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc) });

        book.Merge(Feed(FeedJson("2026-10-31T19:00:00Z", Listing("Borrik", "Wood", 200, 10, "2026-10-31T12:00:00Z", 1, stack: 20))));

        Assert.Equal("Borrik", Assert.Single(book.Rows.Values).Player);
        Assert.Equal("Avelin", Assert.Single(book.History).Player);   // Avelin's row moved to the history; the May row is gone
    }

    [Fact]
    public void Cards_stacks_requests_and_the_readers_health_come_along()
    {
        var book = new MarketBook();
        book.Merge(Feed());
        Assert.Equal(20, book.StackOf("Copper Ore"));
        Assert.Equal("Rawhide Backpack", book.Card("rawhide backpack")!.Name);
        Assert.Equal(2, book.Requests.Count);
        Assert.Equal(0, book.Capture!.BlindNowMin);
        Assert.Equal(new[] { "Trem" }, book.Servers);
    }

    [Fact]
    public void The_book_survives_the_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mnmcompanion-market-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "market.json");
            var book = new MarketBook();
            book.Merge(Feed(FeedJson("2026-10-31T13:00:00Z", Listing("Avelin", "Copper Ore", 500, 25, "2026-10-31T12:00:00Z", 2, stack: 20))));
            book.Merge(Feed());
            Assert.True(book.Save(path));

            var read = MarketBook.Load(path);

            Assert.NotNull(read);
            Assert.Equal(book.Rows.Count, read!.Rows.Count);
            Assert.Equal(book.Rows.Values.OrderBy(r => r.Key), read.Rows.Values.OrderBy(r => r.Key));
            Assert.Equal(book.History, read.History);
            Assert.Equal(book.GeneratedAt, read.GeneratedAt);
            Assert.Equal(DateTimeKind.Utc, read.Rows.Values.First().Seen.Kind);
            Assert.Equal(20, read.StackOf("COPPER ORE"));
            Assert.Equal(6.0, read.Card("Iron Longsword")!.Dmg);
            Assert.Equal(2, read.Card("Iron Longsword")!.Stats["str"]);
            Assert.Equal(2, read.Requests.Count);

            Assert.Null(MarketBook.Load(Path.Combine(folder, "missing.json")));
            File.WriteAllText(path, "nonsense");
            Assert.Null(MarketBook.Load(path));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void The_meta_survives_the_file_and_backs_off_when_asked()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mnmcompanion-market-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bookPath = Path.Combine(folder, "market.json");
            var meta = new MarketMeta { ETag = "\"abc\"", CheckedUtc = Made };
            meta.BackOff(TimeSpan.FromDays(9), Made);
            Assert.Equal(Made + MarketRefresh.MaxBackoff, meta.NotBeforeUtc);
            Assert.True(meta.Save(bookPath));
            Assert.Equal(Path.Combine(folder, "market.meta.json"), MarketMeta.PathFor(bookPath));

            var read = MarketMeta.Load(bookPath);
            Assert.Equal(("\"abc\"", Made, Made + MarketRefresh.MaxBackoff), (read.ETag, read.CheckedUtc, read.NotBeforeUtc));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
