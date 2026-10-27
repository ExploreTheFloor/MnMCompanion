using System.Text.Json;
using MnMCompanion.Community;
using MnMCompanion.Data;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion.Tests;

/// <summary>
/// Schema 4: every row about play says which server it is from, so the community can tell servers apart (what players pay each other differs between them).
/// The totals and the running counters stay the player's across all servers, which is how the service checks they add up.
/// </summary>
public class ContributionServersTests
{
    private const string Id = "0123456789abcdef0123456789abcdef", Me = "Aldric";
    private static readonly DateTime Noon = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Made = new(2026, 10, 25);

    private static HarvestRow Harvested(string item, string zone, int qty = 1, int minutes = 0) => new(Noon.AddMinutes(minutes), Me, item, qty, zone);

    private static VendorRow Sale(string item, string zone, long copper = 40) => new(Noon, Me, VendorDirection.Sold, item, 1, copper, zone);

    private static TradeRow Gave(DateTime at, long coin, string zone, params (string Name, int Qty)[] items) =>
        new(at, Me, true, "ref_71358", coin, items.Select(i => new TradeLine(i.Name, i.Qty)).ToList(), zone);

    private static TradeRow Got(DateTime at, long coin, string zone) => new(at, Me, false, "ref_71358", coin, [], zone);

    private static ItemMoveRow Dropped(string item, int qty, string zone) => new(Noon, Me, ItemMoveKind.Dropped, item, qty, zone);

    /// <summary>A rat corpse looted on a server: an item row and a purse row (the shape the ledger gives).</summary>
    private static List<LootEvent> Rats(int corpses, string zone = "nightharborw")
    {
        var list = new List<LootEvent>();
        for (int i = 0; i < corpses; i++)
        {
            var at = Noon.AddMinutes(i * 3);
            list.Add(new LootEvent(at, LootKind.Item, Me, true, "a rat", "Rat Ear", 1, 0, 0, zone));
            list.Add(new LootEvent(at.AddSeconds(1), LootKind.Coin, Me, true, "a rat", "", 0, 5, 0, zone));
        }
        return list;
    }

    private static ServerRows On(string server, IReadOnlyList<LootEvent>? loot = null, IReadOnlyList<VendorRow>? vendor = null, IReadOnlyList<HarvestRow>? harvest = null,
        IReadOnlyList<TradeRow>? trades = null, IReadOnlyList<ItemMoveRow>? moves = null) => new(server, loot ?? [], vendor ?? [], harvest ?? [], trades ?? [], moves ?? []);

    private static Contribution Make(params ServerRows[] servers) => ContributionBuilder.Build(servers, [], Id, "MnMCompanion 0.5.0", Made);

    [Fact]
    public void Every_row_about_play_says_which_server_it_is_from_and_the_same_thing_on_two_servers_is_two_rows()
    {
        var c = Make(
            On("alpha", Rats(12), [Sale("Rat Ear", "nightharborw", 40)], [Harvested("Wood", "nightharborw", 2)], [Gave(Noon, 0, "nightharborw", ("Bat Wing", 3)), Got(Noon.AddSeconds(1), 90, "nightharborw")],
                [Dropped("Raw Beetle Meat", 4, "nightharborw")]),
            On("beta", Rats(8), [Sale("Rat Ear", "nightharborw", 60)], [Harvested("Wood", "nightharborw", 1)], [Gave(Noon.AddHours(1), 0, "nightharborw", ("Bat Wing", 3)), Got(Noon.AddHours(1).AddSeconds(1), 150, "nightharborw")],
                [Dropped("Raw Beetle Meat", 2, "nightharborw")]));

        Assert.Equal([("alpha", 12), ("beta", 8)], c.Drops.Select(d => (d.Server, d.Corpses)));   // the commonest first
        Assert.Equal(["alpha", "beta"], c.Prices.Select(p => p.Server));
        Assert.Equal(["40", "60"], c.Prices.Select(p => p.Sold.Keys.Single()));
        Assert.Equal([("alpha", 2), ("beta", 1)], c.Harvest.Select(h => (h.Server, h.Units)));
        Assert.Equal(["beta", "alpha"], c.Trades.Select(t => t.Server));                          // newest first, across the servers
        Assert.Equal([("alpha", 4), ("beta", 2)], c.Discards.Select(d => (d.Server, d.Dropped)));
        Assert.All(c.Drops.Select(d => d.Server).Concat(c.Prices.Select(p => p.Server)).Concat(c.Harvest.Select(h => h.Server)).Concat(c.Trades.Select(t => t.Server)).Concat(c.Discards.Select(d => d.Server)),
            server => Assert.NotEqual("", server));
        Assert.Equal(["alpha", "beta"], ContributionBuilder.ServersOf(c));
    }

