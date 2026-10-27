using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion.Tests;

/// <summary>The search over every day of loot, and the row search of the History pages.</summary>
public class LootSearchTests
{
    private static readonly DateTime Mon = new(2026, 10, 19, 19, 0, 0);

    private static LootEvent Item(DateTime at, string who, string mob, string item, int count = 1) =>
        new(at, LootKind.Item, who, false, mob, item, count, 0, 0, "zone");

    private static LootEvent Coin(DateTime at, string who, string mob, long copper) =>
        new(at, LootKind.Coin, who, false, mob, "", 0, copper, 0, "zone");

    /// <summary>Three days, three looters, a purse on two of them and a few kinds of items.</summary>
    private static List<LootEvent> Ledger() =>
    [
        Coin(Mon, "Orrin", "a rat", 40),
        Item(Mon.AddSeconds(1), "Orrin", "a rat", "Rat Ear", 2),
        Item(Mon.AddMinutes(5), "Pip", "a wolf", "Iron Scraps"),
        Item(Mon.AddMinutes(5).AddSeconds(2), "Pip", "a wolf", "Wolf Pelt"),
        Item(Mon.AddDays(1), "Orrin", "a wolf", "Iron Scraps", 3),
        Coin(Mon.AddDays(1).AddSeconds(1), "Orrin", "a wolf", 90),
        Item(Mon.AddDays(1).AddMinutes(2), "Tamsin", "a bandit", "Cloth Scraps"),
        Item(Mon.AddDays(2), "Pip", "a bandit", "Iron Scraps", 4),
        Item(Mon.AddDays(2).AddMinutes(3), "Tamsin", "a wolf", "Raw Wolf Meat", 5),
    ];

    private static string[] Items(string query) => LootSearch.Matching(Ledger(), query).Select(e => e.Item).Distinct().Order().ToArray();

    [Fact]
    public void Nothing_typed_finds_nothing()
    {
        Assert.Empty(LootSearch.Matching(Ledger(), ""));
        Assert.Empty(LootSearch.Matching(Ledger(), "   "));
        Assert.Null(LootSearch.Answer(Ledger(), null));
    }

    [Fact]
    public void An_item_is_found_on_every_day_it_was_taken_with_its_total()
    {
        var answer = LootSearch.Answer(Ledger(), "iron scraps")!;
        Assert.True(LootSearch.IsAnswer(answer));
        Assert.Equal(8, answer.Totals.Items);                       // 1 + 3 + 4
        Assert.Equal(3, LootSearch.Days(answer.Events));
        Assert.Equal(new[] { "Iron Scraps" }, answer.Items.Select(i => i.Item));
        Assert.Equal(new[] { ("Pip", 5), ("Orrin", 3) }, answer.Items[0].By);   // who took them, most first
        // the purses and every other item are left out
        Assert.All(answer.Events, e => Assert.Equal(LootKind.Item, e.Kind));
        Assert.Equal(0, answer.Totals.Copper);
    }

    [Fact]
    public void Words_can_come_from_the_looter_the_item_or_the_monster_in_any_order_and_case()
    {
        Assert.Equal(new[] { "Iron Scraps" }, Items("PIP scraps"));        // looter + item
        Assert.Equal(new[] { "Iron Scraps" }, Items("iron ORRIN"));        // in the other order
        Assert.Equal(new[] { "Rat Ear" }, Items("orrin rat"));             // looter + monster ("a rat")
        // a monster: what came off wolves, and the items with wolf in the name
        Assert.Equal(new[] { "Iron Scraps", "Raw Wolf Meat", "Wolf Pelt" }, Items("wolf"));
        // every word has to be there
        Assert.Empty(Items("pip pelt meat"));
        Assert.Equal(new[] { "Raw Wolf Meat" }, Items("tamsin meat"));
    }

    [Fact]
    public void A_search_that_matches_nothing_has_no_answer()
        => Assert.Null(LootSearch.Answer(Ledger(), "dragon"));

    [Fact]
    public void The_answer_names_who_took_it_and_from_which_monsters()
    {
        var answer = LootSearch.Answer(Ledger(), "scraps")!;
        Assert.Equal(new[] { "Pip", "Orrin", "Tamsin" }, answer.Members.OrderByDescending(m => m.Items).Select(m => m.Name));
        Assert.Equal(new[] { "a bandit", "a wolf" }, answer.Mobs.OrderByDescending(m => m.Items).Select(m => m.Mob));   // 5 and 4 taken
        // two corpses of each: the visits are days apart
        Assert.Equal(2, answer.Mobs.Single(m => m.Mob == "a wolf").Looted);
        Assert.Equal(2, answer.Mobs.Single(m => m.Mob == "a bandit").Looted);
    }

    [Fact]
    public void Days_are_listed_newest_first_with_what_was_taken_and_worth()
    {
        var hits = LootSearch.Matching(Ledger(), "scraps");
        var days = LootSearch.ByDay(hits, new ItemValues([("Iron Scraps", 10), ("Cloth Scraps", 4)]));
        Assert.Equal(new[] { new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 19) }, days.Select(d => d.Day));
        Assert.Equal(new[] { 4, 4, 1 }, days.Select(d => d.Taken));
        Assert.Equal(new[] { 40L, 34L, 10L }, days.Select(d => d.Worth));   // 4×10; 3×10 + 1×4; 1×10
        Assert.Equal(new[] { 1, 2, 1 }, days.Select(d => d.Kinds));
        Assert.Equal(new[] { 1, 2, 1 }, days.Select(d => d.Looters));
        Assert.All(days, d => Assert.False(d.Unpriced));
        Assert.All(LootSearch.ByDay(hits), d => Assert.True(d.Unpriced));   // no prices known
    }

    [Fact]
    public void The_text_for_the_clipboard_says_what_was_searched_for()
    {
        var text = LootSearch.Text("iron scraps", LootSearch.Answer(Ledger(), "iron scraps")!);
        Assert.Contains("“iron scraps”", text);
        Assert.Contains("8 taken", text);
        Assert.Contains("Iron Scraps ×8", text);
    }

    // ---- the rows of the other History pages ----

    [Fact]
    public void The_history_filter_wants_every_word_somewhere_in_the_row()
    {
        var view = new HistoryView();
        view.SetFilter("raw croc meat");
        Assert.True(view.Matches("Raw Crocodile Meat", "Dustrend"));
        Assert.False(view.Matches("Raw Orc Meat", "Dustrend"));
        view.SetFilter("orrin scraps");                         // one word in the character column, one in the item
        Assert.True(view.Matches("Iron Scraps", "Shaded Dunes", "Orrin"));
        Assert.False(view.Matches("Iron Scraps", "Shaded Dunes", "Pip"));
        view.SetFilter("dunes");                                // a place
        Assert.True(view.Matches("Iron Scraps", "Shaded Dunes", ""));
        view.SetFilter("");
        Assert.True(view.Matches("anything"));
        Assert.True(view.Matches());
    }

    [Fact]
    public void A_word_has_to_be_inside_one_column_not_spread_over_two()
    {
        Assert.False(NameSearch.MatchesAny("ross", "Cro", "ss"));   // "cro" + "ss" are two columns, not the word "ross"
        Assert.True(NameSearch.MatchesAny("cro ss", "Cro", "ss"));
        Assert.True(NameSearch.MatchesAny("", "x"));
        Assert.True(NameSearch.MatchesAny("x", null, "x"));
        Assert.False(NameSearch.MatchesAny("x", null, null));
    }
}
