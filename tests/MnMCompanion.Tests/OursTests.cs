using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using static MnMCompanion.Tests.Fixture;

namespace MnMCompanion.Tests;

/// <summary>
/// The two figures at the top of the loot windows: Total (every purse once, whole, plus what a vendor pays for all the items) and Ours (what reached
/// the ledger's owner: their coin and the items they looted). Only the owner's coin is known in full, so players are compared by items alone.
/// </summary>
public class OursTests
{
    private static readonly DateTime T = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Local);
    private static readonly DateOnly Day = DateOnly.FromDateTime(T);
    private static readonly ItemValues Prices = new([("Rat Ear", 50), ("Fang", 200)]);

    private static List<LootEvent> Rows(params string[] rows) => LootExtractor.Extract(Parse(File(rows), LedgerFileKind.Social));

    [Fact]
    public void Ours_is_your_coin_and_the_items_you_looted_and_Total_is_everyones()
    {
        var day = LootDay.Of(Day, Rows(
            Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15"),            // split: 100 in all, 15 reached you
            Purse(T.AddHours(1), "Aldric", "Aldric", "a rat", "0,0,0,30"),                // yours, whole (an hour on, nobody's purse near it was divided: you were alone)
            Item(T.AddHours(1).AddSeconds(1), "Aldric", "Aldric", "a rat", "Rat Ear"),    // 50
            Purse(T.AddSeconds(60), "Pip", "Aldric", "a rat", "0,0,0,40"),
            Item(T.AddSeconds(61), "Pip", "Aldric", "a rat", "Fang")), Prices);           // 200, not yours

        var ours = day.Ours;
        Assert.Equal(45, ours.Copper);          // 30 whole + 15 share
        Assert.Equal(50, ours.ItemValue);
        Assert.Equal(1, ours.Items);
        Assert.Equal(95, ours.Total);

        Assert.Equal(170, day.Totals.Copper);   // each purse once, whole
        Assert.Equal(250, day.ItemValue);
        Assert.Equal(420, day.Total);
        Assert.True(ours.Total <= day.Total);
    }

    [Fact]
    public void Ours_is_just_your_share_when_you_looted_nothing_and_nothing_when_the_day_has_no_owner()
    {
        var shared = LootDay.Of(Day, Rows(Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15")), Prices);
        Assert.Equal(new LootOurs(15, 0, 0, 0, 0), shared.Ours);
        Assert.True(shared.Members.Single(m => m.Name == "Aldric").You);

        Assert.Equal(LootOurs.None, LootDay.Of(Day, [], Prices).Ours);
    }

    [Fact]
    public void Ours_flags_what_is_estimated_or_unpriced_like_the_rest()
    {
        var day = LootDay.Of(Day, Rows(
            Item(T, "Aldric", "Aldric", "a rat", "Rat Ear"),
            Item(T.AddSeconds(5), "Aldric", "Aldric", "a rat", "Mystery Thing")), Prices);

        Assert.Equal(1, day.Ours.Unpriced);
        Assert.Equal(50, day.Ours.ItemValue);
        Assert.False(day.Ours.Approximate);
    }

    [Fact]
    public void Ours_adds_up_every_character_of_yours_in_view_and_counts_a_split_purse_once_in_Total()
    {
        var aldric = Rows(Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15"));
        var wendell = Rows(Purse(T, "Brom", "Wendell", "a rat", "0,0,1,0", split: "0,0,0,20"), Item(T.AddSeconds(1), "Wendell", "Wendell", "a rat", "Fang"));

        var day = LootDay.Of(Day, LootMerge.Combine([aldric, wendell]), Prices);

        Assert.Equal(35, day.Ours.Copper);
        Assert.Equal(200, day.Ours.ItemValue);
        Assert.Equal(100, day.Totals.Copper);   // one purse, not two
    }

    [Fact]
    public void Players_are_ranked_by_the_value_of_what_they_looted_then_by_how_many_items()
    {
        var day = LootDay.Of(Day, Rows(
            Purse(T, "Brom", "Aldric", "a rat", "0,5,0,0"),                              // a rich purse: no help in the ranking
            Item(T.AddSeconds(30), "Pip", "Aldric", "a bat", "Rat Ear"),
            Item(T.AddSeconds(31), "Pip", "Aldric", "a bat", "Mystery Thing"),
            Item(T.AddSeconds(60), "Tamsin", "Aldric", "a fox", "Fang"),
            Item(T.AddSeconds(90), "Orrin", "Aldric", "a fox", "Mystery Thing")), Prices);

        // Tamsin 200, Pip 50 (and an unpriced item), Orrin nothing priced, Brom coin only
        Assert.Equal(["Tamsin", "Pip", "Orrin", "Brom"], day.Members.Select(m => m.Name));
    }

    [Fact]
    public void The_copied_summary_has_an_Ours_line_and_no_coin_column_for_players()
    {
        var events = Rows(
            Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15"),
            Item(T.AddSeconds(30), "Aldric", "Aldric", "a rat", "Rat Ear"));

        var text = LootSummary.Breakdown(Day, events, values: Prices);

        Assert.Contains("Ours 65cp  ·  coin 15cp + items 50cp", text);
        Assert.Contains("ITEMS WORTH", text);
        var players = text.Split("PLAYERS" + Environment.NewLine)[1].Split(Environment.NewLine + Environment.NewLine)[0];   // the table: header and rows
        Assert.Contains("ITEMS WORTH", players);
        Assert.DoesNotContain("COIN", players);
    }

    [Fact]
    public void The_copied_summary_has_no_Ours_line_without_an_owner()
    {
        // a ledger always has an owner, but a made-up list of other people's loot (a search answer) may not
        var others = new[] { new LootEvent(T, LootKind.Coin, "Brom", false, "a rat", "", 0, 100, 0) };
        Assert.DoesNotContain("Ours", LootSummary.Breakdown(Day, others, values: Prices));
    }
}
