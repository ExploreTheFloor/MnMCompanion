using System.Text.Json;
using MnMCompanion.Community;
using MnMCompanion.History;

namespace MnMCompanion.Tests;

/// <summary>What the player dropped and destroyed (schema 3): a tally per item and zone, with how much of the dropped was picked up again soon after.</summary>
public class ContributionDiscardsTests
{
    private const string Id = "0123456789abcdef0123456789abcdef", Me = "Aldric", Server = ContributionBuilder.UnknownServer;   // rows built without a server are given this one
    private static readonly DateTime Noon = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static ItemMoveRow Move(ItemMoveKind kind, string item, int qty, string zone = "nightharborw", int minutes = 0, string who = Me) =>
        new(Noon.AddMinutes(minutes), who, kind, item, qty, zone);

    private static Contribution Make(params ItemMoveRow[] moves) =>
        ContributionBuilder.Build([], [], [], [], Id, "MnMCompanion 0.4.0", new DateOnly(2026, 10, 25), null, null, moves);

    [Fact]
    public void Drops_and_destroys_are_added_up_in_units_per_item_and_zone()
    {
        var c = Make(
            Move(ItemMoveKind.Dropped, "Raw Beetle Meat", 4, "z1"),
            Move(ItemMoveKind.Dropped, "Raw Beetle Meat", 2, "z1", 60),
            Move(ItemMoveKind.Destroyed, "Raw Beetle Meat", 1, "z1", 90),
            Move(ItemMoveKind.Destroyed, "Rusty Dagger", 3, "z2"),
            Move(ItemMoveKind.Dropped, "Raw Beetle Meat", 7, "z2"));

        Assert.Equal(
            [new ContributionDiscard("Raw Beetle Meat", "z1", 6, 1, 0, Server), new ContributionDiscard("Raw Beetle Meat", "z2", 7, 0, 0, Server), new ContributionDiscard("Rusty Dagger", "z2", 0, 3, 0, Server)],
            c.Discards.OrderBy(d => d.Item).ThenBy(d => d.Zone));
    }

    [Fact]
    public void A_pickup_soon_after_a_drop_is_taken_back_against_the_zone_of_the_drop_and_never_more_than_was_dropped()
    {
        var c = Make(
            Move(ItemMoveKind.Dropped, "Cloth Scraps", 5, "z1"),
            Move(ItemMoveKind.PickedUp, "Cloth Scraps", 3, "z2", 4),     // picked up elsewhere a few minutes later: still taken back, against where it was dropped
            Move(ItemMoveKind.Dropped, "Bone Chips", 2, "z1"),
            Move(ItemMoveKind.PickedUp, "Bone Chips", 9, "z1", 3));      // more picked up than was dropped: only the dropped ones are taken back

        var cloth = Assert.Single(c.Discards, d => d.Item == "Cloth Scraps");
        Assert.Equal((5, 0, 3), (cloth.Dropped, cloth.Destroyed, cloth.TakenBack));
        var bone = Assert.Single(c.Discards, d => d.Item == "Bone Chips");
        Assert.Equal((2, 0, 2), (bone.Dropped, bone.Destroyed, bone.TakenBack));
        Assert.All(c.Discards, d => Assert.True(d.TakenBack <= d.Dropped));
    }

    [Fact]
    public void A_pickup_long_after_a_drop_or_by_another_character_is_not_taking_it_back()
    {
        var late = Make(Move(ItemMoveKind.Dropped, "Bone Chips", 2, "z1"), Move(ItemMoveKind.PickedUp, "Bone Chips", 2, "z1", 30));
        Assert.Equal(0, Assert.Single(late.Discards).TakenBack);

        var other = Make(Move(ItemMoveKind.Dropped, "Cloth Scraps", 5, "z1", 0, "Aldric"), Move(ItemMoveKind.PickedUp, "Cloth Scraps", 5, "z1", 2, "Brannoch"));
        Assert.Equal(0, Assert.Single(other.Discards).TakenBack);
    }

    [Fact]
    public void An_item_only_picked_up_is_not_a_discard_and_nothing_without_a_clean_name_is_kept()
    {
        var c = Make(
            Move(ItemMoveKind.PickedUp, "Found Thing", 3),
            Move(ItemMoveKind.Dropped, "   ", 1),
            Move(ItemMoveKind.Dropped, "bad\u0007name", 1),
            Move(ItemMoveKind.Dropped, "Real Thing", 0),
            Move(ItemMoveKind.Dropped, "Kept Thing", 1));

        Assert.Equal(["Kept Thing"], c.Discards.Select(d => d.Item));
    }

