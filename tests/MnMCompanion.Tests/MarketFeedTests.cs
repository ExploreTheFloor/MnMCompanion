using MnMCompanion.Market;

namespace MnMCompanion.Tests;

/// <summary>Reading mnm-db.com's auctions.json: the listings, the cards, the stack sizes, the requests and the reader's health.</summary>
public class MarketFeedTests
{
    private static MarketFeed Feed() => MarketFeed.TryParse(MarketFixture.Json) ?? throw new Xunit.Sdk.XunitException("the fixture didn't parse");

    [Fact]
    public void Every_listing_is_read_with_its_prices_per_lot_and_per_one()
    {
        var feed = Feed();
        Assert.Equal(11, feed.Listings.Count);
        Assert.Equal(new DateTime(2026, 10, 31, 19, 0, 4, 996, DateTimeKind.Utc), feed.GeneratedAt);
        Assert.Equal(DateTimeKind.Utc, feed.GeneratedAt.Kind);

        var ore = feed.Listings[0];
        Assert.Equal(("Trem", MarketIntent.Sell, "Copper Ore", 500L, 25L, 20, true, "Avelin", 2, true), (ore.Server, ore.Intent, ore.Item, ore.Price, ore.Unit, ore.Stack, ore.PerStack, ore.Player, ore.Count, ore.Matched));
        Assert.Equal(new DateTime(2026, 10, 31, 18, 57, 49, 31, DateTimeKind.Utc), ore.Seen);
        Assert.Equal(DateTimeKind.Utc, ore.Seen.Kind);

        var pack = feed.Listings[4];
        Assert.Equal((1200L, 600L, 2, null), (pack.Price, pack.Unit, pack.Qty, pack.Stack));
        Assert.False(pack.PerStack);
    }

    [Fact]
    public void A_post_without_a_price_an_unreadable_intent_and_a_trade_are_still_listings()
    {
        var feed = Feed();
        var flux = feed.Listings.Single(l => l.Item == "Flux");
        Assert.Null(flux.Price);
        Assert.Null(flux.Unit);
        Assert.Equal(MarketIntent.Unknown, feed.Listings.Single(l => l.Player == "Jorah").Intent);
        Assert.Equal(MarketIntent.Trade, feed.Listings.Single(l => l.Player == "Kestrel").Intent);
        Assert.True(feed.Listings.Single(l => l.Player == "Gwil").Assumed);
        Assert.False(feed.Listings.Single(l => l.Item == "TB").Matched);
    }

    [Fact]
    public void The_cards_say_what_an_item_is()
    {
        var feed = Feed();
        Assert.Equal(4, feed.Cards.Count);
        var sword = feed.Cards["iron longsword"];
        Assert.Equal(("Primary", "1H Slashing", 6.0, 30.0, 5.5, 1250L), (sword.Slot, sword.Handed, sword.Dmg, sword.Delay, sword.Weight, sword.Vendor));
        Assert.Equal(2, sword.Stats["str"]);
        Assert.True(sword.IsWeapon);
        Assert.Equal("Primary · 1H Slashing · 6 dmg 30 dly · Str +2 Sta +1 · 5.5 wt · WAR PAL", sword.Summary());

        var pack = feed.Cards["Rawhide Backpack"];   // any case
        Assert.True(pack.IsContainer);
        Assert.Equal(10, pack.Capacity);
        Assert.Equal("Backpack · 10 slots · extra large · 2.5 wt", pack.Summary());

        Assert.True(feed.Cards["barnacle bracer"].IsArmor);   // a bracer is armor; only neck, ear and finger are jewelry
        Assert.False(feed.Cards["barnacle bracer"].IsJewelry);
        Assert.Equal("", feed.Cards["copper ore"].Summary().Replace("0.25 wt", ""));
    }

    [Fact]
    public void Slots_come_in_either_case_and_several_to_an_item()
    {
        Assert.True(new ItemCard { Slot = "PRIMARY SECONDARY BELT" }.IsWeapon);
        Assert.True(new ItemCard { Slot = "Secondary Range" }.IsWeapon);
        Assert.True(new ItemCard { Slot = "BAG BELT" }.IsContainer);
        Assert.True(new ItemCard { Slot = "EAR" }.IsJewelry);
        Assert.True(new ItemCard { Slot = "Finger" }.IsJewelry);
        Assert.True(new ItemCard { Slot = "FACE" }.IsArmor);
        Assert.True(new ItemCard { Slot = "SHOULDERS" }.IsArmor);
        Assert.False(new ItemCard { Slot = "RANGE" }.IsArmor);
        Assert.Equal("Primary Secondary · Two Handed · 9 dmg 40 dly", new ItemCard { Slot = "PRIMARY SECONDARY", Handed = "TWO HANDED", Dmg = 9, Delay = 40 }.Summary());
        Assert.Equal("10 slots · small", new ItemCard { Capacity = 10, Size = "SMALL\n| ITEM_LINK_ID-OLD = X" }.Summary());
    }

    [Fact]
    public void Stack_sizes_requests_and_the_readers_health_are_read()
    {
        var feed = Feed();
        Assert.Equal(20, feed.Stacks["copper ore"]);
        Assert.Equal(2, feed.Requests.Count);
        Assert.Equal(new[] { 7 }, feed.Requests[0].Plus);
        Assert.Equal(new[] { "str", "sta" }, feed.Requests[1].Stats);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 48, 43, 31, DateTimeKind.Utc), feed.Requests[1].LastSeen);
        Assert.NotNull(feed.Capture);
        Assert.Equal((664, 638, 52, 0), (feed.Capture!.LiveCycles24h, feed.Capture.ReadingCycles24h, feed.Capture.LongestBlindMin, feed.Capture.BlindNowMin));
        Assert.Equal(new[] { "Trem" }, feed.Servers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"items\": []}")]
    [InlineData("{\"listings\": {}}")]
    public void Anything_that_is_not_the_feed_is_refused(string json)
    {
        Assert.Null(MarketFeed.TryParse(json));
    }

    [Fact]
    public void A_listing_missing_its_name_or_player_is_skipped_and_a_missing_time_is_the_feeds()
    {
        var feed = MarketFeed.TryParse("""{"generatedAt":"2026-10-31T19:00:00Z","listings":[{"item":"","player":"A","price":1},{"item":"Wood","player":"B","price":100,"unit":5,"intent":"sell","matched":true}]}""")!;
        var wood = Assert.Single(feed.Listings);
        Assert.Equal(feed.GeneratedAt, wood.Seen);
        Assert.Equal(1, wood.Count);
    }
}
