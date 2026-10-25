using MnMCompanion.Data;
using MnMCompanion.History;
using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using Scratch = MnMCompanion.Tests.Fixture.Scratch;

namespace MnMCompanion.Tests;

/// <summary>The History window looking at everyone, one server or one character.</summary>
public class ScopeTests
{
    private static readonly DateTime T = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Local);

    private static LedgerSource Of(string server, string character) => new(server, character, $@"c:\game\{server}\{character}", T);

    // ------------------------------------------------------------------ the scope itself

    [Fact]
    public void A_scope_is_everyone_a_server_or_a_character_and_a_character_needs_its_server()
    {
        var sources = new[] { Of("alpha", "Aldric"), Of("alpha", "Brannoch"), Of("beta", "Aldric") };

        Assert.Equal(3, HistoryScope.Everyone.Pick(sources).Count);
        Assert.Equal(["Aldric", "Brannoch"], HistoryScope.Of("Alpha").Pick(sources).Select(s => s.Character));   // the same server whatever the case
        Assert.Equal([("beta", "Aldric")], HistoryScope.Of("beta", "aldric").Pick(sources).Select(s => (s.Server, s.Character)));
        Assert.Same(HistoryScope.Everyone, HistoryScope.Of(null, "Aldric"));   // a character alone would be ambiguous: two servers can both have a Aldric
        Assert.True(HistoryScope.Everyone.IsEveryone);
        Assert.True(HistoryScope.Of("alpha", "Brannoch").IsOneCharacter);
        Assert.False(HistoryScope.Of("alpha").IsOneCharacter);
    }

    [Fact]
    public void A_scope_reads_as_a_label_and_has_a_key_that_changes_with_it()
    {
        Assert.Equal("Everyone", HistoryScope.Everyone.Label);
        Assert.Equal("Everyone on Alpha", HistoryScope.Of("alpha").Label);
        Assert.Equal("Aldric · Alpha", HistoryScope.Of("alpha", "Aldric").Label);
        Assert.Equal(3, new[] { HistoryScope.Everyone.Key, HistoryScope.Of("alpha").Key, HistoryScope.Of("alpha", "Aldric").Key }.Distinct().Count());
    }

    // ------------------------------------------------------------------ the same loot seen by two characters

    private static LootEvent Item(DateTime at, string who, bool you, string cls = "ftr", string item = "Rat Ear", int count = 1, long split = 0)
        => new(at, LootKind.Item, who, you, "a rat", item, count, 0, split, "valeofzintar", cls, 30, "9a818e77c3484fe98efd9081");

    [Fact]
    public void An_event_both_characters_logged_counts_once_and_keeps_the_real_class_and_the_you()
    {
        var seenByLooter = new[] { Item(T, "Aldric", you: true, cls: "None") };
        var seenByFriend = new[] { Item(T, "Aldric", you: false, cls: "ftr") };

        var merged = LootMerge.Combine([seenByLooter, seenByFriend]);

        var e = Assert.Single(merged);
        Assert.True(e.You);
        Assert.Equal("ftr", e.Class);
    }

    [Fact]
    public void Identical_items_in_one_second_count_as_many_as_the_ledger_that_saw_most_of_them()
    {
        var one = new[] { Item(T, "Rook", false), Item(T, "Rook", false), Item(T, "Rook", false) };
        var two = new[] { Item(T, "Rook", false), Item(T, "Rook", false) };

        Assert.Equal(3, LootMerge.Combine([one, two]).Count);   // not five
        Assert.Equal(3, LootMerge.Combine([two, one]).Count);
    }

    [Fact]
    public void Events_that_differ_in_time_who_or_what_stay_separate_and_each_ledgers_purse_share_adds_up()
    {
        var a = new[] { Item(T, "Rook", false), Item(T.AddSeconds(1), "Rook", false), Item(T, "Pip", false), Item(T, "Rook", false, item: "Rat Tooth") };
        var b = new[] { Item(T, "Rook", false) };
        Assert.Equal(4, LootMerge.Combine([a, b]).Count);

        var purse = (long split) => new LootEvent(T, LootKind.Coin, "Rook", false, "a rat", "", 0, 500, split);
        var coin = Assert.Single(LootMerge.Combine([[purse(100)], [purse(150)]]));
        Assert.Equal(250, coin.Split);   // each character was handed its own share
    }

    [Fact]
    public void One_ledger_alone_is_returned_as_it_is()
    {
        var list = new[] { Item(T, "Rook", false), Item(T, "Rook", false) };
        Assert.Equal(2, LootMerge.Combine([list]).Count);
    }

    [Fact]
    public void The_store_reads_several_characters_together_and_an_event_their_ledgers_share_is_counted_once()
    {
        using var s = new Scratch();
        string Social(string who) => $"srv\\{who}\\Ledger\\{who}_Social_2026-10-25.json";
        // Rook looted a Rat Ear in front of both Aldric and Wendell, so each ledger has it; each of them also looted one thing alone
        s.Write(Social("Aldric"), Fixture.File(Fixture.Item(T, "Rook", "Aldric", "a rat", "Rat Ear"), Fixture.Item(T.AddSeconds(10), "Aldric", "Aldric", "a rat", "Rat Tooth")));
        s.Write(Social("Wendell"), Fixture.File(Fixture.Item(T, "Rook", "Wendell", "a rat", "Rat Ear"), Fixture.Item(T.AddSeconds(20), "Wendell", "Wendell", "a rat", "Rat Hide")));
        var store = new LootStore(Path.Combine(s.Path, "state"), s.Path);

        store.Select(store.Sources.ToList());
        store.Refresh();

        var events = store.Events(DateOnly.FromDateTime(T));
        Assert.Equal(["Rat Ear", "Rat Tooth", "Rat Hide"], events.Select(e => e.Item));
        Assert.Equal([false, true, true], events.Select(e => e.You));
        Assert.Null(store.Selected);   // more than one character: none is "the" selected one
        Assert.Equal(2, store.Selection.Count);

        store.Select(store.Sources.Single(x => x.Character == "Aldric"));
        store.Refresh();
        Assert.Equal(["Rat Ear", "Rat Tooth"], store.Events(DateOnly.FromDateTime(T)).Select(e => e.Item));   // one character is just that one
        Assert.Equal("Aldric", store.Selected?.Character);
    }

    // ------------------------------------------------------------------ the histories for a wider scope

    private static string Harvest(DateTime when, string item, string who)
        => Fixture.Row("act_27", "ref_9", new { d01 = 1, d02 = "", d03 = Fixture.B64("harvest"), d04 = item, d05 = "43e9d5b85dbdc81fa268da3a" }, when, who, who);

    [Fact]
    public void The_history_queries_take_one_character_everyone_on_a_server_or_everyone()
    {
        using var s = new Scratch();
        s.Write(@"alpha\Aldric\Ledger\Aldric_Character_2026-10-25.json", Fixture.File(Harvest(T, "Wood", "Aldric"), Harvest(T.AddMinutes(1), "Resin", "Aldric")));
        s.Write(@"alpha\Wendell\Ledger\Wendell_Character_2026-10-25.json", Fixture.File(Harvest(T.AddMinutes(2), "Copper Ore", "Wendell")));
        s.Write(@"beta\Aldric\Ledger\Aldric_Character_2026-10-26.json", Fixture.File(Harvest(T.AddDays(1), "Wood", "Aldric")));
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        var store = new HistoryStore(db);
        LedgerArchiver.Run(db, s.Path);
        EventBuilder.Update(db);

        Assert.Equal(2, store.Harvests("alpha", "Aldric").Count);
        Assert.Equal(3, store.Harvests("alpha", null).Count);                       // everyone on one server
        Assert.Equal(4, store.Harvests(null, null).Count);                         // everyone on the PC
        Assert.Equal(1, store.Harvests("beta", "Aldric").Count);               // the same name on another server is another character
        Assert.Equal(["Aldric", "Aldric", "Wendell"], store.Harvests("alpha", null).Select(h => h.Character));
        Assert.Equal(3, store.Counts("alpha", null).Harvest);
        Assert.Equal(4, store.Counts(null, null).Harvest);
        Assert.Equal(2, store.Days(null, null).Count);
        Assert.Single(store.Days("beta", null));
        // and a period narrows it as before
        Assert.Equal(3, store.Harvests(null, null, new Period("the 25th", T.Date, T.Date.AddDays(1))).Count);
    }

    // ------------------------------------------------------------------ the small window's markers, seen from the History window

    [Fact]
    public void Another_copy_of_the_sessions_file_is_picked_up_without_counting_its_own_writes()
    {
        using var s = new Scratch();
        var small = new LootSessions(s.Path);
        var history = new LootSessions(s.Path);
        Assert.False(history.Reload());                                   // nothing yet

        small.Start(T, "manual");
        Assert.True(history.Reload());                                    // the History window sees the small window's new session
        Assert.Equal(2, history.All.Count);
        Assert.False(history.Reload());                                   // and only once
        Assert.False(small.Reload());                                     // a copy doesn't treat its own save as someone else's
    }

    // ------------------------------------------------------------------ a run pointed at another game folder

    [Fact]
    public void A_borrowed_game_folder_gets_its_own_saved_copy_and_never_the_players()
    {
        var a = LedgerDb.OtherFolder(@"C:\Temp\demo-game");
        Assert.Equal(a, LedgerDb.OtherFolder(@"c:\temp\DEMO-GAME\"));     // the same folder, however it is spelled
        Assert.NotEqual(a, LedgerDb.OtherFolder(@"C:\Temp\other-game"));
        Assert.StartsWith(Path.Combine(AppFolder.Folder, "other-ledgers"), a);
        Assert.NotEqual(Path.GetDirectoryName(LedgerDb.DefaultPath), a);  // never the folder the real ledger.db is in
    }
}
