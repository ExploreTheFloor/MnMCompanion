using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using static MnMCompanion.Tests.Fixture;

namespace MnMCompanion.Tests;

/// <summary>
/// A player joining the group starts a new session: the store notices party joins in the ledger as they appear (never the ones already there when it
/// starts looking), and a session can start as of the join, archiving the loot before it.
/// </summary>
public class PartyJoinSessionTests
{
    private const string Me = "Aldric";

    private static string Join(DateTime at, string who) =>
        Row("act_32", "name_" + B64(who), new { d61 = "", d62 = 3, d63 = B64("PartyJoin"), d64 = "ref_9" }, at, who, Me);

    private sealed class World : IDisposable
    {
        public Scratch Scratch { get; } = new();
        public LootStore Store { get; }
        private readonly string _social;
        private readonly string _character;

        public World(string[] character, string[] social)
        {
            _character = "ledger\\srv\\Aldric\\Ledger\\Aldric_Character_x.json";
            _social = "ledger\\srv\\Aldric\\Ledger\\Aldric_Social_x.json";
            Scratch.Write(_character, File(character));
            Scratch.Write(_social, File(social));
            Store = new LootStore(Path.Combine(Scratch.Path, "state"), Path.Combine(Scratch.Path, "ledger"));
            Store.Select(Store.Sources.Single(s => s.Character == Me));
            Store.Refresh();
        }

        /// <summary>The ledger grows: the party file is rewritten with more rows (the game appends to it).</summary>
        public void Social(params string[] rows)
        {
            Scratch.Write(_social, File(rows));
            Store.Refresh();
        }

        public void Dispose() => Scratch.Dispose();
    }

    // Kept inside today, clear of midnight, like the store's own tests
    private static DateTime Now => DateTime.Now.Hour == 0 && DateTime.Now.Minute < 40 ? DateTime.Now.Date.AddMinutes(40) : DateTime.Now;

    [Fact]
    public void Joins_already_in_the_ledger_never_count_and_a_later_one_does_once()
    {
        var now = Now;
        using var w = new World([Purse(now.AddMinutes(-20), Me, Me, "a rat", "0,0,0,30")], [Join(now.AddMinutes(-10), "Brom")]);

        Assert.Empty(w.Store.NewJoins(now));                                   // the first look only notes where the ledger stands

        w.Social(Join(now.AddMinutes(-10), "Brom"), Join(now.AddMinutes(-1), "Pip"));
        var join = Assert.Single(w.Store.NewJoins(now));
        Assert.Equal("Pip", join.Who);
        Assert.False(join.You);
        Assert.Empty(w.Store.NewJoins(now));                                   // and not again
    }

    [Fact]
    public void Your_own_join_is_marked_and_a_stale_one_is_dropped()
    {
        var now = Now;
        using var w = new World([], []);
        w.Store.NewJoins(now);

        w.Social(Join(now.AddMinutes(-45), "Brom"), Join(now.AddSeconds(-30), Me));   // one turned up long after it happened, one is fresh and yours
        var join = Assert.Single(w.Store.NewJoins(now));
        Assert.True(join.You);
        Assert.Equal(Me, join.Who);
    }

    [Fact]
    public void Another_characters_joins_are_ignored_and_selecting_again_forgets_what_was_seen()
    {
        var now = Now;
        using var w = new World([], []);
        w.Store.NewJoins(now);
        w.Social(Row("act_32", "name_" + B64("Pip"), new { d61 = "", d62 = 3, d63 = B64("PartyJoin"), d64 = "ref_9" }, now.AddMinutes(-1), "Pip", "Wendell"));
        Assert.Empty(w.Store.NewJoins(now));                                   // a row in Aldric's file that belongs to somebody else's ledger

        w.Social(Join(now.AddMinutes(-1), "Pip"));
        w.Store.Select(w.Store.Sources.Single(s => s.Character == Me));        // as when the character being followed changes
        w.Store.Refresh();
        Assert.Empty(w.Store.NewJoins(now));                                   // the join is old news again: it was there when the store looked
    }

    [Fact]
    public void A_session_can_start_as_of_the_join_archiving_the_loot_before_it()
    {
        var now = Now;
        var join = now.AddMinutes(-5);
        using var w = new World(
            [Purse(now.AddMinutes(-30), Me, Me, "a rat", "0,0,0,30"), Item(now.AddMinutes(-29), Me, Me, "a rat", "Rat Ear"),
             Item(now.AddMinutes(-2), Me, Me, "a bat", "Bat Wing")], []);
        var day = DateOnly.FromDateTime(join);

        var closed = w.Store.NewSession("party", join);

        Assert.NotNull(closed);
        Assert.Equal(join, w.Store.Sessions.Current.Start);
        Assert.Equal("party", w.Store.Sessions.Current.Reason);
        Assert.Equal(2, w.Store.Events(day, closed).Count);                    // the purse and the ear, before the join
        Assert.Single(w.Store.Events(day, w.Store.Sessions.Current));          // the bat wing, after it
        var archive = Directory.GetFiles(Path.Combine(w.Store.StateFolder!, "sessions"), "*.txt").Single();
        Assert.Contains("Rat Ear", System.IO.File.ReadAllText(archive));
        Assert.DoesNotContain("Bat Wing", System.IO.File.ReadAllText(archive));
    }

    [Fact]
    public void A_join_starts_nothing_when_nothing_was_looted_before_it_or_a_session_started_since()
    {
        var now = Now;
        using var w = new World([Item(now.AddMinutes(-2), Me, Me, "a bat", "Bat Wing")], []);

        Assert.Null(w.Store.NewSession("party", now.AddMinutes(-5)));          // nothing looted before the join
        Assert.Empty(w.Store.Sessions.Marks);

        Assert.NotNull(w.Store.NewSession("party", now.AddMinutes(-1)));       // the bat wing is before this one
        Assert.Null(w.Store.NewSession("party", now.AddMinutes(-5)));          // a join the ledger reports late, from before the session that has started since
        Assert.Null(w.Store.NewSession("party", now.AddSeconds(-58)));         // a second join right behind: nothing looted in between
        Assert.Single(w.Store.Sessions.Marks);
    }
}
