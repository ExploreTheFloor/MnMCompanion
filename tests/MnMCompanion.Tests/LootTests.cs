using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using static MnMCompanion.Tests.Fixture;

namespace MnMCompanion.Tests;

public class LootExtractorTests
{
    private static readonly DateTime T = new(2026, 10, 24, 9, 0, 0);

    private static List<LootEvent> Extract(string json, LedgerFileKind kind) => LootExtractor.Extract(Parse(json, kind));

    [Fact]
    public void An_item_row_is_a_loot_event_with_its_creature_looter_class_and_level()
    {
        var e = Assert.Single(Extract(File(Item(T, "Brom", "Aldric", "a Dustrend legionary", "Rusty Iron Axe", "clr", 34)), LedgerFileKind.Social));
        Assert.Equal(LootKind.Item, e.Kind);
        Assert.Equal("Brom", e.Who);
        Assert.False(e.You);
        Assert.Equal("a Dustrend legionary", e.Source);
        Assert.Equal("Rusty Iron Axe", e.Item);
        Assert.Equal(1, e.Count);
        Assert.Equal("clr", e.Class);
        Assert.Equal(34, e.Level);
        Assert.Equal("9a818e77c3484fe98efd9081", e.ItemId);
    }

    [Fact]
    public void A_purse_counts_a_hundred_copper_to_a_silver_and_carries_the_owners_share()
    {
        var e = Assert.Single(Extract(File(Purse(T, "Brom", "Aldric", "a Dustrend legionary", "0,0,1,44", split: "0,0,0,7")), LedgerFileKind.Social));
        Assert.Equal(LootKind.Coin, e.Kind);
        Assert.Equal(144, e.Copper);
        Assert.Equal(7, e.Split);
        Assert.Equal("a Dustrend legionary", e.Source);
    }

    [Fact]
    public void A_purse_with_no_coin_and_rows_with_no_looter_are_not_loot()
    {
        Assert.Empty(Extract(File(Purse(T, "Brom", "Aldric", "a rat", "0,0,0,0")), LedgerFileKind.Social));
        Assert.Empty(Extract(File(Item(T, "", "Aldric", "a rat", "Rat Ear")), LedgerFileKind.Social));
    }

    [Fact]
    public void Other_acts_are_not_loot()
    {
        Assert.Empty(Extract(File(Sale(T, "Aldric", "Rat Ear", 1, "0 platinum 0 gold 0 silver 3 copper")), LedgerFileKind.Character));
    }

    [Fact]
    public void The_owners_own_loot_comes_from_the_character_file_and_its_social_twin_is_not_counted_twice()
    {
        var own = Item(T, "Aldric", "Aldric", "a rat", "Rat Ear");
        var twin = Item(T.AddMilliseconds(400), "Aldric", "Aldric", "a rat", "Rat Ear");
        var rows = Parse(File(own), LedgerFileKind.Character).Concat(Parse(File(twin), LedgerFileKind.Social));
        var e = Assert.Single(LootExtractor.Extract(rows));
        Assert.True(e.You);
    }

    [Fact]
    public void Two_real_loots_of_one_item_in_the_character_file_both_count_and_a_social_only_row_is_kept()
    {
        var character = File(Item(T, "Aldric", "Aldric", "a rat", "Rat Ear"), Item(T.AddSeconds(30), "Aldric", "Aldric", "a rat", "Rat Ear"));
        // The social file lacks the first (a day with no character file for it) and has the second as a twin
        var social = File(Item(T.AddMinutes(10), "Aldric", "Aldric", "a rat", "Rat Tail"), Item(T.AddSeconds(30.5), "Aldric", "Aldric", "a rat", "Rat Ear"));
        var rows = Parse(character, LedgerFileKind.Character).Concat(Parse(social, LedgerFileKind.Social));
        var events = LootExtractor.Extract(rows);
        Assert.Equal(3, events.Count);
        Assert.Equal(2, events.Count(e => e.Item == "Rat Ear"));
        Assert.Single(events, e => e.Item == "Rat Tail");
    }

    [Fact]
    public void Other_players_loot_is_taken_from_the_social_file_only()
    {
        var events = Extract(File(Item(T, "Brom", "Aldric", "a rat", "Rat Ear"), Item(T.AddSeconds(1), "Doc", "Aldric", "a rat", "Rat Tail")), LedgerFileKind.Social);
        Assert.Equal(["Brom", "Doc"], events.Select(e => e.Who));
        Assert.All(events, e => Assert.False(e.You));
    }
}

