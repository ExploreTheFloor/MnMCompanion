using MnMCompanion.History;
using MnMCompanion.Loot;
using Scratch = MnMCompanion.Tests.Fixture.Scratch;

namespace MnMCompanion.Tests;

/// <summary>The zone the character is in, the map that goes with it, and a session as a stretch of time.</summary>
public class ZoneAndSessionTests
{
    private static readonly DateTime T = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Local);

    [Fact]
    public void A_zone_reads_as_a_name_a_person_would_say()
    {
        Assert.Equal("Fallen Pass", ZoneMaps.DisplayName("fallenpass"));
        Assert.Equal("Night Harbor (west)", ZoneMaps.DisplayName("nightharborw"));
        Assert.Equal("Tel Ekir", ZoneMaps.DisplayName("telekir"));
        Assert.Equal("Keeper's Bight", ZoneMaps.DisplayName("keepersbight"));
        Assert.Equal("Somewhereelse", ZoneMaps.DisplayName("somewhereelse"));   // not known: the game's spelling with a capital
        Assert.Equal("", ZoneMaps.DisplayName(""));
    }

    [Fact]
    public void A_session_is_the_part_of_the_day_it_covers()
    {
        var day = DateOnly.FromDateTime(T);
        var midnight = day.ToDateTime(TimeOnly.MinValue);

        var whole = SessionPeriods.Of(day, null);
        Assert.Equal((midnight, midnight.AddDays(1)), (whole.From, whole.To));

        var open = SessionPeriods.Of(day, new LootSession(T, null, "manual"));
        Assert.Equal((T, midnight.AddDays(1)), (open.From, open.To));            // still going: to the end of the day

        var closed = SessionPeriods.Of(day, new LootSession(T, T.AddHours(1), "manual"));
        Assert.Equal((T, T.AddHours(1)), (closed.From, closed.To));

        var first = SessionPeriods.Of(day, new LootSession(DateTime.MinValue, T, ""));   // the first session of all has no start of its own
        Assert.Equal((midnight, T), (first.From, first.To));

        var spill = SessionPeriods.Of(day, new LootSession(T.AddDays(-1), T.AddDays(3), ""));   // began yesterday, ends after today
        Assert.Equal((midnight, midnight.AddDays(1)), (spill.From, spill.To));
        Assert.True(open.Contains(T.AddMinutes(5)) && !open.Contains(T.AddMinutes(-5)));
    }

    [Fact]
    public void The_store_knows_the_zone_of_the_characters_newest_row_and_follows_it_to_the_next()
    {
        using var s = new Scratch();
        string Own(string zone, DateTime at) => Fixture.Row("act_13", "ref_1", new { d01 = 1, d02 = Fixture.B64("a rat"), d03 = "", d04 = "Rat Ear", d05 = "9a818e77c3484fe98efd9081" }, at, "Aldric", "Aldric", zone: zone);
        var path = s.Write(@"srv\Aldric\Ledger\Aldric_Character_2026-10-25.json", Fixture.File(Own("fallenpass", T), Own("valeofzintar", T.AddMinutes(5))));
        var store = new LootStore(Path.Combine(s.Path, "state"), s.Path);
        store.Select(store.Sources.Single());

        store.Refresh();
        Assert.Equal(("valeofzintar", T.AddMinutes(5)), (store.CurrentZone?.Zone, store.CurrentZone?.At));

        File.WriteAllText(path, Fixture.File(Own("fallenpass", T), Own("valeofzintar", T.AddMinutes(5)), Own("shadeddunes", T.AddMinutes(9))));
        store.Refresh();
        Assert.Equal("shadeddunes", store.CurrentZone?.Zone);
    }

    [Fact]
    public void A_character_with_no_ledger_is_in_no_known_zone()
    {
        using var s = new Scratch();
        s.Write(@"srv\Fresh\chats.json", "{}");
        var store = new LootStore(Path.Combine(s.Path, "state"), s.Path);
        store.Select(store.Sources.Single());
        store.Refresh();
        Assert.Null(store.CurrentZone);
    }
}
