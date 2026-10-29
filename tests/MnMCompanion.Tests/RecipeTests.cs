using MnMCompanion.Recipes;

namespace MnMCompanion.Tests;

/// <summary>Reading the wiki's recipe document, cutting a skill into stretches of levels, and the search box over it.</summary>
public class RecipeTests
{
    private static RecipeBook Book => RecipeFixture.Book;

    // ---------------------------------------------------------------- reading

    [Fact]
    public void Skills_without_recipes_are_left_out_and_the_rest_are_listed_by_name()
    {
        Assert.Equal(["Blacksmithing", "Enchanting", "Jewelcrafting"], Book.Skills.Select(s => s.Name));
        Assert.Equal(10, Book.Recipes.Count);
    }

    [Fact]
    public void A_skill_knows_its_station_tools_trainers_count_and_levels()
    {
        var smith = Book.SkillNamed("blacksmithing")!;
        Assert.Equal(("Anvil", "Smithing Pliers, Smithing Hammer", "Blacksmith Musafar"), (smith.Station, smith.Tools, smith.Trainers));
        Assert.Equal((8, 5, 110, "5–110"), (smith.Count, smith.LowLevel, smith.HighLevel, smith.LevelRange));   // the "?" and "-" recipes have no level and don't widen it
        Assert.Equal("20", Book.SkillNamed("Jewelcrafting")!.LevelRange);
    }

    [Theory]
    [InlineData("Copper Fasteners", 5, "5")]
    [InlineData("Iron Fasteners", 60, "60")]        // the wiki sometimes sends the number as a number
    [InlineData("Bronze Rivets", 5, "5+")]          // "5+": at least 5
    [InlineData("Mystery Hinge", null, "?")]
    public void A_level_is_the_number_the_wiki_gives_or_nothing(string result, int? level, string text)
    {
        var r = Book.Recipes.Single(x => x.Result == result);
        Assert.Equal((level, text), (r.Level, r.LevelText));
    }

    [Fact]
    public void Ingredients_keep_their_quantities_and_the_alternative_they_can_be_swapped_for()
    {
        var links = Book.Recipes.Single(r => r.Result == "Copper Chain Links");
        Assert.Equal("6 Copper Bar, 1 Smithing Pliers", links.IngredientLine);
        Assert.Equal("1 Bronze Bar or Zinc Bar", Book.Recipes.Single(r => r.Result == "Bronze Rivets").IngredientLine);
        Assert.Equal("", Book.Recipes.Single(r => r.Result == "Mystery Hinge").IngredientLine);
    }

    [Fact]
    public void A_recipe_that_makes_several_says_so_and_names_are_tidied()
    {
        Assert.Equal("Iron Fasteners ×4", Book.Recipes.Single(r => r.Result == "Iron Fasteners").Title);
        Assert.Contains(Book.Recipes, r => r.Result == "Odd Spaced Name");
        Assert.Equal(3, Book.WithoutIngredients);   // Mystery Hinge (an empty list), the odd name (no list at all) and Powder
    }

    [Fact]
    public void Notes_are_the_wikis_extra_columns_without_the_blank_ones()
    {
        var sword = Book.Recipes.Single(r => r.Result == "Iron Longsword");
        Assert.Equal([("Station", "Anvil"), ("Slot", "Primary")], sword.Notes.Select(n => (n.Label, n.Text)));   // Notes: "-" is nothing
        Assert.Equal([("Notes", "Used in armor and Jewelcrafting.")], Book.Recipes.Single(r => r.Result == "Copper Chain Links").Notes.Select(n => (n.Label, n.Text)));
    }

    [Fact]
    public void The_stat_columns_of_a_piece_of_jewelry_become_one_line_with_the_zeros_and_dashes_left_out()
    {
        var ring = Book.Recipes.Single(r => r.Result == "Jade Ring");
        Assert.Equal([("Mold", "Crude Ring Mold"), ("Stats", "STR 2 · WIS 1")], ring.Notes.Select(n => (n.Label, n.Text)));
    }

    [Fact]
    public void A_broken_dash_in_the_wikis_text_is_shown_as_a_dash()
    {
        var powder = Book.Recipes.Single(r => r.Result == "Powder");
        Assert.Equal("Arcane Powder x4–x5", powder.Notes.Single(n => n.Label == "Disenchants Into").Text);
        Assert.Equal("+2 CHA, +1 WIS", powder.Notes.Single(n => n.Label == "Stats").Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"something\":1}")]
    [InlineData("{\"tradeskills\":[]}")]
    [InlineData("{\"tradeskills\":[{\"name\":\"Mining\",\"recipeGroups\":[]}]}")]
    public void A_document_that_is_not_the_recipe_list_gives_nothing_instead_of_an_error(string? json) => Assert.Null(RecipeBook.TryParse(json));

    [Fact]
    public void Odd_entries_inside_a_good_document_are_skipped()
    {
        var book = RecipeBook.TryParse("""{"tradeskills":[1,{"name":"Cooking","recipeGroups":[{"name":"x","recipes":[5,{"result":""},{"result":"Stew","trivial":"7","ingredients":[7,{"name":""},{"name":"Meat"}]}]}]}]}""")!;
        var stew = Assert.Single(book.Recipes);
        Assert.Equal(("Stew", 7, "1 Meat"), (stew.Result, stew.Level, stew.IngredientLine));
    }