    [Fact]
    public void The_same_item_in_another_case_is_one_item_and_a_missing_zone_is_unknown()
    {
        var c = Make(Move(ItemMoveKind.Dropped, "Bat Wing", 2, "z1"), Move(ItemMoveKind.Dropped, "bat wing", 3, "Z1"), Move(ItemMoveKind.Destroyed, "Bat Wing", 1, ""), Move(ItemMoveKind.Destroyed, "Bat Wing", 1, "bad\u0007zone"));

        Assert.Equal(2, c.Discards.Count);
        Assert.Equal((5, 0), c.Discards.Where(d => d.Zone.Equals("z1", StringComparison.OrdinalIgnoreCase)).Select(d => (d.Dropped, d.Destroyed)).Single());
        Assert.Equal((0, 2), c.Discards.Where(d => d.Zone == "unknown").Select(d => (d.Dropped, d.Destroyed)).Single());
    }

    [Fact]
    public void The_most_thrown_away_comes_first_and_a_file_holds_at_most_five_thousand()
    {
        var moves = Enumerable.Range(0, ContributionBuilder.MaxDiscards + 30).Select(i => Move(ItemMoveKind.Dropped, $"Item {i:0000}", i + 1, "z1")).ToArray();

        var c = Make(moves);

        Assert.Equal(ContributionBuilder.MaxDiscards, c.Discards.Count);
        Assert.Equal(ContributionBuilder.MaxDiscards + 30, c.Discards[0].Dropped);
        Assert.True(c.Discards.Zip(c.Discards.Skip(1), (a, b) => a.Dropped + a.Destroyed >= b.Dropped + b.Destroyed).All(x => x));
    }

    [Fact]
    public void The_discards_are_in_the_totals_file_with_exactly_the_fields_the_service_reads_and_in_the_preview()
    {
        var c = Make(Move(ItemMoveKind.Dropped, "Raw Beetle Meat", 4, "z1"), Move(ItemMoveKind.PickedUp, "Raw Beetle Meat", 1, "z1", 2));

        using var doc = JsonDocument.Parse(c.ToCoreJson());
        var entry = doc.RootElement.GetProperty("discards")[0];
        Assert.Equal(["item", "zone", "dropped", "destroyed", "takenBack", "server"], entry.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, entry.GetProperty("takenBack").GetInt32());
        using var part = JsonDocument.Parse(c.ToTradesPartJson());
        Assert.False(part.RootElement.TryGetProperty("discards", out _), "the trades part carries only trades");
        Assert.Contains(ContributionBuilder.Describe(c), l => l.StartsWith("1 items you dropped or destroyed"));
    }

    [Fact]
    public void A_change_in_what_was_discarded_changes_the_totals_file_and_leaves_the_trades_alone()
    {
        var a = Make(Move(ItemMoveKind.Dropped, "Bat Wing", 2));
        var b = Make(Move(ItemMoveKind.Dropped, "Bat Wing", 2));
        var more = Make(Move(ItemMoveKind.Dropped, "Bat Wing", 3));
        Assert.Equal(SharePlan.Fingerprint(a), SharePlan.Fingerprint(b));
        Assert.NotEqual(SharePlan.Fingerprint(a), SharePlan.Fingerprint(more));
        Assert.Equal(SharePlan.TradesFingerprint(a), SharePlan.TradesFingerprint(more));
    }

    [Fact]
    public void Nothing_dropped_or_destroyed_gives_an_empty_list_that_still_describes_itself()
    {
        var c = Make();
        Assert.Empty(c.Discards);
        Assert.Contains(ContributionBuilder.Describe(c), l => l.StartsWith("0 items you dropped or destroyed"));
    }

    [Fact]
    public void The_taken_back_figure_the_pages_show_is_the_sum_of_the_per_zone_figures()
    {
        ItemMoveRow[] rows =
        [
            Move(ItemMoveKind.Dropped, "A", 5, "z1"), Move(ItemMoveKind.Dropped, "A", 4, "z2", 1), Move(ItemMoveKind.PickedUp, "A", 7, "z3", 2),
            Move(ItemMoveKind.Dropped, "B", 2, "z1", 3), Move(ItemMoveKind.PickedUp, "B", 2, "z1", 40),
        ];

        var perZone = ItemChangeSummary.TakenBackByItemAndZone(rows);

        Assert.Equal(7, perZone.Values.Sum());
        Assert.Equal(5, perZone[("a", "z1")]);
        Assert.Equal(2, perZone[("a", "z2")]);
        Assert.Equal(ItemChangeSummary.Totals(rows).TakenBack, perZone.Values.Sum());
    }
}
