using MnMCompanion.Timers;

namespace MnMCompanion.Tests;

/// <summary>The sound each timer ends with: what can be kept, how it is named for the player, which Windows sounds are offered, and that it survives a restart.</summary>
public class TimerSoundTests
{
    [Theory]
    [InlineData("system:Beep", "system:Beep")]
    [InlineData("system:beep", "system:Beep")]
    [InlineData("  SYSTEM: asterisk ", "system:Asterisk")]
    [InlineData("system:Hand", "system:Hand")]
    [InlineData(@"file:C:\Sounds\horn.wav", @"file:C:\Sounds\horn.wav")]
    [InlineData(@"FILE:  C:\Sounds\horn.WAV ", @"file:C:\Sounds\horn.WAV")]
    [InlineData("file:/home/deck/horn.wav", "file:/home/deck/horn.wav")]
    public void A_sound_is_kept_in_one_written_form(string typed, string kept) => Assert.Equal(kept, TimerSounds.Normalize(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("beep")]
    [InlineData("system:")]
    [InlineData("system:Nothing")]
    [InlineData(@"file:C:\Sounds\horn.mp3")]
    [InlineData(@"file:C:\Sounds\horn")]
    [InlineData("file:")]
    [InlineData("file:   ")]
    [InlineData("url:http://example.com/horn.wav")]
    public void Anything_that_is_not_a_playable_sound_is_none(string? typed) => Assert.Null(TimerSounds.Normalize(typed));

    [Fact]
    public void A_path_with_a_control_character_or_a_very_long_one_is_none()
    {
        Assert.Null(TimerSounds.Normalize("file:C:\\Sounds\\ho\trn.wav"));
        Assert.Null(TimerSounds.Normalize("file:C:\\Sounds\\ho\0rn.wav"));
        var longPath = @"file:C:\" + new string('a', TimerSounds.MaxPath) + ".wav";
        Assert.Null(TimerSounds.Normalize(longPath));
        Assert.NotNull(TimerSounds.Normalize(@"file:C:\" + new string('a', TimerSounds.MaxPath - 10) + ".wav"));
    }

    [Theory]
    [InlineData(null, "No sound")]
    [InlineData("", "No sound")]
    [InlineData("nonsense", "No sound")]
    [InlineData("system:asterisk", "Asterisk")]
    [InlineData(@"file:C:\Windows\Media\Alarm01.wav", "Alarm01")]
    [InlineData(@"file:C:\Sounds\Big Horn.wav", "Big Horn")]
    public void A_sound_is_named_for_the_player_by_its_name_alone(string? sound, string shown) => Assert.Equal(shown, TimerSounds.Describe(sound));

    [Fact]
    public void A_sound_says_whether_it_is_a_system_sound_or_a_file()
    {
        Assert.Equal("Beep", TimerSounds.SystemName("system:beep"));
        Assert.Null(TimerSounds.FilePath("system:beep"));
        Assert.Equal(@"C:\Sounds\horn.wav", TimerSounds.FilePath(@"file:C:\Sounds\horn.wav"));
        Assert.Null(TimerSounds.SystemName(@"file:C:\Sounds\horn.wav"));
        Assert.Null(TimerSounds.SystemName(null));
        Assert.Null(TimerSounds.FilePath("junk"));
        Assert.Equal(@"file:C:\Sounds\horn.wav", TimerSounds.ForFile(@"C:\Sounds\horn.wav"));
        Assert.Null(TimerSounds.ForFile(@"C:\Sounds\horn.mp3"));
        Assert.Null(TimerSounds.ForFile(null));
    }

    [Fact]
    public void The_windows_sounds_on_offer_are_grouped_and_leave_out_speech_and_what_is_not_a_wav()
    {
        using var s = new Fixture.Scratch();
        foreach (var name in new[] { "Alarm02.wav", "Alarm01.wav", "Ring01.wav", "Windows Ding.wav", "chimes.wav", "Speech On.wav", "notes.txt", @"Sonata\Sonata Alarm.wav" })
            s.Write(name, "x");

        var found = TimerSounds.WindowsFiles(s.Path);

        Assert.Equal(["Alarm01", "Alarm02", "Ring01", "chimes", "Windows Ding"], found.Select(f => f.Name));
        Assert.Equal(["Alarms", "Alarms", "Rings", "Windows sounds", "Windows sounds"], found.Select(f => f.Group));
        Assert.All(found, f => Assert.Equal(f.Value, TimerSounds.Normalize(f.Value)));
        Assert.Equal(Path.Combine(s.Path, "Alarm01.wav"), TimerSounds.FilePath(found[0].Value));
    }

    [Fact]
    public void A_folder_that_is_not_there_offers_nothing_and_the_pcs_own_never_throws()
    {
        using var s = new Fixture.Scratch();
        Assert.Empty(TimerSounds.WindowsFiles(Path.Combine(s.Path, "nowhere")));
        _ = TimerSounds.WindowsFiles();   // this PC's own Media folder, or none
    }

    // ------------------------------------------------------------------ on the timers

    [Fact]
    public void A_timer_to_hand_can_carry_a_sound_and_one_that_cannot_be_kept_is_left_out_without_refusing_the_timer()
    {
        var book = new TimerBook();
        Assert.True(book.AddPreset("Haste", 90, "Ctrl+1", "system:asterisk"));
        Assert.True(book.AddPreset("Shield", 600, null, @"file:C:\Sounds\horn.wav"));
        Assert.True(book.AddPreset("Food", 1800, null, "junk"));
        Assert.True(book.AddPreset("Plain", 60));

        Assert.Equal("system:Asterisk", book.Presets[0].Sound);
        Assert.Equal(@"file:C:\Sounds\horn.wav", book.Presets[1].Sound);
        Assert.Null(book.Presets[2].Sound);
        Assert.Null(book.Presets[3].Sound);
        Assert.Equal("system:Asterisk", book.SoundOf("haste"));
        Assert.Null(book.SoundOf("Plain"));
        Assert.Null(book.SoundOf("No such timer"));
    }

    [Fact]
    public void Changing_a_timer_keeps_its_sound_and_the_sound_can_be_changed_on_its_own()
    {
        var book = new TimerBook();
        book.AddPreset("Haste", 90, "Ctrl+1", "system:Beep");

        Assert.True(book.SetPreset(0, "Quickness", 120, "Ctrl+2"));
        Assert.Equal(new TimerPreset("Quickness", 120, "Ctrl+2", "system:Beep"), book.Presets[0]);

        Assert.True(book.SetSound(0, "system:hand"));
        Assert.Equal("system:Hand", book.Presets[0].Sound);
        Assert.True(book.SetSound(0, null));
        Assert.Null(book.Presets[0].Sound);
        Assert.True(book.SetSound(0, "not a sound"));
        Assert.Null(book.Presets[0].Sound);
        Assert.False(book.SetSound(1, "system:Beep"));
        Assert.False(book.SetSound(-1, "system:Beep"));
    }

    [Fact]
    public void A_new_timer_can_be_added_with_a_sound()
    {
        var book = new TimerBook();
        Assert.True(book.AddNewPreset("Haste", 90, null, out var problem, "system:Question"));
        Assert.Null(problem);
        Assert.Equal("system:Question", book.Presets[0].Sound);
        Assert.True(book.AddNewPreset("Shield", 600, null, out _));
        Assert.Null(book.Presets[1].Sound);
    }

    [Fact]
    public void Sounds_are_saved_and_read_back_the_corpse_sound_too_and_a_bad_one_in_a_hand_edited_file_is_dropped()
    {
        using var s = new Fixture.Scratch();
        var folder = Path.Combine(s.Path, "srv-Aldric");
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var book = new TimerBook();
        book.AddPreset("Haste", 90, "Ctrl+1", "system:Asterisk");
        book.AddPreset("Shield", 600, null, @"file:C:\Sounds\horn.wav");
        book.Corpse = new CorpseTimerSetting { On = true, Sound = "system:Exclamation" };
        Assert.True(book.Save(folder));

        var back = TimerBook.Load(folder, now);
        Assert.Equal(book.Presets, back.Presets);
        Assert.Equal("system:Exclamation", back.Corpse.Sound);

        File.WriteAllText(Path.Combine(folder, "timers.json"), """
            { "presets": [ {"name":"A","seconds":60,"sound":"system:beep"}, {"name":"B","seconds":60,"sound":"whatever"}, {"name":"C","seconds":60,"key":"Q","sound":"system:Hand"}, {"name":"D","seconds":60} ],
              "corpse": { "on": true, "sound": "file:C:\\x\\song.mp3" } }
            """);
        var hand = TimerBook.Load(folder, now);
        Assert.Equal(["system:Beep", null, "system:Hand", null], hand.Presets.Select(p => p.Sound));   // C's key can't be used, but it keeps its sound
        Assert.Null(hand.Corpse.Sound);
    }

    [Fact]
    public void A_file_from_before_sounds_loads_with_none()
    {
        using var s = new Fixture.Scratch();
        var folder = Path.Combine(s.Path, "old");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "timers.json"), """{ "presets": [ {"name":"Haste","seconds":90,"key":"Ctrl+1"} ] }""");
        var book = TimerBook.Load(folder, DateTime.UtcNow);
        Assert.Equal([new TimerPreset("Haste", 90, "Ctrl+1")], book.Presets);
        Assert.Null(book.Corpse.Sound);
    }
}
