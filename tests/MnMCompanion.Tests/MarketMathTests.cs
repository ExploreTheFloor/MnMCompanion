using MnMCompanion.Market;

namespace MnMCompanion.Tests;

/// <summary>The market read from the book: what stands, the headline prices, the kinds, the days, the changes and the trend.</summary>
public class MarketMathTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 19, 30, 0, DateTimeKind.Utc);

    private static MarketSnapshot Market()
    {
        var book = new MarketBook();
        book.Merge(MarketFeed.TryParse(MarketFixture.Json)!);
        return MarketSnapshot.From(book, null, Now);
    }

    [Fact]
    public void An_item_gathers_its_sellers_and_buyers_and_prices_per_one()
    {
        var ore = Market().Named("copper ore")!;
        Assert.Equal("Copper Ore", ore.Name);
        Assert.Equal(20, ore.Stack);
        Assert.Equal(2, ore.Sellers);
        Assert.Equal(1, ore.Buyers);
        Assert.Equal((15L, 20L, 25L, 10L), (ore.Low, ore.Median, ore.High, ore.BestBid));
        Assert.Equal(20L, ore.Headline);
        Assert.Equal(12, ore.Sightings);
        Assert.True(ore.Active);
        Assert.Equal(MarketKind.Materials, ore.Kind);
        Assert.Equal("4sp/stack", MarketMath.Shown(ore.Median!.Value, ore.Stack));
        Assert.Equal("5sp", MarketMath.Shown(500, null));
    }

    [Fact]
    public void A_seller_silent_for_more_than_three_days_no_longer_sets_the_price()
    {
        var sword = Market().Named("Iron Longsword")!;
        Assert.False(sword.Active);
        Assert.Empty(sword.Asks);
        Assert.Null(sword.Headline);
        Assert.Equal(25000L, sword.LatestAsk!.Copper);
        Assert.Equal(MarketKind.Weapons, sword.Kind);
    }

    [Fact]
    public void An_item_only_bid_for_shows_the_bid()
    {
        var bracer = Market().Named("Barnacle Bracer")!;
        Assert.True(bracer.Active);
        Assert.Null(bracer.Median);
        Assert.Equal(40000L, bracer.Headline);
        Assert.Equal(MarketKind.Armor, bracer.Kind);
        Assert.Equal(MarketKind.Bags, Market().Named("Rawhide Backpack")!.Kind);
    }

    [Fact]
    public void The_kind_is_told_from_the_card_or_the_stack()
    {
        Assert.Equal(MarketKind.Other, MarketItem.KindOf(null, null));
        Assert.Equal(MarketKind.Materials, MarketItem.KindOf(null, 20));
        Assert.Equal(MarketKind.Armor, MarketItem.KindOf(new ItemCard { Slot = "Chest", Ac = 8 }, null));
        Assert.Equal(MarketKind.Weapons, MarketItem.KindOf(new ItemCard { Slot = "Secondary", Dmg = 4 }, null));
        Assert.Equal(MarketKind.Materials, MarketItem.KindOf(new ItemCard { Tradeskills = ["Tailoring"] }, null));
        Assert.Equal(MarketKind.Other, MarketItem.KindOf(new ItemCard { Name = "Rations" }, null));
    }

    [Fact]
    public void The_median_is_the_middle_value_or_the_mean_of_the_two_middle_ones()
    {
        Assert.Null(MarketMath.Median([]));
        Assert.Equal(5, MarketMath.Median([9, 5, 1]));
        Assert.Equal(6, MarketMath.Median([9, 5, 1, 7]));
    }

    private static MarketItem WithHistory()
    {
        var t0 = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        var versions = new List<MarketListing>
        {
            new("Trem", "Avelin", "Copper Ore", MarketIntent.Sell, 30, 600, 20, null, t0, t0.AddHours(12), 3, false),             // replaced on the 27th
            new("Trem", "Avelin", "Copper Ore", MarketIntent.Sell, 25, 500, 20, null, t0.AddDays(2), Now, 9, false),           // current
            new("Trem", "Borrik", "Copper Ore", MarketIntent.Sell, 15, 300, 20, null, t0.AddDays(5), Now, 2, false),           // current
            new("Trem", "Cerys", "Copper Ore", MarketIntent.Buy, 10, 200, 20, null, t0.AddDays(5), Now, 1, false),             // current
        };
        var now = versions.Skip(1).ToList();
        return new MarketItem(MarketBook.ItemKey("Trem", "Copper Ore"), "Trem", "Copper Ore", null, 20, now, versions, Now);
    }

    [Fact]
    public void Each_day_has_the_prices_that_stood_on_it()
    {
        var days = MarketMath.Days(WithHistory(), DateOnly.FromDateTime(Now));
        Assert.Equal(7, days.Count);   // 25 Oct to 31 Oct
        Assert.Equal((new DateOnly(2026, 10, 25), 1, 30L, 30L, 30L, 0), (days[0].Day, days[0].Sellers, days[0].Low, days[0].Median, days[0].High, days[0].Buyers));
        Assert.Equal((new DateOnly(2026, 10, 27), 1, 25L), (days[2].Day, days[2].Sellers, days[2].Median));   // the new price from the 27th
        Assert.Equal((new DateOnly(2026, 10, 31), 2, 15L, 20L, 25L, 1, 10L), (days[6].Day, days[6].Sellers, days[6].Low, days[6].Median, days[6].High, days[6].Buyers, days[6].BestBid));
        Assert.DoesNotContain(days, d => d.Day == new DateOnly(2026, 10, 26) && d.Sellers == 0);   // the 25th's price stood only until seen + 6 h, so the 26th has it (it ran into the day) but no gap row
    }

    [Fact]
    public void Changes_list_every_price_set_latest_first_with_what_it_replaced()
    {
        var changes = MarketMath.Changes(WithHistory());
        Assert.Equal(4, changes.Count);
        var moved = Assert.Single(changes.Where(c => c.Previous is not null));
        Assert.Equal(("Avelin", 30L, 25L, true), (moved.Who, moved.Previous, moved.Copper, moved.Current));
        Assert.Equal(1, WithHistory().Changes);
    }

    [Fact]
    public void The_trend_compares_the_middle_ask_now_with_a_week_ago()
    {
        var trend = MarketMath.Trend(WithHistory(), Now, TimeSpan.FromDays(7));
        Assert.NotNull(trend);
        Assert.Equal((30L, 20L), (trend!.Value.Then, trend.Value.Now));
        Assert.Equal(-1 / 3.0, trend.Value.Fraction, 6);
    }

    [Fact]
    public void An_item_whose_every_row_went_to_the_history_is_still_in_the_market()
    {
        var book = new MarketBook();
        book.Merge(MarketFeed.TryParse(MarketFixture.Json)!);
        var key = MarketBook.Key("Trem", "Gwil", "Iron Longsword", MarketIntent.Sell);
        book.History.Add(book.Rows[key]);
        book.Rows.Remove(key);

        var sword = MarketSnapshot.From(book, null, Now).Named("Iron Longsword");

        Assert.NotNull(sword);
        Assert.Empty(sword!.Now);
        Assert.Single(sword.Versions);
        Assert.Null(sword.LatestAsk);
    }
}
