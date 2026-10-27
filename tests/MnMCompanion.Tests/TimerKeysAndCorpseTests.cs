using MnMCompanion.Loot;
using MnMCompanion.Timers;

namespace MnMCompanion.Tests;

/// <summary>The keys that start a timer (what can be typed, what is allowed, what is kept) and the corpse timer that every looted corpse gets.</summary>
public class TimerKeysAndCorpseTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Local);

    // ------------------------------------------------------------------ the keys

    [Theory]
    [InlineData("ctrl+alt+1", "Ctrl+Alt+1")]
    [InlineData("ALT + f5", "Alt+F5")]
    [InlineData("Control+Shift+a", "Ctrl+Shift+A")]
    [InlineData("num5", "Num5")]
    [InlineData("Windows+Ctrl+F12", "Ctrl+Win+F12")]
    [InlineData("  Ctrl+Alt+NumAdd ", "Ctrl+Alt+NumAdd")]
    public void A_key_is_read_however_it_is_typed_and_written_back_one_way(string typed, string kept) => Assert.Equal(kept, Hotkey.TryParse(typed)?.ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("+1")]
    [InlineData("Ctrl++1")]
    [InlineData("banana")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Ctrl+Alt+1+2")]
    [InlineData("Hyper+1")]
    [InlineData("Ctrl+F25")]
    public void Text_that_is_not_a_key_is_not_read(string? typed) => Assert.Null(Hotkey.TryParse(typed));

    [Fact]
    public void Modifiers_are_written_in_one_fixed_order_and_are_windows_own_values()
    {
        var all = new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, "1");
        Assert.Equal("Ctrl+Alt+Shift+Win+1", all.ToString());
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Ctrl);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Win);
    }

    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("Z", 0x5A)]
    [InlineData("0", 0x30)]
    [InlineData("9", 0x39)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("F24", 0x87)]
    [InlineData("Num0", 0x60)]
    [InlineData("Num9", 0x69)]
    [InlineData("NumMultiply", 0x6A)]
    [InlineData("NumAdd", 0x6B)]
    [InlineData("NumSubtract", 0x6D)]
    [InlineData("NumDecimal", 0x6E)]
    [InlineData("NumDivide", 0x6F)]
    public void Keys_have_the_codes_windows_knows_them_by(string key, int code) => Assert.Equal(code, new Hotkey(HotkeyModifiers.Ctrl, key).VirtualKey);

    [Fact]
    public void Every_key_that_can_be_used_reads_back_and_has_a_code_of_its_own()
    {
        var names = Hotkey.KeyNames;
        Assert.Equal(26 + 10 + 24 + 10 + 5, names.Count);
        foreach (var name in names)
        {
            var key = Hotkey.TryParse("Ctrl+" + name);
            Assert.NotNull(key);
            Assert.Equal(name, key!.Key);
            Assert.True(key.VirtualKey > 0, name);
        }
        Assert.Equal(names.Count, names.Select(n => new Hotkey(HotkeyModifiers.None, n).VirtualKey).Distinct().Count());
        Assert.Equal(0, new Hotkey(HotkeyModifiers.Ctrl, "Enter").VirtualKey);
    }

    [Theory]
    [InlineData("Ctrl+1", true)]
    [InlineData("Alt+Q", true)]
    [InlineData("Ctrl+Alt+Shift+Win+Z", true)]
    [InlineData("Win+1", true)]
    [InlineData("F5", true)]            // function and keypad keys are allowed bare: the game seldom needs them taken away
    [InlineData("Shift+F5", true)]
    [InlineData("Num3", true)]
    [InlineData("NumAdd", true)]
    [InlineData("1", false)]            // a bare letter or digit would be taken from the game
    [InlineData("Q", false)]
    [InlineData("Shift+1", false)]      // Shift alone is typing
    [InlineData("Shift+Q", false)]
    public void A_letter_or_digit_needs_ctrl_or_alt_so_a_timer_cannot_take_a_key_away_from_the_game(string text, bool allowed)
    {
        var problem = Hotkey.Problem(Hotkey.TryParse(text));
        Assert.Equal(allowed, problem is null);
        if (!allowed) Assert.Contains("Ctrl or Alt", problem);
    }

    [Fact]
    public void No_key_at_all_is_a_problem_in_words()
    {
        Assert.Contains("can't be used", Hotkey.Problem(null));
        Assert.Contains("can't be used", Hotkey.Problem(new Hotkey(HotkeyModifiers.Ctrl, "Enter")));
    }

    // ------------------------------------------------------------------ keys on the timers to hand

    [Fact]
    public void A_timer_to_hand_can_carry_a_key_which_is_kept_in_its_one_written_form()
    {
        var book = new TimerBook();
        Assert.True(book.AddPreset("Haste", 90, "ctrl+alt+1"));
        Assert.True(book.AddPreset("Shield", 600));
        Assert.Equal("Ctrl+Alt+1", book.Presets[0].Key);
        Assert.Null(book.Presets[1].Key);
        Assert.True(book.AddPreset("haste", 95, "Ctrl+Alt+1"));   // starting the same name over keeps its own key
        Assert.Equal(new TimerPreset("haste", 95, "Ctrl+Alt+1"), book.Presets[0]);
    }

    [Fact]
    public void A_key_that_is_not_allowed_or_is_already_taken_keeps_the_timer_out_and_says_why()
    {
        var book = new TimerBook();
        Assert.True(book.AddPreset("Haste", 90, "Ctrl+1"));

        Assert.False(book.AddPreset("Shield", 600, "Q"));
        Assert.False(book.AddPreset("Shield", 600, "banana"));
        Assert.False(book.AddPreset("Shield", 600, "control + 1"));   // the same key, written another way
        Assert.Single(book.Presets);

        Assert.Contains("Ctrl or Alt", book.KeyProblem("Q"));
        Assert.Contains("isn't a key", book.KeyProblem("banana"));
        Assert.Equal("Ctrl+1 already starts “Haste”.", book.KeyProblem("ctrl+1"));
        Assert.Null(book.KeyProblem("ctrl+1", exceptIndex: 0));   // the timer that has it may keep it
        Assert.Null(book.KeyProblem(null));
        Assert.Null(book.KeyProblem("  "));
    }

    [Fact]
    public void A_timer_can_be_changed_in_place_and_renamed_but_not_into_another_ones_name_or_key()
    {
        var book = new TimerBook();
        book.AddPreset("Haste", 90, "Ctrl+1");
        book.AddPreset("Shield", 600, "Ctrl+2");

        Assert.True(book.SetPreset(0, "Quickness", 120, "Ctrl+Alt+1"));
        Assert.Equal(new TimerPreset("Quickness", 120, "Ctrl+Alt+1"), book.Presets[0]);
        Assert.True(book.SetPreset(0, "Quickness", 120, null));
        Assert.Null(book.Presets[0].Key);

        Assert.Equal("There is already a timer called “Shield”.", book.PresetProblem(0, "shield", 120, null));
        Assert.Equal("Ctrl+2 already starts “Shield”.", book.PresetProblem(0, "Quickness", 120, "Ctrl+2"));
        Assert.Equal("Type a name for the timer.", book.PresetProblem(0, "  ", 120, null));
        Assert.StartsWith("Type a length", book.PresetProblem(0, "Quickness", 0, null));
        Assert.Equal("That timer is gone.", book.PresetProblem(5, "x", 10, null));
        Assert.False(book.SetPreset(0, "Shield", 120, null));
        Assert.Equal("Quickness", book.Presets[0].Name);   // a refused change changes nothing
    }

    [Fact]
    public void A_new_timer_is_added_to_the_end_with_the_same_rules_and_a_full_list_says_so()
    {
        var book = new TimerBook();
        Assert.True(book.AddNewPreset("Haste", 90, "ctrl+1", out var problem));
        Assert.Null(problem);
        Assert.False(book.AddNewPreset("Haste", 90, null, out problem));
        Assert.Contains("already a timer", problem);
        Assert.False(book.AddNewPreset("Other", 90, "Ctrl+1", out problem));
        Assert.Contains("already starts", problem);
        Assert.False(book.AddNewPreset("", 90, null, out problem));
        Assert.Single(book.Presets);

        for (int i = 1; i < TimerBook.MaxPresets; i++) Assert.True(book.AddNewPreset("T" + i, 10, null, out _));
        Assert.False(book.AddNewPreset("One too many", 10, null, out problem));
        Assert.Contains("full", problem);
        Assert.Equal(TimerBook.MaxPresets, book.Presets.Count);
    }

    [Fact]
    public void Keys_and_the_corpse_timer_are_saved_and_read_back_and_a_bad_key_in_a_hand_edited_file_is_dropped_but_the_timer_stays()
    {
        using var s = new Fixture.Scratch();
        var folder = Path.Combine(s.Path, "srv-Aldric");
        var book = new TimerBook();
        book.AddPreset("Haste", 90, "Ctrl+Alt+1");
        book.AddPreset("Shield", 600);
        book.Corpse = new CorpseTimerSetting { On = true, Name = "Decays", Seconds = 420, Group = true };
        Assert.True(book.Save(folder));

        var back = TimerBook.Load(folder, Now.ToUniversalTime());
        Assert.Equal(book.Presets, back.Presets);
        Assert.True(back.Corpse.On);
        Assert.Equal("Decays", back.Corpse.Name);
        Assert.Equal(420, back.Corpse.Seconds);
        Assert.True(back.Corpse.Group);

        File.WriteAllText(Path.Combine(folder, "timers.json"), """
            { "presets": [ {"name":"A","seconds":60,"key":"Ctrl+1"}, {"name":"B","seconds":60,"key":"ctrl+1"}, {"name":"C","seconds":60,"key":"Q"},
                           {"name":"D","seconds":60,"key":"nonsense"}, {"name":"E","seconds":60,"key":null}, {"name":"F","seconds":60} ],
              "corpse": { "on": true, "name": "  ", "seconds": 0, "group": false } }
            """);
        var hand = TimerBook.Load(folder, Now.ToUniversalTime());
        Assert.Equal(["A", "B", "C", "D", "E", "F"], hand.Presets.Select(p => p.Name));
        Assert.Equal(["Ctrl+1", null, null, null, null, null], hand.Presets.Select(p => p.Key));   // only the first holder of a key keeps it
        Assert.True(hand.Corpse.On);
        Assert.Equal(CorpseTimerSetting.DefaultName, hand.Corpse.Name);
        Assert.Equal(CorpseTimerSetting.DefaultSeconds, hand.Corpse.Seconds);
    }

    [Fact]
    public void A_file_from_before_keys_and_the_corpse_timer_loads_with_none_and_the_corpse_timer_off()
    {
        using var s = new Fixture.Scratch();
        var folder = Path.Combine(s.Path, "old");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "timers.json"), """{ "presets": [ {"name":"Haste","seconds":90} ], "running": [] }""");
        var book = TimerBook.Load(folder, Now.ToUniversalTime());
        Assert.Equal([new TimerPreset("Haste", 90)], book.Presets);
        Assert.False(book.Corpse.On);
        Assert.Equal(CorpseTimerSetting.DefaultName, book.Corpse.Name);
        Assert.False(TimerBook.Load(null, Now.ToUniversalTime()).Corpse.On);
    }

    // ------------------------------------------------------------------ the corpse timer

    private static LootedCorpse Corpse(string monster, DateTime start, bool you = true, bool mob = true, string who = "Aldric", string zone = "bogmoor") =>
        new(who, you, monster, mob, start, 1, 0, 0, null, zone);

    [Fact]
    public void Nothing_runs_while_the_corpse_timer_is_off()
    {
        var corpses = new[] { Corpse("a bog crawler", Now.AddSeconds(-5)) };
        Assert.Empty(CorpseTimers.Active(corpses, new CorpseTimerSetting { On = false, Seconds = 600 }, Now));
    }

    [Fact]
    public void Every_looted_monster_corpse_of_the_player_gets_a_timer_from_the_moment_it_was_looted()
    {
        var setting = new CorpseTimerSetting { On = true, Name = "Decays", Seconds = 600 };
        var a = Now.AddSeconds(-100);
        var b = Now.AddSeconds(-30);
        var timers = CorpseTimers.Active([Corpse("a bog crawler", b), Corpse("a marsh wolf", a)], setting, Now);

        Assert.Equal(2, timers.Count);
        Assert.Equal("Decays: a marsh wolf", timers[0].Label);   // the one ending soonest first
        Assert.Equal(a.AddSeconds(600), timers[0].EndsLocal);
        Assert.Equal(600, timers[0].Seconds);
        Assert.Equal("a marsh wolf", timers[0].Monster);
        Assert.Equal("bogmoor", timers[0].Zone);
        Assert.Equal("Decays: a bog crawler", timers[1].Label);
        Assert.Equal(b.AddSeconds(600), timers[1].EndsLocal);
        Assert.NotEqual(timers[0].Id, timers[1].Id);
    }

    [Fact]
    public void The_same_monster_looted_twice_gives_two_timers_with_their_own_ids_and_the_id_stays_the_same_when_asked_again()
    {
        var setting = new CorpseTimerSetting { On = true, Seconds = 600 };
        var corpses = new[] { Corpse("a bog crawler", Now.AddSeconds(-60)), Corpse("a bog crawler", Now.AddSeconds(-20)) };
        var first = CorpseTimers.Active(corpses, setting, Now);
        var later = CorpseTimers.Active(corpses, setting, Now.AddSeconds(5));
        Assert.Equal(2, first.Count);
        Assert.Equal(2, first.Select(t => t.Id).Distinct().Count());
        Assert.Equal(first.Select(t => t.Id), later.Select(t => t.Id));
        Assert.Equal(CorpseTimerSetting.DefaultName + ": a bog crawler", first[0].Label);
    }

    [Fact]
    public void A_players_own_corpse_is_not_a_monster_and_a_groups_corpses_count_only_when_asked_for()
    {
        var corpses = new[]
        {
            Corpse("a bog crawler", Now.AddSeconds(-10)),
            Corpse("your corpse", Now.AddSeconds(-10), mob: false),
            Corpse("Brom's corpse", Now.AddSeconds(-10), you: false, mob: false, who: "Brom"),
            Corpse("a marsh wolf", Now.AddSeconds(-10), you: false, who: "Brom"),
        };
        var mine = CorpseTimers.Active(corpses, new CorpseTimerSetting { On = true, Seconds = 600, Group = false }, Now);
        Assert.Equal(["a bog crawler"], mine.Select(t => t.Monster));
        Assert.All(mine, t => Assert.True(t.You));

        var group = CorpseTimers.Active(corpses, new CorpseTimerSetting { On = true, Seconds = 600, Group = true }, Now);
        Assert.Equal(["a bog crawler", "a marsh wolf"], group.Select(t => t.Monster).Order());
        Assert.Equal([true, false], group.OrderByDescending(t => t.You).Select(t => t.You));
    }

    [Fact]
    public void A_timer_that_ended_stays_for_a_few_seconds_and_then_goes_and_a_long_one_outlives_the_session()
    {
        var setting = new CorpseTimerSetting { On = true, Seconds = 60 };
        var corpses = new[]
        {
            Corpse("just ended", Now.AddSeconds(-60 - TimerClock.DoneShownSeconds + 1)),
            Corpse("long gone", Now.AddSeconds(-60 - TimerClock.DoneShownSeconds - 1)),
            Corpse("still running", Now.AddSeconds(-10)),
        };
        Assert.Equal(["just ended", "still running"], CorpseTimers.Active(corpses, setting, Now).Select(t => t.Monster));

        // looted while Companion was closed: it still shows what is left when the app starts again
        var hour = new CorpseTimerSetting { On = true, Seconds = 3600 };
        var left = Assert.Single(CorpseTimers.Active([Corpse("a bog crawler", Now.AddMinutes(-40))], hour, Now));
        Assert.Equal(20 * 60, (left.EndsLocal - Now).TotalSeconds, 3);
    }

    [Fact]
    public void A_setting_that_is_not_usable_is_brought_to_a_usable_one()
    {
        var corpses = new[] { Corpse("a bog crawler", Now.AddSeconds(-5)) };
        var odd = new CorpseTimerSetting { On = true, Name = "  \t ", Seconds = 0 };
        var timer = Assert.Single(CorpseTimers.Active(corpses, odd, Now));
        Assert.Equal("Corpse: a bog crawler", timer.Label);
        Assert.Equal(1, timer.Seconds);   // the length is held to at least a second (a saved file is brought to the default by Cleaned)

        var cleaned = odd.Cleaned();
        Assert.Equal(CorpseTimerSetting.DefaultName, cleaned.Name);
        Assert.Equal(CorpseTimerSetting.DefaultSeconds, cleaned.Seconds);
        Assert.Equal(CorpseTimerSetting.DefaultSeconds, new CorpseTimerSetting { Seconds = TimerClock.MaxSeconds + 1 }.Cleaned().Seconds);
        Assert.True(cleaned.On);
    }
}
