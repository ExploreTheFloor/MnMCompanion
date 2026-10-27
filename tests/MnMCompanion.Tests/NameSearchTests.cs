using MnMCompanion.Loot;

namespace MnMCompanion.Tests;

/// <summary>The search over item names: part of a name, any case, any order, every word needed.</summary>
public class NameSearchTests
{
    [Theory]
    [InlineData("Rawhide Backpack", "rawhide", true)]
    [InlineData("Rawhide Backpack", "BACKPACK", true)]
    [InlineData("Rawhide Backpack", "hide back", true)]          // a part of each word is enough
    [InlineData("Rawhide Backpack", "backpack rawhide", true)]   // in any order
    [InlineData("Rawhide Backpack", "rawhide pouch", false)]     // every word is needed
    [InlineData("Rawhide Backpack", "  rawhide   ", true)]       // blanks around and between don't count
    [InlineData("Rawhide Backpack", "", true)]
    [InlineData("Rawhide Backpack", "   ", true)]
    [InlineData("Rawhide Backpack", null, true)]
    [InlineData("Rawhide Backpack", "satchel", false)]
    [InlineData("Cinder Beetle Mandible", "beetle mand", true)]
    public void A_name_matches_when_it_has_every_word(string name, string? query, bool expected)
        => Assert.Equal(expected, NameSearch.Matches(name, query));

    [Fact]
    public void A_missing_name_matches_only_an_empty_search()
    {
        Assert.True(NameSearch.Matches(null, ""));
        Assert.False(NameSearch.Matches(null, "wood"));
        Assert.False(NameSearch.Matches("", "wood"));
    }

    [Fact]
    public void Filtering_keeps_the_rows_that_match_in_their_own_order()
    {
        string[] names = ["Wood", "Cloth Scraps", "Rawhide Scraps", "Grave Marker"];
        Assert.Equal(new[] { "Cloth Scraps", "Rawhide Scraps" }, NameSearch.Filter(names, n => n, "scraps"));
        Assert.Same(names, NameSearch.Filter(names, n => n, ""));   // nothing typed: the very same list, nothing copied
        Assert.Empty(NameSearch.Filter(names, n => n, "gold"));
    }
}
