using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using static MnMCompanion.Tests.Fixture;

namespace MnMCompanion.Tests;

/// <summary>
/// The ledger writes a purse its owner looted as theirs whole, in a group or not. In a group the owner's autosplit divides it like anyone's, so the group's
/// coin takes the whole purse and the owner's coin only their share (which the ledger doesn't give, so it is estimated from the shares they got of others'
/// purses). Alone, the purse is theirs whole.
/// </summary>
public class OwnPurseTests
{
    private static readonly DateTime T = new(2026, 10, 25, 12, 0, 0);
    private const string Me = "Aldric";

    private static string Join(DateTime at, string who, int members) =>
        Row("act_32", "name_" + B64(who), new { d61 = "", d62 = members, d63 = B64("PartyJoin"), d64 = "ref_9" }, at, who, Me);

    private static string Leave(DateTime at, string who, int members) =>
        Row("act_34", "name_" + B64(who), new { d61 = "", d62 = members, d63 = B64("PartyLeave"), d64 = "ref_9" }, at, who, Me);

    private static string Disband(DateTime at) =>
        Row("act_33", "name_" + B64(Me), new { d61 = "", d62 = 3, d63 = B64("PartyDisband"), d64 = "ref_9" }, at, Me, Me);

    /// <summary>Social rows (the party's) and Character rows (the owner's own looting), read together as the app does.</summary>
    private static List<LootEvent> Extract(string[] social, string[] character)
    {
        var rows = Parse(File(social), LedgerFileKind.Social).Concat(Parse(File(character), LedgerFileKind.Character)).ToList();
        return LootExtractor.Extract(rows);
    }

    private static string[] OthersDivided(DateTime from) =>
    [
        // Brom's purses, divided: the owner got a fifth of each (20 of 100, 40 of 200)
        Purse(from, "Brom", Me, "a rat", "0,0,1,0", "0,0,0,20"),
        Purse(from.AddSeconds(30), "Brom", Me, "a rat", "0,0,2,0", "0,0,0,40"),
    ];

    [Fact]
    public void Alone_a_purse_you_looted_is_yours_whole()
    {
        var events = Extract([], [Purse(T, Me, Me, "a rat", "0,0,1,50")]);

        var e = Assert.Single(events);
        Assert.False(e.Shared);
        var day = LootDay.Of(DateOnly.FromDateTime(T), events);
        Assert.Equal(150, day.Ours.Copper);
        Assert.Equal(0, day.Ours.EstimatedShares);
    }

    [Fact]
    public void In_a_group_a_purse_you_looted_is_divided_and_only_your_estimated_share_is_yours()
    {
        var events = Extract([Join(T.AddMinutes(-30), Me, 2), .. OthersDivided(T.AddMinutes(-1))], [Purse(T, Me, Me, "a rat", "0,0,1,50")]);

        var own = events.Single(e => e.You && e.Copper == 150);
        Assert.True(own.Shared);
        var share = Assert.Single(own.Shares!);
        Assert.True(share.Estimated);
        Assert.Equal(30, share.Copper);                 // a fifth, as of Brom's purses
        Assert.Equal(30, own.Split);

        var day = LootDay.Of(DateOnly.FromDateTime(T), events);
        Assert.Equal(60 + 30, day.Ours.Copper);          // shares only: 20 + 40 of Brom's, 30 of yours
        Assert.Equal(1, day.Ours.EstimatedShares);
        Assert.True(day.Ours.Approximate);
        Assert.Equal(450, day.Totals.Copper);            // the group's coin takes every purse whole
    }

    [Fact]
    public void A_divided_purse_of_someone_elses_close_by_is_enough_to_be_in_a_group()
    {
        var near = Extract(OthersDivided(T.AddMinutes(-4)), [Purse(T, Me, Me, "a rat", "0,0,1,50")]);
        Assert.True(near.Single(e => e.You).Shared);

        var far = Extract(OthersDivided(T.AddMinutes(-40)), [Purse(T, Me, Me, "a rat", "0,0,1,50")]);
        Assert.False(far.Single(e => e.You).Shared);
    }

    [Fact]
    public void Leaving_or_disbanding_ends_it()
    {
        var left = Extract([Join(T.AddMinutes(-30), Me, 2), Leave(T.AddMinutes(-10), Me, 1)], [Purse(T, Me, Me, "a rat", "0,0,1,50")]);
        Assert.False(left.Single().Shared);

        var disbanded = Extract([Join(T.AddMinutes(-30), Me, 3), Disband(T.AddMinutes(-10))], [Purse(T, Me, Me, "a rat", "0,0,1,50")]);
        Assert.False(disbanded.Single().Shared);

        // someone else leaving a group of three leaves a group of two
        var stays = Extract([Join(T.AddMinutes(-30), Me, 3), Leave(T.AddMinutes(-10), "Brom", 2)], [Purse(T, Me, Me, "a rat", "0,0,1,50")]);
        Assert.True(stays.Single().Shared);
    }

    [Fact]
    public void With_no_purse_of_others_to_go_by_the_share_is_an_equal_part_of_the_group()
    {
        var events = Extract([Join(T.AddMinutes(-5), Me, 5)], [Purse(T, Me, Me, "a rat", "0,0,1,0")]);

        var own = Assert.Single(events);
        Assert.True(own.Shared);
        Assert.Equal(20, own.Split);                    // a fifth of 100
    }

