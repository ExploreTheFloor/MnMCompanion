using System.Text.Json;
using MnMCompanion.Community;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion.Tests;

/// <summary>
/// The running counters of schema 2 (kills as looted corpses, coin, items, gathering, sales, trades, by week): they add up to the lists
/// beside them, fall into the right UTC weeks, fold old weeks into one row, and keep the file small however long someone has played.
/// </summary>
public class ContributionCountsTests
{
    private const string Me = "Aldric";
    private const string Id = "0123456789abcdef0123456789abcdef";
    private static readonly DateOnly Made = new(2026, 10, 25);   // a Sunday: its week starts on Monday the 19th

    /// <summary>Noon UTC on a day (noon, so a time zone can't move it into another week in the way a test reads it).</summary>
    private static DateTime Noon(int y, int m, int d) => new(y, m, d, 12, 0, 0, DateTimeKind.Utc);

    private static IEnumerable<LootEvent> Corpse(DateTime at, string mob, string zone, long purse, params string[] items)
    {
        var list = new List<LootEvent>();
        if (purse > 0) list.Add(new LootEvent(at, LootKind.Coin, Me, true, mob, "", 0, purse, 0, zone));
        foreach (var item in items)
        {
            at = at.AddSeconds(1);
            list.Add(new LootEvent(at, LootKind.Item, Me, true, mob, item, 1, 0, 0, zone));
        }
        return list;
    }

    private static Contribution Make(IEnumerable<LootEvent>? loot = null, IEnumerable<VendorRow>? vendor = null, IEnumerable<HarvestRow>? harvest = null,
        IEnumerable<TradeRow>? trades = null, DateOnly? made = null)
        => ContributionBuilder.Build(loot ?? [], vendor ?? [], harvest ?? [], [], Id, "MnMCompanion 0.3.0", made ?? Made, null, trades);

    private static TradeRow Trade(DateTime at, bool gave, string partner = "ref_71358") => new(at, Me, gave, partner, 0, [new TradeLine("Rat Ear", 1)]);

    [Fact]
    public void The_weeks_add_up_to_the_lists_beside_them()
    {
        var loot = new List<LootEvent>();
        loot.AddRange(Corpse(Noon(2026, 10, 7), "a rat", "z1", 5, "Rat Ear", "Rat Tooth"));       // Wednesday of the week of the 5th
        loot.AddRange(Corpse(Noon(2026, 10, 14), "a rat", "z1", 0, "Rat Ear"));                     // the week of the 12th
        loot.AddRange(Corpse(Noon(2026, 10, 21), "a bandit", "z2", 40, "Cloth Scraps", "Cloth Scraps"));
        var vendor = new[]
        {
            new VendorRow(Noon(2026, 10, 7), Me, VendorDirection.Sold, "Rat Ear", 2, 8, "z1"),
            new VendorRow(Noon(2026, 10, 22), Me, VendorDirection.Sold, "Cloth Scraps", 1, 9, "z2"),
            new VendorRow(Noon(2026, 10, 22), Me, VendorDirection.Bought, "Bread", 1, 12, "z2"),
        };
        var harvest = new[] { new HarvestRow(Noon(2026, 10, 14), Me, "Wood", 3, "z1"), new HarvestRow(Noon(2026, 10, 15), Me, "Wood", 2, "z1") };

        var c = Make(loot, vendor, harvest);
        var all = ContributionBuilder.Lifetime(c.Counts);

        Assert.Equal(c.Totals.Corpses, all.Corpses);
        Assert.Equal(c.Drops.Sum(d => d.Purses), all.Purses);
        Assert.Equal(c.Drops.Sum(d => d.Copper), all.Copper);
        Assert.Equal(c.Drops.Sum(d => d.Items.Sum(i => i.Units)), all.Units);
        Assert.Equal(c.Totals.Harvests, all.Harvests);
        Assert.Equal(c.Harvest.Sum(h => h.Units), all.HarvestUnits);
        Assert.Equal(c.Totals.Sales, all.Sold);
        Assert.Equal(c.Totals.Purchases, all.Bought);
        Assert.Equal((3L, 2L, 45L, 5L, 2L, 5L), (all.Corpses, all.Purses, all.Copper, all.Units, all.Harvests, all.HarvestUnits));
        Assert.Equal((2L, 17L, 1L, 12L), (all.Sold, all.SoldCopper, all.Bought, all.BoughtCopper));
    }

