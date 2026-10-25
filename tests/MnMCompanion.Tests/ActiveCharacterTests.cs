using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using static MnMCompanion.Tests.Fixture;

namespace MnMCompanion.Tests;

/// <summary>Following whichever character the game is writing a ledger for.</summary>
public class ActiveCharacterTests
{
    private static readonly DateTime At = new(2026, 10, 25, 12, 0, 0);

    private static LedgerSource Of(string character, int minutesAgo, bool ledger = true, string server = "alpha") =>
        new(server, character, $@"c:\game\{server}\{character}", ledger ? At.AddMinutes(-minutesAgo) : DateTime.MinValue, ledger);

    [Fact]
    public void The_first_look_only_remembers_so_what_was_written_before_the_app_started_never_counts()
    {
        var watch = new ActiveCharacterWatch();
        Assert.Null(watch.Update([Of("Main", 5), Of("Alt", 1)], showing: null));
        Assert.Null(watch.Update([Of("Main", 5), Of("Alt", 1)], showing: "c:\\game\\alpha\\Main"));   // nothing new since
    }

    [Fact]
    public void A_character_written_since_the_last_look_is_the_one_to_switch_to()
    {
        var watch = new ActiveCharacterWatch();
        watch.Update([Of("Main", 5), Of("Alt", 30)], @"c:\game\alpha\Main");

        var next = watch.Update([Of("Main", 5), Of("Alt", 0)], @"c:\game\alpha\Main");

        Assert.Equal("Alt", next?.Character);
        Assert.Null(watch.Update([Of("Main", 5), Of("Alt", 0)], @"c:\game\alpha\Alt"));   // the same write is not reported twice
    }

    [Fact]
    public void The_character_being_shown_stays_while_it_is_still_being_written_even_if_another_is_too()
    {
        var watch = new ActiveCharacterWatch();
        watch.Update([Of("Main", 5), Of("Alt", 30)], @"c:\game\alpha\Main");

        Assert.Null(watch.Update([Of("Main", 0), Of("Alt", 0)], @"c:\game\alpha\Main"));   // two clients: no flapping
    }

    [Fact]
    public void When_several_others_were_written_the_newest_wins()
    {
        var watch = new ActiveCharacterWatch();
        watch.Update([Of("Main", 50), Of("Alt", 40), Of("Third", 30, server: "beta")], @"c:\game\alpha\Main");

        var next = watch.Update([Of("Main", 50), Of("Alt", 2), Of("Third", 0, server: "beta")], @"c:\game\alpha\Main");

        Assert.Equal(("beta", "Third"), (next?.Server, next?.Character));
    }

    [Fact]
    public void A_character_that_gets_its_first_ledger_while_the_app_runs_is_picked_up_and_empty_folders_never_are()
    {
        var watch = new ActiveCharacterWatch();
        watch.Update([Of("Main", 5), Of("Fresh", 0, ledger: false)], @"c:\game\alpha\Main");

        Assert.Null(watch.Update([Of("Main", 5), Of("Fresh", 0, ledger: false)], @"c:\game\alpha\Main"));   // still nothing written
        Assert.Equal("Fresh", watch.Update([Of("Main", 5), Of("Fresh", 0)], @"c:\game\alpha\Main")?.Character);
    }

    [Fact]
    public void The_store_finds_the_character_whose_ledger_file_was_just_written()
    {
        using var s = new Scratch();
        string Path(string who) => System.IO.Path.Combine(s.Path, "srv", who, "Ledger", who + "_Character_2026-10-25.json");
        s.Write(@"srv\Main\Ledger\Main_Character_2026-10-25.json", File(Sale(At, "Main", "Rat Ear", 1, "0 platinum 0 gold 0 silver 3 copper")));
        s.Write(@"srv\Alt\Ledger\Alt_Character_2026-10-25.json", File(Sale(At, "Alt", "Rat Ear", 1, "0 platinum 0 gold 0 silver 3 copper")));
        System.IO.File.SetLastWriteTime(Path("Main"), At.AddHours(-2));
        System.IO.File.SetLastWriteTime(Path("Alt"), At.AddHours(-1));
        var store = new LootStore(System.IO.Path.Combine(s.Path, "state"), s.Path);
        var main = store.Sources.Single(x => x.Character == "Main");
        store.Select(main);

        Assert.Null(store.DetectPlayed(main.Directory));                   // the first look only remembers
        System.IO.File.SetLastWriteTime(Path("Alt"), At);                  // the game writes the alt's ledger
        Assert.Equal("Alt", store.DetectPlayed(main.Directory)?.Character);
        Assert.Null(store.DetectPlayed(main.Directory));                   // reported once
    }
}