public class CorpseTests
{
    private static readonly DateTime T = new(2026, 10, 24, 9, 0, 0);

    private static LootEvent Item(double seconds, string who, string mob, string item) =>
        new(T.AddSeconds(seconds), LootKind.Item, who, false, mob, item, 1, 0, 0);

    private static LootEvent Coin(double seconds, string who, string mob, long copper, long split = 0) =>
        new(T.AddSeconds(seconds), LootKind.Coin, who, false, mob, "", 0, copper, split);

    [Fact]
    public void A_purse_and_the_items_a_second_apart_are_one_corpse()
    {
        var c = Assert.Single(LootSummary.Corpses([Coin(0, "Brom", "a rat", 144), Item(1, "Brom", "a rat", "Rat Ear"), Item(2, "Brom", "a rat", "Rat Tail")]));
        Assert.Equal(2, c.Items);
        Assert.Equal(144, c.Copper);
        Assert.True(c.Mob);
    }

    [Fact]
    public void A_second_purse_or_a_long_gap_starts_another_corpse_and_looters_are_kept_apart()
    {
        var corpses = LootSummary.Corpses([
            Coin(0, "Brom", "a rat", 10), Coin(2, "Brom", "a rat", 20),
            Item(30, "Brom", "a rat", "Rat Ear"),
            Coin(1, "Doc", "a rat", 5)]);
        Assert.Equal(4, corpses.Count);
        Assert.Equal(3, corpses.Count(c => c.Who == "Brom"));
    }

    [Fact]
    public void Player_corpses_are_not_mobs()
    {
        Assert.False(LootSummary.IsMob("your corpse"));
        Assert.False(LootSummary.IsMob("Brom's corpse"));
        Assert.True(LootSummary.IsMob("a Dustrend legionary"));
    }

    [Fact]
    public void Coin_is_written_in_platinum_gold_silver_and_copper()
    {
        Assert.Equal("0cp", LootSummary.Coin(0));
        Assert.Equal("1sp 44cp", LootSummary.Coin(144));
        Assert.Equal("1pp 2gp 3sp 4cp", LootSummary.Coin(1_020_304));
        Assert.Equal("~1sp+", LootSummary.Worth(100, true, true));
    }
}

public class ItemValuesTests
{
    private static readonly DateTime T = new(2026, 10, 24, 9, 0, 0);

    [Fact]
    public void A_sold_item_is_worth_the_median_of_what_one_fetched()
    {
        var rows = Parse(File(
            Sale(T, "Aldric", "Rat Ear", 1, "0 platinum 0 gold 0 silver 3 copper"),
            Sale(T, "Aldric", "Rat Ear", 4, "0 platinum 0 gold 0 silver 20 copper"),      // 5 each
            Sale(T, "Aldric", "Rat Ear", 1, "0 platinum 0 gold 0 silver 4 copper")));
        var v = ItemValues.FromLedger(rows).Of("rat ear");
        Assert.Equal(new ItemValue(4, ValueSource.Sold), v);
        Assert.False(v.Estimated);
    }

    [Fact]
    public void An_unsold_item_takes_its_familys_median_and_is_marked_estimated()
    {
        var values = ItemValues.FromLedger(Parse(File(
            Sale(T, "Aldric", "Corroded Bronze Chain Gloves", 1, "0 platinum 0 gold 0 silver 135 copper"),
            Sale(T, "Aldric", "Corroded Bronze Chain Mask", 1, "0 platinum 0 gold 0 silver 135 copper"),
            Sale(T, "Aldric", "Corroded Bronze Chain Boots", 1, "0 platinum 0 gold 0 silver 135 copper"))));
        var v = values.Of("Corroded Bronze Chain Shoulderguards");
        Assert.Equal(135, v.Each);
        Assert.Equal(ValueSource.Estimated, v.Source);
        Assert.True(v.Estimated);
    }

