using MnMCompanion.Data;

namespace MnMCompanion.Tests;

/// <summary>The community file built from the saved database: the player's own rows, from every character on the PC, and nobody else's.</summary>
public class CommunityExportTests
{
    private static readonly DateTime T0 = new(2026, 10, 25, 20, 0, 0, DateTimeKind.Local);

    private static string Harvest(DateTime when, string item, string who)
        => Fixture.Row("act_27", "ref_9", new { d01 = 1, d02 = "", d03 = Fixture.B64("harvest"), d04 = item, d05 = "43e9d5b85dbdc81fa268da3a" }, when, who, who);

    private static void Character(Fixture.Scratch s, string who, int corpses)
    {
        var rows = new List<string>();
        for (int i = 0; i < corpses; i++)
        {
            var at = T0.AddMinutes(i);
            rows.Add(Fixture.Purse(at, who, who, "a rat", "0,0,0,5"));
            rows.Add(Fixture.Item(at.AddSeconds(1), who, who, "a rat", "Rat Ear"));
        }
        rows.Add(Fixture.Sale(T0.AddHours(1), who, "Rat Ear", 6, "0 platinum 0 gold 0 silver 24 copper"));
        rows.Add(Harvest(T0.AddHours(2), "Wood", who));
        s.Write(Path.Combine("alpha", who, "Ledger", $"{who}_Character_2026-10-25.json"), Fixture.File(rows.ToArray()));
    }

    private static string Path_(Fixture.Scratch s) => System.IO.Path.Combine(s.Path, "ledger.db");

    [Fact]
    public void Every_characters_own_rows_are_included_and_other_players_are_not()
    {
        using var s = new Fixture.Scratch();
        Character(s, "Aldric", 12);
        Character(s, "Wendell", 8);
        // What the party saw: another player's loot, in Aldric's Social file. It must not be counted or named.
        s.Write(System.IO.Path.Combine("alpha", "Aldric", "Ledger", "Aldric_Social_2026-10-25.json"),
            Fixture.File(Enumerable.Range(0, 30).Select(i => Fixture.Item(T0.AddMinutes(i), "Hobart", "Aldric", "a rat", "Rat Ear")).ToArray()));
        using var db = new LedgerDb(Path_(s));
        LedgerArchiver.Run(db, s.Path);
        EventBuilder.Update(db);

        var c = CommunityExport.Build(db, "MnMCompanion test", new DateOnly(2026, 10, 25));

        var rat = Assert.Single(c.Drops);
        Assert.Equal(("a rat", "testzone", 20, 20), (rat.Mob, rat.Zone, rat.Corpses, rat.Purses));   // 12 + 8, not the 30 Hobart looted
        Assert.Equal(100, rat.Copper);
        Assert.Equal(new MnMCompanion.Community.ContributionItem("Rat Ear", 20, 20), rat.Items.Single());
        var price = Assert.Single(c.Prices);
        Assert.Equal(2, price.Sold["4"]);                // two sales, one by each character, of one for 4cp
        Assert.Equal(2, c.Harvest.Single().Harvests);
        Assert.Equal("9a818e77c3484fe98efd9081", c.Items.Single(i => i.Name == "Rat Ear").Id);
        var json = c.ToJson();
        Assert.DoesNotContain("Hobart", json);
        Assert.DoesNotContain("Aldric", json);
        Assert.DoesNotContain("Wendell", json);
    }

    [Fact]
    public void The_contributor_id_is_random_made_once_and_kept()
    {
        using var s = new Fixture.Scratch();
        string first;
        using (var db = new LedgerDb(Path_(s)))
        {
            first = db.ContributorId();
            Assert.Equal(32, first.Length);
            Assert.Matches("^[0-9a-f]{32}$", first);
            Assert.Equal(first, db.ContributorId());
        }
        using (var again = new LedgerDb(Path_(s))) Assert.Equal(first, again.ContributorId());   // survives a restart

        using var other = new Fixture.Scratch();
        using var db2 = new LedgerDb(System.IO.Path.Combine(other.Path, "ledger.db"));
        Assert.NotEqual(first, db2.ContributorId());   // a different PC gets a different one
    }

    [Fact]
    public void An_empty_database_gives_an_empty_contribution_that_still_carries_its_id_and_licence()
    {
        using var s = new Fixture.Scratch();
        using var db = new LedgerDb(Path_(s));

        var c = CommunityExport.Build(db, "MnMCompanion test", new DateOnly(2026, 10, 25));

        Assert.Empty(c.Drops);
        Assert.Equal(db.ContributorId(), c.Contributor);
        Assert.Equal("CC-BY-NC-SA-4.0", c.License);
        Assert.Equal(2, c.Schema);
    }
}