    [Fact]
    public void The_fraction_follows_the_purses_nearest_in_time()
    {
        // Early the owner got a fifth of what others looted, later a tenth (the group grew): the purse at the end follows the later ones
        var early = new[]
        {
            Purse(T.AddHours(-3), "Brom", Me, "a rat", "0,0,2,0", "0,0,0,40"),
            Purse(T.AddHours(-3).AddSeconds(20), "Brom", Me, "a rat", "0,0,2,0", "0,0,0,40"),
        };
        var late = new[]
        {
            Purse(T.AddMinutes(-2), "Brom", Me, "a rat", "0,0,2,0", "0,0,0,20"),
            Purse(T.AddMinutes(-1), "Brom", Me, "a rat", "0,0,2,0", "0,0,0,20"),
        };
        var events = Extract([Join(T.AddHours(-4), Me, 2), .. early, .. late], [Purse(T, Me, Me, "a rat", "0,0,1,0")]);

        // 12 purses each side are used, but only those within two hours: the early ones are three hours away
        Assert.Equal(10, events.Single(e => e.You).Split);
    }

    [Fact]
    public void Two_characters_of_one_player_do_not_estimate_over_a_share_the_ledger_shows()
    {
        // Aldric looted it (his ledger can't say what he got); Brindlewick, in the same group, has the exact share in his
        var aldric = Extract([Join(T.AddMinutes(-5), Me, 2)], [Purse(T, Me, Me, "a rat", "0,0,1,0")]);
        var brindlewick = LootExtractor.Extract(Parse(File(Purse(T, Me, "Brindlewick", "a rat", "0,0,1,0", "0,0,0,13")), LedgerFileKind.Social));

        var merged = LootMerge.Combine([aldric, brindlewick]);

        var one = Assert.Single(merged);
        Assert.Contains(one.Shares!, s => s.Owner == "Brindlewick" && s.Copper == 13 && !s.Estimated);
        Assert.Contains(one.Shares!, s => s.Owner == Me && s.Estimated);
    }

    [Fact]
    public void A_purse_row_that_does_not_name_its_creature_gets_a_placeholder_not_the_word_party_split()
    {
        // The game's build of 2026-10-27 writes "party_split" (or nothing) where the creature's name was
        string Unnamed(string who, string d14) => Row("act_14", "name_" + B64(d14 == "party_split" ? "party_split" : ""), new { d11 = 1, d12 = B64("0,0,1,0"), d13 = B64(d14 == "party_split" ? "party_split" : ""), d14 = B64(d14), d15 = d14 == "party_split" ? B64("0,0,0,20") : "" }, T, who, Me);
        var events = LootExtractor.Extract(Parse(File(Unnamed("Brom", "party_split"), Unnamed(Me, "npc_corpse")), LedgerFileKind.Social));

        Assert.All(events, e => Assert.Equal(LootExtractor.UnknownCreature, e.Source));
        Assert.Equal(2, LootDay.Of(DateOnly.FromDateTime(T), events).Unrecorded);   // two corpses whose items the ledger left out
        Assert.Equal("", LootExtractor.PurseSource("party_split", "npc_corpse"));
        Assert.Equal("a rat", LootExtractor.PurseSource("", "a rat"));
        Assert.Equal("a rat", LootExtractor.PurseSource("a rat", "party_split"));
    }

    [Fact]
    public void A_player_whose_items_the_ledger_left_out_reads_as_not_recorded_not_as_none()
    {
        var unknown = LootExtractor.UnknownCreature;
        var day = LootDay.Of(DateOnly.FromDateTime(T),
        [
            new LootEvent(T, LootKind.Coin, "Brom", false, unknown, "", 0, 100, 0),
            new LootEvent(T.AddSeconds(30), LootKind.Coin, "Brom", false, unknown, "", 0, 100, 0),
            new LootEvent(T.AddSeconds(60), LootKind.Coin, "Pip", false, unknown, "", 0, 100, 0),
            new LootEvent(T.AddSeconds(120), LootKind.Coin, "Pip", false, "a rat", "", 0, 5, 0),
            new LootEvent(T.AddSeconds(121), LootKind.Item, "Pip", false, "a rat", "Rat Ear", 1, 0, 0),
        ]);

        var brom = day.Member("Brom")!;
        Assert.Equal(2, brom.Unrecorded);
        Assert.Equal("items not recorded", LootSummary.ItemsOf(brom));
        Assert.Equal("—", LootSummary.ItemsWorthOf(brom));

        var pip = day.Member("Pip")!;
        Assert.Equal(1, pip.Unrecorded);
        Assert.Equal("1+ items", LootSummary.ItemsOf(pip));
        Assert.EndsWith("+", LootSummary.ItemsWorthOf(pip));

        Assert.Equal(3, day.Unrecorded);
        Assert.Equal(0, LootDay.Of(DateOnly.FromDateTime(T), [new LootEvent(T, LootKind.Item, "Pip", false, "a rat", "Rat Ear", 1, 0, 0)]).Member("Pip")!.Unrecorded);
        Assert.Equal("1 item", LootSummary.ItemsOf(LootDay.Of(DateOnly.FromDateTime(T), [new LootEvent(T, LootKind.Item, "Pip", false, "a rat", "Rat Ear", 1, 0, 0)]).Member("Pip")!));
    }
}