    [Fact]
    public void A_recipe_copies_as_one_line_of_text()
    {
        Assert.Equal("Iron Fasteners ×4 (Blacksmithing 60): 2 Iron Bar", Book.Recipes.Single(r => r.Result == "Iron Fasteners").Text());
        Assert.Equal("Mystery Hinge (Blacksmithing): ingredients not listed on the wiki yet", Book.Recipes.Single(r => r.Result == "Mystery Hinge").Text());
    }

    // ---------------------------------------------------------------- levels

    [Fact]
    public void A_short_skill_is_one_list_in_order_of_level()
    {
        var jewel = RecipeLevels.Bands(Book.Of("Jewelcrafting"));
        var band = Assert.Single(jewel);
        Assert.Equal("All levels", band.Title);
    }

    private static List<Recipe> Many(params int?[] levels) =>
        levels.Select((l, i) => new Recipe("Smithing", "g", $"Thing {i:D2}", 1, l, l?.ToString() ?? "?", [], [])).ToList();

    [Fact]
    public void A_long_skill_is_cut_into_stretches_of_twenty_five_levels_easiest_first_with_the_unknown_last()
    {
        var recipes = Many(60, 1, 25, 26, 50, 51, 300, null, 0, 100, 74, 75, 76, 12);
        var bands = RecipeLevels.Bands(recipes);
        Assert.Equal(["Level 1–25", "Level 26–50", "Level 51–75", "Level 76–100", "Level 276–300", "Level not on the wiki yet"], bands.Select(b => b.Title));
        Assert.Equal([0, 1, 12, 25], bands[0].Recipes.Select(r => r.Level!.Value));   // level 0 goes in the first stretch
        Assert.Equal([26, 50], bands[1].Recipes.Select(r => r.Level!.Value));
        Assert.Equal([51, 60, 74, 75], bands[2].Recipes.Select(r => r.Level!.Value));
        Assert.Equal((1, 25), (bands[0].From, bands[0].To));
        Assert.Null(bands[^1].From);
    }

    [Fact]
    public void Recipes_of_the_same_level_are_in_name_order()
    {
        var recipes = new[] { "Zinc", "apple", "Mango" }.Select(n => new Recipe("S", "g", n, 1, 10, "10", [], [])).Concat(Many(1, 2, 3, 4, 5, 6, 7, 8, 9, 11)).ToList();
        var first = RecipeLevels.Bands(recipes)[0];
        Assert.Equal(["apple", "Mango", "Zinc"], first.Recipes.Where(r => r.Level == 10).Select(r => r.Result));
    }

    [Fact]
    public void No_recipes_make_no_bands() => Assert.Empty(RecipeLevels.Bands([]));

    // ---------------------------------------------------------------- search

    private static List<string> Find(string? query, string? skill = null) =>
        RecipeSearch.Filter(skill is null ? Book.Recipes : Book.Of(skill), query).Select(r => r.Result).ToList();

    [Fact]
    public void An_empty_search_finds_everything() => Assert.Equal(10, Find("  ").Count);

    [Fact]
    public void Every_word_has_to_match_in_any_order_and_a_part_of_a_word_is_enough()
    {
        Assert.Equal(["Iron Fasteners", "Iron Longsword", "Iron Whammer"], Find("iron").OrderBy(x => x));
        Assert.Equal(["Iron Fasteners", "Iron Longsword"], Find("fast iron").OrderBy(x => x));   // the sword uses Iron Fasteners
        Assert.Equal(["Copper Chain Links", "Copper Fasteners"], Find("copp bar").OrderBy(x => x));   // the ingredient is searched too
    }

    [Fact]
    public void The_skill_the_kind_the_notes_and_the_alternative_ingredient_are_searched()
    {
        Assert.Equal(["Copper Chain Links", "Jade Ring"], Find("jewelcrafting").OrderBy(x => x));   // the ring's skill, and a note that mentions it
        Assert.Equal(["Iron Longsword", "Iron Whammer"], Find("weapons").OrderBy(x => x));
        Assert.Equal(["Jade Ring"], Find("crude mold"));
        Assert.Equal(["Bronze Rivets"], Find("zinc"));
        Assert.Equal(["Iron Longsword"], Find("primary"));
    }

    [Fact]
    public void Two_numbers_with_a_dash_are_a_range_of_levels_and_one_number_is_a_level()
    {
        Assert.Equal(["Iron Fasteners", "Iron Longsword", "Iron Whammer"], Find("50-120").OrderBy(x => x));
        Assert.Equal(["Iron Longsword", "Iron Whammer"], Find("120-100").OrderBy(x => x));   // either way round
        Assert.Equal(["Iron Fasteners"], Find("60"));
        Assert.Equal(["Iron Longsword"], Find("iron 90-105"));
        Assert.Empty(Find("200-300"));
    }

    [Fact]
    public void A_recipe_with_no_level_is_in_no_range()
    {
        Assert.DoesNotContain("Mystery Hinge", Find("1-300"));
        Assert.Contains("Mystery Hinge", Find("mystery"));
    }

    [Fact]
    public void Searching_inside_one_skill_stays_inside_it() => Assert.Equal(["Jade Ring"], Find("ring", "Jewelcrafting"));
}