    [Fact]
    public void The_totals_and_the_running_counters_are_across_all_servers_so_they_still_add_up_to_the_lists()
    {
        var c = Make(
            On("alpha", Rats(12), [Sale("Rat Ear", "z1")], [Harvested("Wood", "z1", 2)]),
            On("beta", Rats(8), [Sale("Rat Ear", "z1"), Sale("Rat Ear", "z1", 60)], [Harvested("Wood", "z1", 1)]));

        Assert.Equal(new ContributionTotals(2, 20, 3, 0, 2, 0), c.Totals);
        var all = ContributionBuilder.Lifetime(c.Counts);
        Assert.Equal((20L, 20L, 20L * 5, 20L, 2L, 3L, 3L), (all.Corpses, all.Purses, all.Copper, all.Units, all.Harvests, all.HarvestUnits, all.Sold));
        Assert.Equal(c.Drops.Sum(d => d.Corpses), all.Corpses);
        Assert.Equal(c.Drops.Sum(d => d.Items.Sum(i => i.Units)), all.Units);
        Assert.Equal(c.Prices.Sum(p => p.Sold.Values.Sum()), all.Sold);
        Assert.Equal(c.Harvest.Sum(h => h.Harvests), all.Harvests);
    }

    [Fact]
    public void A_server_is_one_server_whatever_its_case_and_one_with_no_usable_name_is_shared_as_unknown()
    {
        var c = Make(
            On("Trem", Rats(3)), On("  trem  ", Rats(2)),
            On("", Rats(4)), On("bad\u0007name", Rats(5)), On("x" + new string('y', 100), Rats(6)));

        Assert.Equal(
            [("unknown", 9), ("x" + new string('y', 79), 6), ("Trem", 5)],   // a name over 80 characters is cut to 80, like every other name
            c.Drops.Select(d => (d.Server, d.Corpses)));
        Assert.True(c.Drops.All(d => d.Server.Length is >= 1 and <= 80));
    }

    [Fact]
    public void The_newest_trades_are_kept_across_the_servers_up_to_the_limit()
    {
        var alpha = Enumerable.Range(0, 2000).SelectMany(i => new[] { Gave(Noon.AddMinutes(2 * i), 0, "z", ("Bat Wing", 1)), Got(Noon.AddMinutes(2 * i).AddSeconds(1), 5, "z") }).ToList();
        var beta = Enumerable.Range(0, 2000).SelectMany(i => new[] { Gave(Noon.AddMinutes(2 * i + 1), 0, "z", ("Rat Ear", 1)), Got(Noon.AddMinutes(2 * i + 1).AddSeconds(1), 5, "z") }).ToList();

        var c = Make(On("alpha", trades: alpha), On("beta", trades: beta));

        Assert.Equal(ContributionBuilder.MaxTrades, c.Trades.Count);
        Assert.Equal("beta", c.Trades[0].Server);                  // the very newest
        Assert.Equal(1500, c.Trades.Count(t => t.Server == "alpha"));   // 3000 newest of the 4000, taken alternately
        Assert.Equal(1500, c.Trades.Count(t => t.Server == "beta"));
    }

    [Fact]
    public void A_server_with_nothing_adds_no_rows_but_an_empty_ledger_has_no_servers_at_all()
    {
        Assert.Equal(["alpha"], ContributionBuilder.ServersOf(Make(On("alpha", Rats(1)), On("beta"))));
        var none = Make();
        Assert.Empty(ContributionBuilder.ServersOf(none));
        Assert.StartsWith("No server names", ContributionBuilder.Describe(none)[0]);
    }