    [Fact]
    public void Each_row_is_the_monday_that_starts_its_week_and_they_run_oldest_first()
    {
        var loot = new List<LootEvent>();
        loot.AddRange(Corpse(Noon(2026, 10, 25), "a rat", "z1", 0, "Rat Ear"));   // Sunday: the week of the 19th
        loot.AddRange(Corpse(Noon(2026, 10, 19), "a rat", "z1", 0, "Rat Ear"));   // Monday: the same week
        loot.AddRange(Corpse(Noon(2026, 10, 18), "a rat", "z1", 0, "Rat Ear"));   // the Sunday before: the week of the 12th
        loot.AddRange(Corpse(Noon(2026, 9, 2), "a rat", "z1", 0, "Rat Ear"));

        var c = Make(loot);

        Assert.Equal(["2026-08-31", "2026-10-12", "2026-10-19"], c.Counts.Weeks.Select(w => w.Week));
        Assert.Equal([1L, 1L, 2L], c.Counts.Weeks.Select(w => w.Corpses));
        Assert.All(c.Counts.Weeks, w => Assert.Equal(DayOfWeek.Monday, DateOnly.ParseExact(w.Week!, "yyyy-MM-dd").DayOfWeek));
        Assert.Equal(0, c.Counts.Before.Corpses);
    }

    [Fact]
    public void A_week_is_cut_at_midnight_UTC_whatever_the_players_clock_says()
    {
        // 23:30 UTC on a Sunday is in the old week, 00:30 UTC on the Monday in the new one, in any time zone
        var c = Make(Corpse(new DateTime(2026, 10, 18, 23, 30, 0, DateTimeKind.Utc), "a rat", "z1", 0, "Rat Ear")
            .Concat(Corpse(new DateTime(2026, 10, 19, 0, 30, 0, DateTimeKind.Utc), "a rat", "z1", 0, "Rat Ear")));

        Assert.Equal(["2026-10-12", "2026-10-19"], c.Counts.Weeks.Select(w => w.Week));
    }

    [Fact]
    public void Weeks_older_than_the_last_fifty_three_are_folded_into_before()
    {
        var loot = new List<LootEvent>();
        loot.AddRange(Corpse(Noon(2024, 3, 6), "a rat", "z1", 7, "Rat Ear"));
        loot.AddRange(Corpse(Noon(2025, 1, 15), "a rat", "z1", 0, "Rat Ear"));
        loot.AddRange(Corpse(Noon(2025, 10, 22), "a rat", "z1", 0, "Rat Ear"));   // the week of 2025-10-20: the oldest still kept (this week, the 19th, minus 52)
        loot.AddRange(Corpse(Noon(2025, 10, 15), "a rat", "z1", 0, "Rat Ear"));   // the week before it: folded
        loot.AddRange(Corpse(Noon(2026, 10, 21), "a rat", "z1", 0, "Rat Ear"));

        var c = Make(loot);

        Assert.Equal(3, c.Counts.Before.Corpses);
        Assert.Null(c.Counts.Before.Week);
        Assert.Equal(1, c.Counts.Before.Purses);
        Assert.Equal(7, c.Counts.Before.Copper);
        Assert.Equal(["2025-10-20", "2026-10-19"], c.Counts.Weeks.Select(w => w.Week));
        Assert.Equal(5, ContributionBuilder.Lifetime(c.Counts).Corpses);
    }

