using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion.Tests;

/// <summary>The numbers behind the History pages: vendor prices, gathering rates, item fates, trades, periods and drop rates.</summary>
public class HistoryTests
{
    private static readonly DateTime T = new(2026, 10, 24, 20, 0, 0);

    private static VendorRow Sale(int min, string item, int qty, long total, string who = "Aldric") => new(T.AddMinutes(min), who, VendorDirection.Sold, item, qty, total);
    private static VendorRow Buy(int min, string item, int qty, long total, string who = "Aldric") => new(T.AddMinutes(min), who, VendorDirection.Bought, item, qty, total);

    // ------------------------------------------------------------------ periods

    [Fact]
    public void The_quick_periods_cover_today_yesterday_a_week_a_month_and_everything()
    {
        var now = new DateTime(2026, 10, 24, 15, 30, 0);
        var presets = Period.Presets(now);

        Assert.Equal(["Today", "Yesterday", "7 days", "30 days", "All time"], presets.Select(p => p.Label));
        Assert.True(presets[0].Contains(now));
        Assert.False(presets[0].Contains(now.Date.AddSeconds(-1)));
        Assert.True(presets[1].Contains(now.Date.AddHours(-1)));
        Assert.False(presets[1].Contains(now));
        Assert.True(presets[2].Contains(now.Date.AddDays(-6)));
        Assert.False(presets[2].Contains(now.Date.AddDays(-7)));
        Assert.True(presets[4].Contains(DateTime.MinValue.AddDays(1)));
        Assert.False(presets[0].Contains(now.Date.AddDays(1)));   // the end is exclusive
    }

    [Fact]
    public void A_picked_day_is_named_by_how_far_back_it_is()
    {
        var today = new DateOnly(2026, 10, 24);
        Assert.Equal("Today", Period.Of(today, today).Label);
        Assert.Equal("Yesterday", Period.Of(today.AddDays(-1), today).Label);
        Assert.Equal("Tue, Oct 20", Period.Of(new DateOnly(2026, 10, 20), today).Label);
    }

    // ------------------------------------------------------------------ vendors

    [Fact]
    public void Vendor_totals_split_what_came_in_from_what_went_out_and_name_the_best_seller()
    {
        var rows = new[] { Sale(0, "Rat Ear", 6, 24), Sale(1, "Rat Ear", 4, 16), Sale(2, "Iron Axe", 1, 271), Buy(3, "Water Flask", 20, 100) };

        var t = VendorSummary.Totals(rows);

        Assert.Equal(311, t.SoldCopper);
        Assert.Equal(11, t.SoldUnits);
        Assert.Equal(3, t.SoldStacks);
        Assert.Equal(100, t.BoughtCopper);
        Assert.Equal(20, t.BoughtUnits);
        Assert.Equal(211, t.Net);
        Assert.Equal("Iron Axe", t.BestSeller);
        Assert.Equal(271, t.BestSellerCopper);
    }

    [Fact]
    public void Nothing_sold_is_a_clean_zero()
    {
        var t = VendorSummary.Totals([]);
        Assert.Equal(0, t.Net);
        Assert.Equal("", t.BestSeller);
    }

    [Fact]
    public void Price_each_is_the_stacks_coin_over_its_count_as_the_item_values_work_it_out()
    {
        Assert.Equal(4, Sale(0, "Rat Ear", 6, 24).Each);
        Assert.Equal(3, Sale(0, "Odd", 3, 10).Each);     // whole coins, rounded down, as the valuation does
        Assert.Equal(0, Sale(0, "Free", 0, 0).Each);
    }