    [Fact]
    public void Short_names_have_no_family_and_unknown_items_stay_unpriced()
    {
        var values = ItemValues.FromLedger(Parse(File(Sale(T, "Aldric", "Rat Ear", 1, "0 platinum 0 gold 0 silver 3 copper"))));
        Assert.False(values.Of("Rat Tail").Known);       // two words: a sibling would be a different thing
        Assert.False(values.Of("Wool Scraps").Known);
        Assert.False(values.Of("Rusty Iron Mace").Known);
    }

    [Fact]
    public void Rows_that_are_not_sales_or_that_paid_nothing_are_ignored()
    {
        var rows = Parse(File(Item(T, "Aldric", "Aldric", "a rat", "Rat Ear"), Sale(T, "Aldric", "Rat Tail", 1, "0 platinum 0 gold 0 silver 0 copper")));
        Assert.Equal(0, ItemValues.FromLedger(rows).Count);
    }
}

public class LootDayTests
{
    private static readonly DateTime T = new(2026, 10, 24, 9, 0, 0);

    private static LootEvent Item(double seconds, string who, string mob, string item, string cls = "", int level = 0, bool you = false) =>
        new(T.AddSeconds(seconds), LootKind.Item, who, you, mob, item, 1, 0, 0, Class: cls, Level: level);

    private static LootEvent Coin(double seconds, string who, string mob, long copper) =>
        new(T.AddSeconds(seconds), LootKind.Coin, who, false, mob, "", 0, copper, 0);

    [Fact]
    public void A_day_ranks_looters_by_coin_plus_item_value_and_names_their_class()
    {
        var events = new[]
        {
            Coin(0, "Brom", "a rat", 100), Item(1, "Brom", "a rat", "Rat Ear", "ftr", 31),
            Coin(60, "Doc", "a rat", 500), Item(61, "Doc", "a rat", "Rat Ear", "clr", 34), Item(62, "Doc", "a rat", "Wool Scraps"),
        };
        var values = new ItemValues([("Rat Ear", 50)]);
        var day = LootDay.Of(new DateOnly(2026, 10, 24), events, values);

        Assert.Equal(["Doc", "Brom"], day.Members.Select(m => m.Name));
        var doc = day.Member("doc")!;
        Assert.Equal("Cleric", doc.Class);
        Assert.Equal("Cleric 34", doc.ClassLevel);
        Assert.Equal(500, doc.Copper);
        Assert.Equal(50, doc.ItemValue);
        Assert.Equal(1, doc.Unpriced);                  // Wool Scraps
        Assert.Equal(550, doc.Total);
        Assert.Equal(1, doc.Mobs);
        Assert.Equal(2, day.Totals.Mobs);
        Assert.Equal(700, day.Total);                    // 600 coin + 2 x 50
        Assert.Equal(1, day.Unpriced);
        Assert.Equal(["Rat Ear", "Wool Scraps"], day.ItemsWorth.Select(i => i.Item));
    }

    [Fact]
    public void A_players_items_know_the_groups_count_and_where_they_mostly_came_from()
    {
        var events = new[]
        {
            Item(0, "Brom", "a rat", "Rat Ear"), Item(100, "Brom", "a bat", "Rat Ear"), Item(200, "Brom", "a bat", "Rat Ear"),
            Item(300, "Doc", "a rat", "Rat Ear"),
        };
        var day = LootDay.Of(new DateOnly(2026, 10, 24), events);
        var brom = day.Member("Brom")!;
        var ear = Assert.Single(brom.Taken);
        Assert.Equal(3, ear.Count);
        Assert.Equal(4, ear.GroupCount);
        Assert.Equal("a bat", ear.MostlyFrom);
        Assert.Equal(0.75, ear.Share);
    }

    [Fact]
    public void The_text_breakdown_lists_players_items_and_mobs()
    {
        var events = new[] { Coin(0, "Brom", "a rat", 144), Item(1, "Brom", "a rat", "Rat Ear", "ftr", 31) };
        var text = LootSummary.Breakdown(new DateOnly(2026, 10, 24), events);
        Assert.Contains("PLAYERS", text);
        Assert.Contains("Brom", text);
        Assert.Contains("Fighter 31", text);
        Assert.Contains("1sp 44cp", text);
        Assert.Contains("Rat Ear", text);
        Assert.Contains("MOBS", text);
        Assert.Contains("Nothing looted.", LootSummary.Breakdown(new DateOnly(2026, 10, 24), []));
    }
}