    [Fact]
    public void A_year_of_every_week_is_at_most_fifty_three_rows_and_a_small_file()
    {
        var loot = new List<LootEvent>();
        var vendor = new List<VendorRow>();
        var harvest = new List<HarvestRow>();
        var trades = new List<TradeRow>();
        for (int week = 0; week < 80; week++)
        {
            var day = Noon(2026, 10, 21).AddDays(-7 * week);
            for (int i = 0; i < 400; i++) loot.AddRange(Corpse(day.AddMinutes(i), "a rat", "z1", 120, "Rat Ear"));
            vendor.Add(new VendorRow(day, Me, VendorDirection.Sold, "Rat Ear", 1, 40000, "z1"));
            harvest.Add(new HarvestRow(day, Me, "Wood", 4, "z1"));
            trades.Add(Trade(day, true, "ref_" + week));
        }

        var c = Make(loot, vendor, harvest, trades);

        Assert.Equal(53, c.Counts.Weeks.Count);
        Assert.Equal(27 * 400, c.Counts.Before.Corpses);
        Assert.Equal(27, c.Counts.Before.Trades);
        var counts = JsonSerializer.Serialize(c.Counts, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
        Assert.True(counts.Length < 25_000, $"the counters take {counts.Length} bytes");
    }

    [Fact]
    public void A_trade_is_counted_once_however_many_sides_it_has()
    {
        var at = Noon(2026, 10, 14);
        var c = Make(trades: [Trade(at, true), Trade(at.AddSeconds(2), false), Trade(at.AddHours(1), true, "ref_5"), Trade(at.AddDays(8), true)]);

        Assert.Equal(3, ContributionBuilder.Lifetime(c.Counts).Trades);
        Assert.Equal([2L, 1L], c.Counts.Weeks.Select(w => w.Trades));
    }

    [Fact]
    public void A_week_with_only_a_trade_is_kept_and_an_empty_ledger_makes_empty_counters()
    {
        var c = Make(trades: [Trade(Noon(2026, 10, 21), true)]);
        var only = Assert.Single(c.Counts.Weeks);
        Assert.Equal((0L, 1L), (only.Corpses, only.Trades));
        Assert.False(only.IsEmpty);

        var none = Make();
        Assert.Empty(none.Counts.Weeks);
        Assert.True(none.Counts.Before.IsEmpty);
    }

    [Fact]
    public void The_counters_leave_out_what_the_lists_leave_out()
    {
        // a player's corpse is not a monster; a vendor stack with no coin and a harvest with no item name are left out of the lists, so out of the counters too
        var loot = new List<LootEvent>();
        loot.AddRange(Corpse(Noon(2026, 10, 21), "your corpse", "z1", 12, "Worn Bow"));
        loot.AddRange(Corpse(Noon(2026, 10, 21), "Hobart's corpse", "z1", 0, "Worn Bow"));
        loot.AddRange(Corpse(Noon(2026, 10, 21), "a rat", "z1", 3, "Rat Ear"));
        var vendor = new[] { new VendorRow(Noon(2026, 10, 21), Me, VendorDirection.Sold, "Rat Ear", 1, 0, "z1") };
        var harvest = new[] { new HarvestRow(Noon(2026, 10, 21), Me, " ", 1, "z1") };

        var all = ContributionBuilder.Lifetime(Make(loot, vendor, harvest).Counts);

        Assert.Equal((1L, 1L, 3L, 1L), (all.Corpses, all.Purses, all.Copper, all.Units));
        Assert.Equal((0L, 0L, 0L), (all.Sold, all.SoldCopper, all.Harvests));
    }

    [Fact]
    public void The_file_has_the_counters_in_the_shape_the_service_reads()
    {
        var loot = Corpse(Noon(2026, 10, 21), "a rat", "z1", 5, "Rat Ear");
        var json = Make(loot, [new VendorRow(Noon(2026, 10, 21), Me, VendorDirection.Sold, "Rat Ear", 1, 4, "z1")]).ToJson();

        using var doc = JsonDocument.Parse(json);
        var counts = doc.RootElement.GetProperty("counts");
        string[] metrics = ["corpses", "purses", "copper", "units", "harvests", "harvestUnits", "sold", "soldCopper", "bought", "boughtCopper", "trades"];
        Assert.Equal(["before", "weeks"], counts.EnumerateObject().Select(p => p.Name));
        Assert.Equal(metrics, counts.GetProperty("before").EnumerateObject().Select(p => p.Name));   // no "week" in the row for everything older
        var week = Assert.Single(counts.GetProperty("weeks").EnumerateArray());
        Assert.Equal(["week", .. metrics], week.EnumerateObject().Select(p => p.Name));
        Assert.Equal("2026-10-19", week.GetProperty("week").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("schema").GetInt32());
    }

    [Fact]
    public void The_counters_read_back_as_they_were_written()
    {
        var made = Make(Corpse(Noon(2026, 10, 21), "a rat", "z1", 5, "Rat Ear"), trades: [Trade(Noon(2026, 10, 22), true)]);

        var back = Contribution.FromJson(made.ToJson())!;

        Assert.Equal(made.Counts.Before, back.Counts.Before);
        Assert.Equal(made.Counts.Weeks, back.Counts.Weeks);
    }

    [Fact]
    public void The_fingerprint_does_not_change_just_because_a_new_day_began()
    {
        var loot = Corpse(Noon(2026, 10, 21), "a rat", "z1", 5, "Rat Ear");
        Assert.Equal(SharePlan.Fingerprint(Make(loot)), SharePlan.Fingerprint(Make(loot, made: Made.AddDays(1))));
        Assert.NotEqual(SharePlan.Fingerprint(Make(loot)), SharePlan.Fingerprint(Make(loot, trades: [Trade(Noon(2026, 10, 22), true)])));
    }
}