    [Fact]
    public void The_price_book_gives_each_item_its_middle_price_spread_and_what_it_cost_to_buy()
    {
        var rows = new[]
        {
            Sale(0, "Rat Ear", 6, 24), Sale(1, "Rat Ear", 1, 5), Sale(2, "Rat Ear", 2, 8), Sale(3, "rat ear", 1, 4),
            Buy(4, "Rat Ear", 1, 16),
            Sale(5, "Iron Axe", 1, 271), Sale(6, "Odd Thing", 1, 0),   // no coin read: no price
            Buy(7, "Water Flask", 20, 100),
        };

        var book = VendorSummary.PriceBook(rows);

        var ear = book.Single(p => p.Item.Equals("Rat Ear", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, ear.Samples);              // four sales; the spelling doesn't split them
        Assert.Equal(4, ear.Median);               // 4, 5, 4, 4 -> sorted 4 4 4 5, upper middle
        Assert.Equal(4, ear.Min);
        Assert.Equal(5, ear.Max);
        Assert.Equal(10, ear.Units);
        Assert.Equal(1, ear.BoughtSamples);
        Assert.Equal(16, ear.BoughtMedian);
        Assert.DoesNotContain(book, p => p.Item == "Odd Thing");
        Assert.Equal(0, book.Single(p => p.Item == "Water Flask").Samples);   // only ever bought
        Assert.Equal("Iron Axe", book[0].Item);     // the best price first
    }

    [Fact]
    public void Items_are_totalled_per_direction_most_coin_first()
    {
        var rows = new[] { Sale(0, "Rat Ear", 6, 24), Sale(1, "Rat Ear", 4, 16), Sale(2, "Iron Axe", 1, 271), Buy(3, "Rat Ear", 1, 99) };

        var sold = VendorSummary.ByItem(rows, VendorDirection.Sold);

        Assert.Equal(["Iron Axe", "Rat Ear"], sold.Select(i => i.Item));
        Assert.Equal(2, sold[1].Stacks);
        Assert.Equal(10, sold[1].Units);
        Assert.Equal(40, sold[1].Total);
        Assert.Equal(4, sold[1].Each);
        Assert.Single(VendorSummary.ByItem(rows, VendorDirection.Bought));
    }

    // ------------------------------------------------------------------ harvest

    private static HarvestRow Gather(int sec, string item = "Wood", int qty = 1, string zone = "nightharborw") => new(T.AddSeconds(sec), "Aldric", item, qty, zone);

    [Fact]
    public void Gathering_time_counts_the_gaps_between_harvests_and_leaves_out_breaks()
    {
        var rows = new[] { Gather(0), Gather(60), Gather(180), Gather(180 + 3600), Gather(180 + 3600 + 120) };   // an hour's break in the middle

        Assert.Equal(TimeSpan.FromSeconds(60 + 120 + 120), HarvestSummary.Active(rows));
    }

    [Fact]
    public void Units_per_hour_wait_for_a_minute_of_gathering_and_then_divide_by_it()
    {
        Assert.Null(HarvestSummary.Totals([Gather(0), Gather(30)]).PerHour);
        Assert.Null(HarvestSummary.Totals([Gather(0)]).PerHour);

        var rows = Enumerable.Range(0, 11).Select(i => Gather(i * 360, qty: 2)).ToList();   // eleven harvests six minutes apart: an hour
        var totals = HarvestSummary.Totals(rows);

        Assert.Equal(11, totals.Harvests);
        Assert.Equal(22, totals.Units);
        Assert.Equal(1, totals.Kinds);
        Assert.Equal(22.0, totals.PerHour!.Value, 6);
    }

    [Fact]
    public void Gathered_items_are_ranked_by_units_with_their_zones_busiest_first()
    {
        var rows = new[]
        {
            Gather(0, "Wood", 1, "nightharborw"), Gather(10, "Wood", 1, "fallenpass"), Gather(20, "Wood", 1, "fallenpass"),
            Gather(30, "Copper Ore", 5, "nightharborw"), Gather(40, "wood", 1, ""),
        };

        var items = HarvestSummary.ByItem(rows);

        Assert.Equal(["Copper Ore", "Wood"], items.Select(i => i.Item));
        Assert.Equal(5, items[0].Units);
        Assert.Equal(4, items[1].Harvests);
        Assert.Equal(["fallenpass", "nightharborw"], items[1].Zones);
    }

    // ------------------------------------------------------------------ item changes

    private static ItemMoveRow Move(int min, ItemMoveKind kind, string item, int qty = 1, string who = "Aldric") => new(T.AddMinutes(min), who, kind, item, qty);

    [Fact]
    public void A_pickup_soon_after_dropping_the_same_item_is_taking_it_back()
    {
        var rows = new[]
        {
            Move(0, ItemMoveKind.Dropped, "Spear"), Move(5, ItemMoveKind.PickedUp, "Spear"),                 // taken back
            Move(10, ItemMoveKind.Dropped, "Bow"), Move(40, ItemMoveKind.PickedUp, "Bow"),                   // too late
            Move(50, ItemMoveKind.Dropped, "Axe"), Move(52, ItemMoveKind.PickedUp, "Axe", who: "Wendell"),   // someone else's
            Move(60, ItemMoveKind.PickedUp, "Root"),                                                          // never dropped
            Move(61, ItemMoveKind.Destroyed, "Residue", 3),
        };

        var t = ItemChangeSummary.Totals(rows);

        Assert.Equal(3, t.Dropped);
        Assert.Equal(4, t.PickedUp);
        Assert.Equal(3, t.Destroyed);
        Assert.Equal(1, t.TakenBack);
    }

    [Fact]
    public void One_drop_can_only_be_taken_back_once()
    {
        var rows = new[] { Move(0, ItemMoveKind.Dropped, "Meat", 2), Move(1, ItemMoveKind.PickedUp, "Meat"), Move(2, ItemMoveKind.PickedUp, "Meat"), Move(3, ItemMoveKind.PickedUp, "Meat") };
        Assert.Equal(2, ItemChangeSummary.Totals(rows).TakenBack);
    }

    [Fact]
    public void The_most_discarded_items_are_those_lost_for_good()
    {
        var rows = new[]
        {
            Move(0, ItemMoveKind.Dropped, "Meat", 24), Move(1, ItemMoveKind.PickedUp, "Meat", 4),
            Move(2, ItemMoveKind.Destroyed, "Residue", 10), Move(3, ItemMoveKind.Dropped, "Spear", 2), Move(4, ItemMoveKind.PickedUp, "Spear", 2),
        };

        var list = ItemChangeSummary.MostDiscarded(rows);

        Assert.Equal(["Meat", "Residue", "Spear"], list.Select(i => i.Item));
        Assert.Equal(20, list[0].Lost);
        Assert.Equal(10, list[1].Lost);
        Assert.Equal(0, list[2].Lost);
    }

    // ------------------------------------------------------------------ trades

    private static TradeRow Side(int sec, bool gave, string partner, long coin, params (string, int)[] items) =>
        new(T.AddSeconds(sec), "Aldric", gave, partner, coin, items.Select(i => new TradeLine(i.Item1, i.Item2)).ToList());

    [Fact]
    public void The_two_rows_of_a_trade_become_one_entry_with_both_sides()
    {
        var rows = new[]
        {
            Side(0, gave: true, "ref_20917", 700),                                      // gave 7sp ...
            Side(0, gave: false, "ref_20917", 0, ("Rawhide Backpack", 1), ("Rawhide Backpack", 1)),   // ... got two backpacks
            Side(600, gave: true, "ref_20917", 0, ("Spear", 1)),                        // a separate trade, gave a spear for 4sp
            Side(601, gave: false, "ref_20917", 400),
            Side(1000, gave: true, "ref_99999", 24),                                    // another partner
        };

        var trades = TradeSummary.Pair(rows);

        Assert.Equal(3, trades.Count);
        Assert.Equal(T.AddSeconds(1000), trades[0].Time);                               // newest first
        var first = trades.Single(t => t.Time == T);
        Assert.Equal(700, first.GaveCoin);
        Assert.Equal(2, first.ReceivedCount);
        Assert.Equal([new TradeLine("Rawhide Backpack", 2)], first.ReceivedItems);
        var second = trades.Single(t => t.Time == T.AddSeconds(600));
        Assert.Equal(400, second.ReceivedCoin);
        Assert.Equal(1, second.GaveCount);
    }

    [Fact]
    public void A_trade_is_told_in_words_and_the_partner_is_an_id_not_a_name()
    {
        Assert.Equal("7sp + Rawhide Backpack ×2", TradeSummary.Describe(700, [new TradeLine("Rawhide Backpack", 2)]));
        Assert.Equal("Spear", TradeSummary.Describe(0, [new TradeLine("Spear", 1)]));
        Assert.Equal("nothing", TradeSummary.Describe(0, []));
        Assert.Equal("Trader #71358", TradeSummary.Label("ref_71358"));
        Assert.Equal("Unknown trader", TradeSummary.Label(""));
    }

    // ------------------------------------------------------------------ drop rates

    private static IEnumerable<LootEvent> Corpse(ref DateTime at, string who, string mob, string zone, long purse, params string[] items)
    {
        var list = new List<LootEvent>();
        if (purse > 0) list.Add(new LootEvent(at, LootKind.Coin, who, false, mob, "", 0, purse, 0, zone));
        foreach (var item in items)
        {
            at = at.AddSeconds(1);
            list.Add(new LootEvent(at, LootKind.Item, who, false, mob, item, 1, 0, 0, zone));
        }
        at = at.AddSeconds(60);   // the next corpse is a minute on
        return list;
    }

    [Fact]
    public void An_items_share_is_the_corpses_it_came_off_over_the_corpses_looted_with_a_range()
    {
        var at = T;
        var events = new List<LootEvent>();
        for (int i = 0; i < 40; i++)
            events.AddRange(Corpse(ref at, "Brom", "a rat", "nightharborw", i % 2 == 0 ? 10 : 0,
                i % 4 == 0 ? ["Rat Tooth", "Rat Ear"] : ["Rat Ear"]));   // Ear on every corpse, Tooth on a quarter

        var mobs = DropRates.ByMob(events);

        var rat = Assert.Single(mobs);
        Assert.Equal(40, rat.Corpses);
        Assert.False(rat.Few);
        Assert.Equal(20, rat.WithPurse);
        Assert.Equal(200, rat.Copper);
        Assert.Equal("nightharborw", rat.Zone);
        var ear = rat.Drops.Single(d => d.Item == "Rat Ear");
        var tooth = rat.Drops.Single(d => d.Item == "Rat Tooth");
        Assert.Equal(1.0, ear.Share);
        Assert.Equal(0.25, tooth.Share);
        Assert.Equal(10, tooth.Corpses);
        Assert.Equal("Rat Ear", rat.Drops[0].Item);            // the likeliest first
        Assert.InRange(tooth.Low, 0.13, 0.15);                 // Wilson 95% for 10 of 40
        Assert.InRange(tooth.High, 0.40, 0.42);
        Assert.Equal(1.25, rat.ItemsEach, 6);
        Assert.Equal(5, rat.CopperEach, 6);
    }

    [Fact]
    public void Few_corpses_are_flagged_and_zones_and_players_corpses_are_kept_apart()
    {
        var at = T;
        var events = new List<LootEvent>();
        for (int i = 0; i < 3; i++) events.AddRange(Corpse(ref at, "Brom", "a bandit", "shadeddunes", 5, "Cloth Scraps"));
        for (int i = 0; i < 25; i++) events.AddRange(Corpse(ref at, "Brom", "a bandit", "fallenpass", 5, "Worn Bow"));
        events.AddRange(Corpse(ref at, "Brom", "Hobart's corpse", "fallenpass", 0, "Worn Bow"));   // a player's corpse is not a monster

        var mobs = DropRates.ByMob(events);

        Assert.Equal(2, mobs.Count);
        Assert.Equal(["fallenpass", "shadeddunes"], mobs.Select(m => m.Zone));   // the bigger sample first
        Assert.False(mobs[0].Few);
        Assert.True(mobs[1].Few);
        Assert.DoesNotContain(mobs, m => m.Mob.Contains("corpse"));
    }

    [Fact]
    public void The_same_figures_turn_around_to_show_where_an_item_comes_from()
    {
        var at = T;
        var events = new List<LootEvent>();
        for (int i = 0; i < 20; i++) events.AddRange(Corpse(ref at, "Brom", "a rat", "z1", 0, "Fur", "Tooth"));
        for (int i = 0; i < 20; i++) events.AddRange(Corpse(ref at, "Brom", "a bat", "z2", 0, i < 5 ? ["Fur"] : ["Wing"]));

        var items = DropRates.ByItem(DropRates.ByMob(events));

        var fur = items.Single(i => i.Item == "Fur");
        Assert.Equal(25, fur.Corpses);
        Assert.Equal(["a rat", "a bat"], fur.Sources.Select(s => s.Mob));      // 100% beats 25%
        Assert.Equal(1.0, fur.Sources[0].Share);
        Assert.Equal(0.25, fur.Sources[1].Share);
        Assert.Equal(20, fur.Sources[1].OfCorpses);
        Assert.Equal("Fur", items[0].Item);                                    // the most corpses first
    }

    [Fact]
    public void The_range_is_wide_for_few_corpses_and_never_leaves_zero_to_one()
    {
        Assert.Equal((0.0, 0.0), DropRates.Wilson(0, 0));
        var (low, high) = DropRates.Wilson(1, 2);
        Assert.InRange(low, 0.0, 0.1);
        Assert.InRange(high, 0.9, 1.0);
        var (l2, h2) = DropRates.Wilson(50, 100);
        Assert.InRange(l2, 0.39, 0.41);
        Assert.InRange(h2, 0.59, 0.61);
        Assert.Equal(1.0, DropRates.Wilson(20, 20).High, 6);
        Assert.Equal(0.0, DropRates.Wilson(0, 20).Low, 6);
    }
}

/// <summary>The period and filter the History pages share.</summary>
public class HistoryViewTests
{
    [Fact]
    public void It_starts_on_today_with_no_filter_and_matches_everything()
    {
        var view = new HistoryView(new DateTime(2026, 10, 24, 15, 0, 0));

        Assert.Equal("Today", view.Period.Label);
        Assert.Equal("", view.Filter);
        Assert.True(view.Matches("anything"));
        Assert.True(view.Matches());
    }

    [Fact]
    public void A_filter_matches_any_field_ignoring_case_and_is_trimmed()
    {
        var view = new HistoryView();
        view.SetFilter("  rawhide ");

        Assert.Equal("rawhide", view.Filter);
        Assert.True(view.Matches("Spear", "Rawhide Backpack"));
        Assert.False(view.Matches("Spear", "Wood"));
    }

    [Fact]
    public void It_tells_its_pages_only_when_something_really_changed()
    {
        var now = new DateTime(2026, 10, 24, 15, 0, 0);
        var view = new HistoryView(now);
        int changes = 0;
        view.Changed += () => changes++;

        view.Set(Period.Presets(now)[0]);        // already on it
        view.SetFilter("   ");                   // still no filter
        Assert.Equal(0, changes);

        view.Set(Period.Presets(now)[2]);
        view.SetFilter("wood");
        view.SetFilter("wood");
        Assert.Equal(2, changes);
        Assert.Equal("7 days", view.Period.Label);
    }
}