    [Fact]
    public void The_file_names_the_server_on_every_row_that_has_one_and_nowhere_else()
    {
        var c = Make(On("alpha", Rats(12), [Sale("Rat Ear", "z1")], [Harvested("Wood", "z1")], [Gave(Noon, 0, "z1", ("Bat Wing", 3)), Got(Noon.AddSeconds(1), 9, "z1")], [Dropped("Raw Beetle Meat", 4, "z1")]));

        using var core = JsonDocument.Parse(c.ToCoreJson());
        var root = core.RootElement;
        Assert.Equal(4, root.GetProperty("schema").GetInt32());
        foreach (var section in new[] { "drops", "prices", "harvest", "discards" })
            Assert.All(root.GetProperty(section).EnumerateArray(), row => Assert.Equal("alpha", row.GetProperty("server").GetString()));
        Assert.False(root.TryGetProperty("server", out _), "the server is on the rows, not on the file");
        Assert.All(root.GetProperty("items").EnumerateArray(), row => Assert.False(row.TryGetProperty("server", out _)));

        using var part = JsonDocument.Parse(c.ToTradesPartJson());
        Assert.Equal(4, part.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal("alpha", part.RootElement.GetProperty("trades")[0].GetProperty("server").GetString());

        using var npcs = JsonDocument.Parse(c.ToNpcPartJson());
        Assert.DoesNotContain("alpha", npcs.RootElement.GetRawText());
    }

    [Fact]
    public void The_preview_says_which_servers_are_in_the_file()
    {
        var c = Make(On("beta", Rats(2)), On("alpha", Rats(3)));
        var lines = ContributionBuilder.Describe(c);
        Assert.Equal("2 servers the rows below are from: alpha, beta (the game's own name for each; every row below says which one it is from)", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("0 trades with other players") && l.Contains("the server"));
        Assert.Equal("The server the rows below are from: alpha (the game's own name for each; every row below says which one it is from)",
            ContributionBuilder.Describe(Make(On("alpha", Rats(1))))[0]);
    }

    [Fact]
    public void A_file_saved_before_servers_were_shared_reads_back_with_no_server()
    {
        var old = """
            {"schema":3,"contributor":"0123456789abcdef0123456789abcdef","app":"x","generated":"2026-10-25","license":"CC-BY-NC-SA-4.0",
             "totals":{"monsters":1,"corpses":2,"sales":0,"purchases":0,"harvests":0,"items":0},
             "drops":[{"mob":"a rat","zone":"z","corpses":2,"purses":0,"copper":0,"items":[]}],"prices":[],"harvest":[],"items":[],"npcs":[],
             "counts":{"before":{"corpses":2,"purses":0,"copper":0,"units":0,"harvests":0,"harvestUnits":0,"sold":0,"soldCopper":0,"bought":0,"boughtCopper":0,"trades":0},"weeks":[]},
             "trades":[],"discards":[]}
            """;
        var c = Contribution.FromJson(old)!;
        Assert.Equal("", Assert.Single(c.Drops).Server);
        Assert.Empty(ContributionBuilder.ServersOf(c));
    }

    // ---- from a real database: a server is a folder of the game's, with its own characters

    private static readonly DateTime T0 = new(2026, 10, 25, 20, 0, 0, DateTimeKind.Local);

    private static void Character(Fixture.Scratch s, string server, string who, int corpses)
    {
        var rows = new List<string>();
        for (int i = 0; i < corpses; i++)
        {
            var at = T0.AddMinutes(i);
            rows.Add(Fixture.Purse(at, who, who, "a rat", "0,0,0,5"));
            rows.Add(Fixture.Item(at.AddSeconds(1), who, who, "a rat", "Rat Ear"));
        }
        rows.Add(Fixture.Sale(T0.AddHours(1), who, "Rat Ear", 6, "0 platinum 0 gold 0 silver 24 copper"));
        s.Write(Path.Combine(server, who, "Ledger", $"{who}_Character_2026-10-25.json"), Fixture.File(rows.ToArray()));
    }

    [Fact]
    public void The_export_builds_each_server_from_its_own_characters()
    {
        using var s = new Fixture.Scratch();
        Character(s, "alpha", "Aldric", 12);
        Character(s, "alpha", "Wendell", 3);   // two characters on a server are added together, as they always were
        Character(s, "beta", "Brannoch", 8);
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        LedgerArchiver.Run(db, s.Path);
        EventBuilder.Update(db);

        Assert.Equal(["alpha", "beta"], new HistoryStore(db).Servers());
        var c = CommunityExport.Build(db, "MnMCompanion test", Made);

        Assert.Equal([("alpha", 15), ("beta", 8)], c.Drops.Select(d => (d.Server, d.Corpses)));
        Assert.Equal([("alpha", 2), ("beta", 1)], c.Prices.Select(p => (p.Server, p.Sold["4"])));
        Assert.Equal(23, c.Totals.Corpses);
        Assert.Equal(23, ContributionBuilder.Lifetime(c.Counts).Corpses);
        var json = c.ToJson();
        Assert.DoesNotContain("Aldric", json);
        Assert.DoesNotContain("Brannoch", json);
    }

    [Fact]
    public void A_player_on_one_server_shares_that_servers_name_on_every_row()
    {
        using var s = new Fixture.Scratch();
        Character(s, "alpha", "Aldric", 5);
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        LedgerArchiver.Run(db, s.Path);
        EventBuilder.Update(db);

        var c = CommunityExport.Build(db, "MnMCompanion test", Made);

        Assert.Equal("alpha", Assert.Single(c.Drops).Server);
        Assert.Equal("alpha", Assert.Single(c.Prices).Server);
    }

    // ---- consent: sharing the server is something new, so a Yes from before has to be asked for again

    [Fact]
    public void A_yes_to_the_layout_before_servers_no_longer_covers_what_is_shared()
    {
        Assert.Equal(4, ContributionBuilder.Schema);
        Assert.True(CommunityPolicy.NeedsRenewal(true, 3, ContributionBuilder.Schema));
        Assert.False(CommunityPolicy.Consented(true, 3, ContributionBuilder.Schema));
        Assert.True(CommunityPolicy.Consented(true, 4, ContributionBuilder.Schema));
    }
}
