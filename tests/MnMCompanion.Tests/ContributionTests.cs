using System.Text.RegularExpressions;
using MnMCompanion.Community;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion.Tests;

/// <summary>What a player shares with the community data: their own aggregates, and nothing that identifies anyone.</summary>
public class ContributionTests
{
    private static readonly DateTime T = new(2026, 10, 25, 20, 13, 47);
    private const string Me = "Aldric";            // the player's own character
    private const string Id = "0123456789abcdef0123456789abcdef";

    private static IEnumerable<LootEvent> Corpse(ref DateTime at, string mob, string zone, long purse, params string[] items)
    {
        var list = new List<LootEvent>();
        if (purse > 0) list.Add(new LootEvent(at, LootKind.Coin, Me, true, mob, "", 0, purse, 0, zone));
        foreach (var item in items)
        {
            at = at.AddSeconds(1);
            list.Add(new LootEvent(at, LootKind.Item, Me, true, mob, item, 1, 0, 0, zone));
        }
        at = at.AddSeconds(60);
        return list;
    }

    private static VendorRow Sell(string item, int qty, long total, string zone = "nightharborw") => new(T, Me, VendorDirection.Sold, item, qty, total, zone);
    private static VendorRow Buy(string item, int qty, long total, string zone = "nightharborw") => new(T, Me, VendorDirection.Bought, item, qty, total, zone);

    private static Contribution Make(IEnumerable<LootEvent>? loot = null, IEnumerable<VendorRow>? vendor = null, IEnumerable<HarvestRow>? harvest = null, IEnumerable<ContributionName>? names = null)
        => ContributionBuilder.Build(loot ?? [], vendor ?? [], harvest ?? [], names ?? [], Id, "MnMCompanion 0.2.0", new DateOnly(2026, 10, 25));

    // ------------------------------------------------------------------

    [Fact]
    public void Drops_say_how_many_corpses_were_looted_and_how_many_gave_each_item()
    {
        var at = T;
        var loot = new List<LootEvent>();
        for (int i = 0; i < 10; i++) loot.AddRange(Corpse(ref at, "a restless skeleton", "fallenpass", i < 4 ? 30 : 0, i < 6 ? ["Bone Chips", "Wool Scraps"] : ["Bone Chips"]));

        var c = Make(loot);

        var skeleton = Assert.Single(c.Drops);
        Assert.Equal(("a restless skeleton", "fallenpass", 10, 4, 120L), (skeleton.Mob, skeleton.Zone, skeleton.Corpses, skeleton.Purses, skeleton.Copper));
        Assert.Equal(new ContributionItem("Bone Chips", 10, 10), skeleton.Items.Single(i => i.Item == "Bone Chips"));
        Assert.Equal(new ContributionItem("Wool Scraps", 6, 6), skeleton.Items.Single(i => i.Item == "Wool Scraps"));
        Assert.Equal((1, 10), (c.Totals.Monsters, c.Totals.Corpses));
    }

    [Fact]
    public void A_players_corpse_is_never_a_monster_and_never_shared()
    {
        var at = T;
        var loot = new List<LootEvent>();
        loot.AddRange(Corpse(ref at, "a rat", "z1", 5, "Rat Ear"));
        loot.AddRange(Corpse(ref at, "Hobart's corpse", "z1", 0, "Worn Bow"));
        loot.AddRange(Corpse(ref at, "your corpse", "z1", 12, "Worn Bow"));

        var c = Make(loot);

        Assert.Equal(["a rat"], c.Drops.Select(d => d.Mob));
        Assert.DoesNotContain("Hobart", c.ToJson());
    }

    [Fact]
    public void Prices_are_a_tally_of_what_one_went_for_kept_apart_by_zone_and_direction()
    {
        var vendor = new[]
        {
            Sell("Rat Ear", 6, 24), Sell("Rat Ear", 2, 8), Sell("rat ear", 1, 5), Sell("Rat Ear", 3, 12, zone: "fallenpass"),
            Buy("Rat Ear", 1, 16), Sell("Odd Thing", 1, 0),      // no coin read: no price
        };

        var c = Make(vendor: vendor);

        var home = c.Prices.Single(p => p.Item == "Rat Ear" && p.Zone == "nightharborw");
        Assert.Equal(new Dictionary<string, int> { ["4"] = 2, ["5"] = 1 }, home.Sold);
        Assert.Equal(new Dictionary<string, int> { ["16"] = 1 }, home.Bought);
        Assert.Equal(new Dictionary<string, int> { ["4"] = 1 }, c.Prices.Single(p => p.Zone == "fallenpass").Sold);
        Assert.DoesNotContain(c.Prices, p => p.Item == "Odd Thing");
        Assert.Equal((4, 1), (c.Totals.Sales, c.Totals.Purchases));
    }

