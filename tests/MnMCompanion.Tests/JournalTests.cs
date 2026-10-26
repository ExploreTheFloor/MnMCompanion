using System.Text.Json;
using MnMCompanion.Community;
using MnMCompanion.Data;
using MnMCompanion.Journal;

namespace MnMCompanion.Tests;

/// <summary>
/// What NPCs have said to the player (the game's journal folder): read into the app's own database, lined up with the NPCs and the monsters
/// the ledger knows, and shared only with the player's own character name taken out.
/// </summary>
public class JournalTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Local);

    private static string Stamp(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>A journal file's text: entries newest first, each stamped, separated by a blank line.</summary>
    private static string Journal(string npc, params (int Seconds, string Text)[] said)
        => string.Join("\n\n", said.OrderByDescending(s => s.Seconds).Select(s => $"{Stamp(T0.AddSeconds(s.Seconds))}: {npc} says {s.Text}")) + "\n";

    private static void Write(Fixture.Scratch s, string server, string character, string npc, string text)
        => s.Write(Path.Combine(server, character, "journal", npc), text);

    // ---------------------------------------------------------------- parsing

    [Fact]
    public void An_entry_has_a_time_a_speaker_a_verb_and_what_was_said()
    {
        var lines = JournalParser.Parse("2026-10-01 14:59:29: Abbot Harrow says Take this satchel and collect four femurs.\n\n2026-10-01 14:58:39: Abbot Harrow whispers \"Yes, you have that look.\"\n");

        Assert.Equal(2, lines.Count);
        Assert.Equal(("Abbot Harrow", "says", "Take this satchel and collect four femurs."), (lines[0].Speaker, lines[0].Verb, lines[0].Text));
        Assert.Equal(new DateTime(2026, 10, 1, 14, 59, 29, DateTimeKind.Local), lines[0].Time);
        Assert.Equal(("whispers", "Yes, you have that look."), (lines[1].Verb, lines[1].Text));   // the quotes come off
    }

    [Fact]
    public void A_line_with_no_time_continues_the_entry_above_and_an_odd_entry_is_kept_whole()
    {
        var lines = JournalParser.Parse("2026-10-01 10:00:00: Hull Orbel says First part\nsecond part of the same entry\n\n2026-10-01 09:00:00: The door creaks open.\n\nno stamp at all\n");

        Assert.Equal(3, lines.Count);
        Assert.Equal("First part second part of the same entry", lines[0].Text);
        Assert.Equal(("", "The door creaks open."), (lines[1].Speaker, lines[1].Text));   // not speech: the whole entry is the text
        Assert.Null(lines[2].Time);
        Assert.Equal("no stamp at all", lines[2].Text);
        Assert.Empty(JournalParser.Parse(""));
        Assert.Empty(JournalParser.Parse("\n\n  \n"));
    }

    [Fact]
    public void The_players_name_is_replaced_in_any_case_but_only_as_a_whole_word()
    {
        Assert.Equal("Well met, {player}. Aye, {player}'s a fine name. {player}!", JournalParser.WithoutPlayerName("Well met, Aldric. Aye, aldric's a fine name. ALDRIC!", "Aldric"));
        Assert.Equal("Aldrich and Realdric are other people", JournalParser.WithoutPlayerName("Aldrich and Realdric are other people", "Aldric"));
        Assert.Equal("nothing to change", JournalParser.WithoutPlayerName("nothing to change", "Aldric"));
        Assert.Equal("a", JournalParser.WithoutPlayerName("a", "A"));   // a one-letter name is too short to replace safely
    }

    // ---------------------------------------------------------------- the database

    [Fact]
    public void Journal_files_become_NPCs_and_their_lines_and_a_second_pass_adds_nothing()
    {
        using var s = new Fixture.Scratch();
        Write(s, "alpha", "Aldric", "Abbot Harrow", Journal("Abbot Harrow", (0, "Our work requires material."), (5, "Take this satchel.")));
        Write(s, "alpha", "Aldric", "Hull Orbel", Journal("Hull Orbel", (10, "Mind the road.")));
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        var first = JournalArchiver.Run(db, s.Path);
        Assert.Equal((2, 2, 2, 3), (first.Files, first.FilesRead, first.NpcsAdded, first.LinesAdded));

        var again = JournalArchiver.Run(db, s.Path);
        Assert.Equal((2, 0, 0, 0), (again.Files, again.FilesRead, again.NpcsAdded, again.LinesAdded));   // unchanged files are not even read

        var store = new HistoryStore(db);
        Assert.Equal(["Hull Orbel", "Abbot Harrow"], store.Npcs().Select(n => n.Name));   // heard from most recently first
        Assert.Equal(["Our work requires material.", "Take this satchel."], store.NpcLines("abbot harrow").Select(l => l.Text));   // names match in any case
        Assert.All(store.NpcLines("Abbot Harrow"), l => Assert.Equal(("Aldric", "Abbot Harrow", "says"), (l.Character, l.Speaker, l.Verb)));
    }

    [Fact]
    public void When_the_game_adds_an_entry_to_a_file_only_that_entry_is_new()
    {
        using var s = new Fixture.Scratch();
        Write(s, "alpha", "Aldric", "Abbot Harrow", Journal("Abbot Harrow", (0, "One."), (5, "Two.")));
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        JournalArchiver.Run(db, s.Path);

        Write(s, "alpha", "Aldric", "Abbot Harrow", Journal("Abbot Harrow", (0, "One."), (5, "Two."), (9, "Three.")));   // a new entry at the top
        var result = JournalArchiver.Run(db, s.Path);

        Assert.Equal((1, 0, 1), (result.FilesRead, result.NpcsAdded, result.LinesAdded));
        Assert.Equal(["One.", "Two.", "Three."], new HistoryStore(db).NpcLines("Abbot Harrow").Select(l => l.Text));
    }

    [Fact]
    public void Two_identical_entries_in_one_file_are_both_kept_and_a_file_with_no_stamp_still_imports()
    {
        using var s = new Fixture.Scratch();
        var same = $"{Stamp(T0)}: Hull Orbel says Aye.";
        Write(s, "alpha", "Aldric", "Hull Orbel", same + "\n\n" + same + "\n\nunstamped remark\n");
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        var result = JournalArchiver.Run(db, s.Path);

        Assert.Equal(3, result.LinesAdded);
        var lines = new HistoryStore(db).NpcLines("Hull Orbel");
        Assert.Equal(3, lines.Count);
        Assert.Contains(lines, l => l.Time is null && l.Text == "unstamped remark");
    }

    [Fact]
    public void An_NPC_two_characters_met_is_one_NPC_and_each_characters_lines_are_kept_apart()
    {
        using var s = new Fixture.Scratch();
        Write(s, "alpha", "Aldric", "Abbot Harrow", Journal("Abbot Harrow", (0, "Hello Aldric.")));
        Write(s, "alpha", "Brannoch", "Abbot Harrow", Journal("Abbot Harrow", (0, "Hello Brannoch.")));
        Write(s, "beta", "Aldric", "Hull Orbel", Journal("Hull Orbel", (0, "Mind the road.")));
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        JournalArchiver.Run(db, s.Path);
        var store = new HistoryStore(db);

        Assert.Equal(2, store.Npcs().Count);
        Assert.Equal(2, store.Npcs().Single(n => n.Name == "Abbot Harrow").Lines);
        Assert.Equal(["Hello Aldric."], store.NpcLines("Abbot Harrow", "alpha", "Aldric").Select(l => l.Text));
        Assert.Equal(["Abbot Harrow"], store.Npcs("alpha", "Brannoch").Select(n => n.Name));
        Assert.Equal(["Hull Orbel"], store.Npcs("beta").Select(n => n.Name));
    }

    [Fact]
    public void An_NPC_whose_name_the_ledger_has_seen_looted_is_lined_up_with_that_monster()
    {
        using var s = new Fixture.Scratch();
        var at = T0;
        // The ledger: Darius Brask was killed (a purse off his corpse) and a skeleton dropped an item. Abbot Harrow was never looted.
        s.Write(Path.Combine("alpha", "Aldric", "Ledger", "Aldric_Character_2026-10-24.json"), Fixture.File(
            Fixture.Purse(at, "Aldric", "Aldric", "Darius Brask", "0,0,1,15"),
            Fixture.Item(at.AddSeconds(5), "Aldric", "Aldric", "a restless skeleton", "Bone Chips")));
        Write(s, "alpha", "Aldric", "Darius Brask", Journal("Darius Brask", (0, "You will regret this.")));
        Write(s, "alpha", "Aldric", "Abbot Harrow", Journal("Abbot Harrow", (0, "Welcome.")));
        Write(s, "alpha", "Aldric", "A Restless Skeleton", Journal("A Restless Skeleton", (0, "...")));   // a different spelling of a looted name
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        LedgerArchiver.Run(db, s.Path);

        JournalArchiver.Run(db, s.Path);

        var npcs = new HistoryStore(db).Npcs().ToDictionary(n => n.Name, n => n.LootedAsMonster);
        Assert.True(npcs["Darius Brask"]);
        Assert.False(npcs["Abbot Harrow"]);
        Assert.True(npcs["A Restless Skeleton"], "names match without regard to case");
    }

    [Fact]
    public void A_character_folder_without_a_journal_and_an_unreadable_root_are_fine()
    {
        using var s = new Fixture.Scratch();
        s.Write(Path.Combine("alpha", "Aldric", "Ledger", "Aldric_Character_2026-10-24.json"), Fixture.File());
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        Assert.Equal((0, 0, 0), (JournalArchiver.Run(db, s.Path).Files, JournalArchiver.Run(db, s.Path).LinesAdded, new HistoryStore(db).Npcs().Count));
        Assert.Equal(0, JournalArchiver.Run(db, Path.Combine(s.Path, "no-such-folder")).Files);
    }

    // ---------------------------------------------------------------- what is shared

    [Fact]
    public void The_shared_file_carries_each_NPCs_distinct_lines_with_the_players_name_taken_out_and_nothing_about_when()
    {
        using var s = new Fixture.Scratch();
        Write(s, "alpha", "Aldric", "Abbot Harrow", Journal("Abbot Harrow", (0, "Well met, Aldric."), (5, "Take this satchel.")));
        Write(s, "alpha", "Brannoch", "Abbot Harrow", Journal("Abbot Harrow", (0, "Well met, Brannoch."), (5, "Take this satchel.")));   // the same line, said to someone else
        Write(s, "alpha", "Aldric", "Hull Orbel", Journal("Hull Orbel", (0, "Mind the road, ALDRIC!")));
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        JournalArchiver.Run(db, s.Path);

        var contribution = CommunityExport.Build(db, "MnMCompanion 0.2.0", new DateOnly(2026, 10, 25));

        Assert.Equal(["Abbot Harrow", "Hull Orbel"], contribution.Npcs.Select(n => n.Name));
        Assert.Equal(["Take this satchel.", "Well met, {player}."], contribution.Npcs[0].Lines);   // one line said to both is one line
        Assert.Equal(["Mind the road, {player}!"], contribution.Npcs[1].Lines);
        var json = contribution.ToJson();
        Assert.DoesNotContain("Aldric", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Brannoch", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2026-10-24", json);   // no time of any entry
        Assert.Contains("\"npcs\"", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(["name", "lines"], doc.RootElement.GetProperty("npcs")[0].EnumerateObject().Select(p => p.Name));
        Assert.Contains(ContributionBuilder.Describe(contribution), l => l.StartsWith("2 NPCs with 3 lines"));
    }

    [Fact]
    public void Overlong_empty_or_control_character_lines_and_oversized_lists_are_left_out()
    {
        var long_ = new string('x', ContributionBuilder.MaxLine + 1);
        var npcs = new List<ContributionNpc>
        {
            new("Abbot Harrow", ["Fine.", "  ", long_, "tab\there", "Fine.", " padded "]),
            new("abbot harrow", ["Another."]),                                   // the same NPC in another case: merged
            new("Empty One", ["", long_]),                                       // nothing usable: the NPC is left out
            new("   ", ["Whose line is this?"]),
        };
        var c = ContributionBuilder.Build([], [], [], [], "a".PadLeft(32, 'a'), "MnMCompanion 0.2.0", new DateOnly(2026, 10, 25), npcs);

        var only = Assert.Single(c.Npcs);
        Assert.Equal("Abbot Harrow", only.Name);
        Assert.Equal(["Another.", "Fine.", "padded"], only.Lines);

        var many = Enumerable.Range(0, ContributionBuilder.MaxLinesPerNpc + 40).Select(n => $"line {n:D4}").ToList();
        var capped = ContributionBuilder.Build([], [], [], [], "a".PadLeft(32, 'a'), "MnMCompanion 0.2.0", new DateOnly(2026, 10, 25), [new ContributionNpc("Talker", many)]);
        Assert.Equal(ContributionBuilder.MaxLinesPerNpc, capped.Npcs[0].Lines.Count);
        Assert.Empty(ContributionBuilder.Build([], [], [], [], "a".PadLeft(32, 'a'), "MnMCompanion 0.2.0", new DateOnly(2026, 10, 25)).Npcs);
    }
}
