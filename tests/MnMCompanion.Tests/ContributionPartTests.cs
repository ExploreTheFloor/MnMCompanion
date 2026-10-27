using System.Text.Json;
using MnMCompanion.Community;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion.Tests;

/// <summary>
/// What is sent is two documents: the totals (compact, without the NPC dialogue) and the dialogue as a "part" of its own. Together they hold
/// what the one big file held, and neither is indented.
/// </summary>
public class ContributionPartTests
{
    private const string Id = "0123456789abcdef0123456789abcdef";
    private static readonly DateTime Noon = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static Contribution Make(bool dialogue = true)
    {
        var loot = new[]
        {
            new LootEvent(Noon, LootKind.Item, "Aldric", true, "a rat", "Rat Ear", 1, 0, 0, "z1"),
            new LootEvent(Noon.AddSeconds(-1), LootKind.Coin, "Aldric", true, "a rat", "", 0, 5, 0, "z1"),
        };
        ContributionNpc[]? npcs = dialogue
            ? [new ContributionNpc("Abbot Harrow", ["Well met, {player}.", "Brom's cart is late."]), new ContributionNpc("Hull Orbel", ["Mind the road."])]
            : null;
        return ContributionBuilder.Build(loot, [], [new HarvestRow(Noon, "Aldric", "Wood", 2, "z1")], [], Id, "MnMCompanion 0.3.1", new DateOnly(2026, 10, 25), npcs);
    }

    [Fact]
    public void The_totals_file_has_no_dialogue_and_no_indentation()
    {
        var c = Make();

        var core = c.ToCoreJson();

        Assert.DoesNotContain("\n", core);
        Assert.DoesNotContain("  ", core);
        using var doc = JsonDocument.Parse(core);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["schema", "contributor", "app", "generated", "license", "totals", "drops", "prices", "harvest", "items", "counts", "discards"], keys);
        Assert.Equal(3, doc.RootElement.GetProperty("schema").GetInt32());
        Assert.True(core.Length < c.ToJson().Length * 0.7, "much smaller than the indented whole");
    }

    [Fact]
    public void The_dialogue_is_a_part_with_the_same_id_and_nothing_else_in_it()
    {
        var c = Make();

        using var doc = JsonDocument.Parse(c.ToNpcPartJson());

        var root = doc.RootElement;
        Assert.Equal(["schema", "part", "contributor", "app", "generated", "license", "npcs"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal((3, "npcs", Id, "2026-10-25", "CC-BY-NC-SA-4.0"),
            (root.GetProperty("schema").GetInt32(), root.GetProperty("part").GetString(), root.GetProperty("contributor").GetString(), root.GetProperty("generated").GetString(), root.GetProperty("license").GetString()));
        var npcs = root.GetProperty("npcs").EnumerateArray().ToList();
        Assert.Equal(["Abbot Harrow", "Hull Orbel"], npcs.Select(n => n.GetProperty("name").GetString()));
        Assert.Equal(["name", "lines"], npcs[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(["Well met, {player}.", "Brom's cart is late."], npcs[0].GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
        Assert.DoesNotContain("\n", c.ToNpcPartJson());
    }

    [Fact]
    public void Names_with_an_apostrophe_are_not_spelled_with_escapes()
    {
        var part = Make().ToNpcPartJson();

        Assert.Contains("Brom's cart", part);
        Assert.DoesNotContain("\\u0027", part);
    }

    [Fact]
    public void The_saved_copy_is_the_same_two_documents_laid_out_to_be_read()
    {
        var c = Make();

        string Squash(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);

        Assert.Contains("\n", c.ToCoreJson(indented: true));
        Assert.Equal(Squash(c.ToCoreJson()), Squash(c.ToCoreJson(indented: true)));
        Assert.Equal(Squash(c.ToNpcPartJson()), Squash(c.ToNpcPartJson(indented: true)));
    }

    [Fact]
    public void With_no_dialogue_the_part_is_empty_and_the_totals_are_the_same_file()
    {
        var talking = Make();
        var quiet = Make(dialogue: false);

        Assert.Empty(quiet.Npcs);
        Assert.Equal(talking.ToCoreJson(), quiet.ToCoreJson());   // the dialogue never was in the totals
        using var doc = JsonDocument.Parse(quiet.ToNpcPartJson());
        Assert.Equal(0, doc.RootElement.GetProperty("npcs").GetArrayLength());
    }

    [Fact]
    public void A_heavy_players_dialogue_has_a_whole_upload_to_itself()
    {
        // 300 NPCs with 100 lines of 100 characters each: a very long journal, and still well under the service's 1 MB for this one document
        var lines = Enumerable.Range(0, 100).Select(i => new string((char)('a' + i % 26), 100) + i).ToList();
        var many = Enumerable.Range(0, 300).Select(n => new ContributionNpc("Talker " + n, lines)).ToList();
        var c = ContributionBuilder.Build([], [], [], [], Id, "MnMCompanion 0.3.1", new DateOnly(2026, 10, 25), many);

        Assert.True(c.ToNpcPartJson().Length < 3_300_000);   // sanity: about 3 MB of text, far beyond any real journal (the biggest seen is ~100 KB)
        Assert.True(c.ToCoreJson().Length < 20_000, "the totals are the same size however much dialogue there is");
    }
}