    [Fact]
    public void Gathering_is_totalled_per_item_and_zone()
    {
        var harvest = new[]
        {
            new HarvestRow(T, Me, "Wood", 1, "nightharborw"), new HarvestRow(T, Me, "wood", 2, "nightharborw"),
            new HarvestRow(T, Me, "Wood", 1, "fallenpass"), new HarvestRow(T, Me, "Copper Ore", 5, "nightharborw"),
        };

        var c = Make(harvest: harvest);

        Assert.Equal(("Copper Ore", "nightharborw", 1, 5), (c.Harvest[0].Item, c.Harvest[0].Zone, c.Harvest[0].Harvests, c.Harvest[0].Units));
        var wood = c.Harvest.Single(h => h.Item == "Wood" && h.Zone == "nightharborw");
        Assert.Equal((2, 3), (wood.Harvests, wood.Units));
        Assert.Equal(4, c.Totals.Harvests);
    }

    [Fact]
    public void The_item_catalogue_keeps_real_ids_once_and_drops_the_rest()
    {
        var names = new[]
        {
            new ContributionName("Rat Ear", "02E039FE58CBD0880099A5A4"), new ContributionName("rat ear", "02e039fe58cbd0880099a5a4"),
            new ContributionName("Rat Ear", "ffffffffffffffffffffffff"), new ContributionName("Bad", "not-an-id"), new ContributionName("", "02e039fe58cbd0880099a5a4"),
        };

        var c = Make(names: names);

        Assert.Equal(["02e039fe58cbd0880099a5a4", "ffffffffffffffffffffffff"], c.Items.Select(i => i.Id));
        Assert.Equal(2, c.Totals.Items);
    }

    [Fact]
    public void Nothing_in_the_file_names_a_player_or_gives_a_time_of_day()
    {
        var at = T;
        var loot = new List<LootEvent>();
        for (int i = 0; i < 5; i++) loot.AddRange(Corpse(ref at, "a bandit", "shadeddunes", 10, "Cloth Scraps"));
        var c = Make(loot, [Sell("Cloth Scraps", 5, 45)], [new HarvestRow(T, Me, "Wood", 1, "fallenpass")], [new ContributionName("Cloth Scraps", "9a818e77c3484fe98efd9081")]);

        var json = c.ToJson();

        Assert.DoesNotContain(Me, json);
        Assert.False(Regex.IsMatch(json, @"\d\d:\d\d"), "a time of day got into the file");
        Assert.DoesNotContain("ref_", json);
        Assert.Contains("\"generated\": \"2026-10-25\"", json);
        Assert.Contains("\"license\": \"CC-BY-NC-SA-4.0\"", json);
        Assert.Contains("\"schema\": 1", json);
        Assert.Contains($"\"contributor\": \"{Id}\"", json);
    }

    [Fact]
    public void The_file_reads_back_as_the_same_contribution()
    {
        var at = T;
        var loot = new List<LootEvent>();
        for (int i = 0; i < 3; i++) loot.AddRange(Corpse(ref at, "a rat", "z1", 4, "Rat Ear"));
        var made = Make(loot, [Sell("Rat Ear", 1, 4)]);

        var back = Contribution.FromJson(made.ToJson())!;

        Assert.Equal(made.Totals, back.Totals);
        Assert.Equal(made.Drops[0], back.Drops[0] with { Items = made.Drops[0].Items });
        Assert.Equal(made.Prices[0].Sold, back.Prices[0].Sold);
        Assert.Equal(made.Contributor, back.Contributor);
    }

    [Fact]
    public void An_empty_ledger_makes_an_empty_but_valid_contribution()
    {
        var c = Make();

        Assert.Empty(c.Drops);
        Assert.Equal(new ContributionTotals(0, 0, 0, 0, 0, 0), c.Totals);
        Assert.Equal(5, ContributionBuilder.Describe(c).Count);   // monsters, prices, harvests, item names, NPC lines
        Assert.Empty(c.Npcs);
    }

    [Fact]
    public void Long_names_are_cut_so_one_odd_row_cannot_bloat_the_file()
    {
        var long80 = new string('x', 200);
        var at = T;
        var c = Make(Corpse(ref at, long80, "z1", 1, "Item"));
        Assert.Equal(80, c.Drops[0].Mob.Length);
    }
}
