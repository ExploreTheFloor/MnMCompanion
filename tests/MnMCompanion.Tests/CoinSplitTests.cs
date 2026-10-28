using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using static MnMCompanion.Tests.Fixture;

namespace MnMCompanion.Tests;

/// <summary>
/// Who a coin purse counts for. A purse the looter kept (<c>npc_corpse</c>) is theirs whole. One the looter's autosplit divided (<c>party_split</c>)
/// is not: they kept only part, and a ledger shows only its own owner's share (<c>d15</c>), never anyone else's. The group's total is always the
/// whole purse (<c>d12</c>), counted once.
/// </summary>
public class CoinSplitTests
{
    private static readonly DateTime T = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Local);
    private static readonly DateOnly Day = DateOnly.FromDateTime(T);

    private static List<LootEvent> Rows(params string[] rows) => LootExtractor.Extract(Parse(File(rows), LedgerFileKind.Social));

    [Fact]
    public void A_purse_that_was_not_split_is_counted_whole_for_whoever_looted_it()
    {
        var day = LootDay.Of(Day, Rows(Purse(T, "Brom", "Aldric", "a rat", "0,0,0,50")));

        Assert.Equal(50, day.Members.Single(m => m.Name == "Brom").Copper);
        Assert.Equal(50, day.Totals.Copper);
        Assert.False(Assert.Single(day.Events).Shared);
    }

    [Fact]
    public void A_split_purse_is_not_counted_for_its_looter_but_the_group_still_earned_all_of_it()
    {
        var day = LootDay.Of(Day, Rows(Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15")));   // 1 silver = 100 copper, your share 15

        var brom = day.Members.Single(m => m.Name == "Brom");
        Assert.Equal(0, brom.Copper);               // not counted for him: he kept only part and the ledger doesn't say how much
        Assert.Equal(1, brom.SplitPurses);
        Assert.Equal(100, brom.Purses);             // but the purses he looted held 100
        Assert.Equal(15, brom.Split);               // and 15 of them reached you
        Assert.Equal(100, day.Totals.Copper);       // what the group earned: the whole purse (d12)
        Assert.Equal(100, day.Total);
        Assert.Equal(15, day.Totals.Share);
        Assert.True(Assert.Single(day.Events).Shared);
    }

    [Fact]
    public void Your_share_of_a_split_purse_is_counted_for_you_even_if_you_looted_nothing()
    {
        var day = LootDay.Of(Day, Rows(Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15")));

        var you = day.Members.Single(m => m.Name == "Aldric");
        Assert.True(you.You);
        Assert.Equal(15, you.Copper);
        Assert.Equal(15, you.Received);
        Assert.Equal(0, you.Mobs);
    }

    [Fact]
    public void Your_own_whole_purses_and_your_shares_add_up_and_the_ranking_ignores_coin()
    {
        var day = LootDay.Of(Day, Rows(
            Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15"),
            Purse(T.AddHours(1), "Aldric", "Aldric", "a rat", "0,0,0,30"),                // alone an hour on: nobody's divided purse is near
            Purse(T.AddSeconds(60), "Pip", "Aldric", "a rat", "0,0,0,40"),
            Item(T.AddSeconds(61), "Pip", "Aldric", "a rat", "Rat Ear")), new ItemValues([("Rat Ear", 50)]));

        Assert.Equal(45, day.Members.Single(m => m.Name == "Aldric").Copper);   // 30 kept whole + 15 share
        // Pip took the only item; the others took none, and their coin (45 for Aldric, 0 known for Brom) is no basis for a ranking: only Aldric's is known in full
        Assert.Equal(["Pip", "Aldric", "Brom"], day.Members.Select(m => m.Name));
        Assert.Equal(170, day.Totals.Copper);                                      // 100 + 30 + 40, each purse once
    }

    [Fact]
    public void A_mobs_coin_and_a_looters_coin_a_mob_are_the_whole_purse_whoever_kept_it()
    {
        var day = LootDay.Of(Day, Rows(Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15")));

        Assert.Equal(100, day.Mobs.Single().Copper);
        Assert.Equal(100, day.Members.Single(m => m.Name == "Brom").CopperPerMob);
    }

    [Fact]
    public void When_two_of_your_characters_saw_the_same_split_purse_each_is_credited_its_own_share_and_the_purse_counts_once()
    {
        var aldric = Rows(Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15"));
        var wendell = Rows(Purse(T, "Brom", "Wendell", "a rat", "0,0,1,0", split: "0,0,0,20"));

        var day = LootDay.Of(Day, LootMerge.Combine([aldric, wendell]));

        Assert.Equal(15, day.Members.Single(m => m.Name == "Aldric").Copper);
        Assert.Equal(20, day.Members.Single(m => m.Name == "Wendell").Copper);
        Assert.Equal(0, day.Members.Single(m => m.Name == "Brom").Copper);
        Assert.Equal(100, day.Totals.Copper);   // one purse, not two
        Assert.Equal(35, day.Totals.Share);
    }

    [Fact]
    public void The_feed_says_when_a_purse_was_split_and_what_your_share_was()
    {
        var split = Assert.Single(Rows(Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15")));
        var whole = Assert.Single(Rows(Purse(T, "Brom", "Aldric", "a rat", "0,0,0,50")));

        Assert.Contains("(split; your share 15cp)", LootSummary.Describe(split));
        Assert.DoesNotContain("split", LootSummary.Describe(whole));
    }
}
