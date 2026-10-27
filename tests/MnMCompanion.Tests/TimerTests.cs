using MnMCompanion.Timers;

namespace MnMCompanion.Tests;

/// <summary>The manual timers: how lengths are read and shown, when a timer warns and ends, and what is kept between runs.</summary>
public class TimerTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("90", 90)]
    [InlineData("90s", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:30", 90)]
    [InlineData("1:02:03", 3723)]
    [InlineData("5m", 300)]
    [InlineData("5 M", 300)]
    [InlineData("2h", 7200)]
    [InlineData("1h30m", 5400)]
    [InlineData("1h 30m 10s", 5410)]
    [InlineData("45s", 45)]
    [InlineData("  12  ", 12)]
    [InlineData("24h", 86400)]
    public void Lengths_are_read_the_ways_a_player_types_them(string text, int seconds) => Assert.Equal(seconds, TimerClock.TryParse(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("0:00")]
    [InlineData("abc")]
    [InlineData("5x")]
    [InlineData("m")]
    [InlineData("30m15")]
    [InlineData("1:2:3:4")]
    [InlineData("1:")]
    [InlineData(":30")]
    [InlineData("-5")]
    [InlineData("1.5m")]
    [InlineData("25h")]
    [InlineData("99999999")]
    [InlineData("1:99999999")]
    public void Anything_else_is_not_a_length(string? text) => Assert.Null(TimerClock.TryParse(text));

    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(-3, "0.0")]
    [InlineData(4.24, "4.2")]
    [InlineData(9.96, "10.0")]
    [InlineData(10, "10")]
    [InlineData(44.2, "45")]
    [InlineData(59.5, "1:00")]
    [InlineData(125, "2:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(5410, "1:30:10")]
    public void The_countdown_reads_at_a_glance(double seconds, string shown) => Assert.Equal(shown, TimerClock.Format(seconds));

    [Theory]
    [InlineData(1, "1s")]
    [InlineData(90, "1:30")]
    [InlineData(3723, "1:02:03")]
    public void A_length_is_spelled_back_in_a_form_that_reads_back_the_same(int seconds, string spelled)
    {
        Assert.Equal(spelled, TimerClock.Spell(seconds));
        Assert.Equal(seconds, TimerClock.TryParse(spelled));
    }

    [Theory]
    [InlineData(600, TimerPhase.Running)]
    [InlineData(30.5, TimerPhase.Running)]
    [InlineData(30, TimerPhase.Warning)]
    [InlineData(10.5, TimerPhase.Warning)]
    [InlineData(10, TimerPhase.Ending)]
    [InlineData(0.1, TimerPhase.Ending)]
    [InlineData(0, TimerPhase.Done)]
    [InlineData(-4, TimerPhase.Done)]
    public void A_timer_warns_in_its_last_half_minute_and_flashes_in_its_last_ten_seconds(double remaining, TimerPhase phase) => Assert.Equal(phase, TimerClock.Phase(remaining));

    [Fact]
    public void Starting_a_timer_sets_when_it_ends_and_starting_it_again_starts_it_over()
    {
        var book = new TimerBook();

        var first = book.Start("  Haste ", 90, Now);
        Assert.Equal(new RunningTimer("Haste", Now.AddSeconds(90), 90), first);
        Assert.Equal(90, TimerBook.Remaining(first!, Now));

        var again = book.Start("haste", 120, Now.AddSeconds(30));
        var only = Assert.Single(book.Running);
        Assert.Equal(again, only);
        Assert.Equal(Now.AddSeconds(150), only.EndsUtc);
    }

    [Fact]
    public void A_timer_needs_a_name_and_a_sensible_length()
    {
        var book = new TimerBook();
        Assert.Null(book.Start("", 60, Now));
        Assert.Null(book.Start("   ", 60, Now));
        Assert.Null(book.Start("Haste", 0, Now));
        Assert.Null(book.Start("Haste", TimerClock.MaxSeconds + 1, Now));
        Assert.Empty(book.Running);
        Assert.False(book.AddPreset("Haste", 0));
        Assert.False(book.AddPreset(null, 60));
        Assert.Empty(book.Presets);
    }

    [Fact]
    public void Names_are_one_short_line()
    {
        Assert.Equal("Bind Wound", TimerBook.CleanName("Bind\r\n Wound\t"));
        Assert.Equal("ab", TimerBook.CleanName("a\nb"));
        Assert.Equal(TimerBook.MaxName, TimerBook.CleanName(new string('x', 200))!.Length);
        Assert.Null(TimerBook.CleanName("\n\t "));
        Assert.Null(TimerBook.CleanName(null));
    }

    [Fact]
    public void The_timers_to_hand_are_kept_by_name_and_a_new_length_replaces_the_old()
    {
        var book = new TimerBook();
        Assert.True(book.AddPreset("Haste", 90));
        Assert.True(book.AddPreset("Bind Wound", 30));
        Assert.True(book.AddPreset("HASTE", 120));
        Assert.Equal([new TimerPreset("HASTE", 120), new TimerPreset("Bind Wound", 30)], book.Presets);
        Assert.True(book.RemovePreset("haste"));
        Assert.False(book.RemovePreset("haste"));
        Assert.Equal([new TimerPreset("Bind Wound", 30)], book.Presets);
    }

    [Fact]
    public void There_is_a_limit_to_how_many_are_kept_and_how_many_run()
    {
        var book = new TimerBook();
        for (int i = 0; i < TimerBook.MaxPresets; i++) Assert.True(book.AddPreset("p" + i, 60));
        Assert.False(book.AddPreset("one too many", 60));
        Assert.True(book.AddPreset("p0", 61), "an existing name is replaced, not added");

        for (int i = 0; i < TimerBook.MaxRunning; i++) Assert.NotNull(book.Start("t" + i, 60, Now));
        Assert.Null(book.Start("one too many", 60, Now));
        Assert.NotNull(book.Start("t0", 70, Now));
        Assert.Equal(TimerBook.MaxRunning, book.Running.Count);
    }

    [Fact]
    public void Running_timers_are_listed_with_the_one_ending_first()
    {
        var book = new TimerBook();
        book.Start("Long", 600, Now);
        book.Start("Short", 20, Now);
        book.Start("Middle", 90, Now);
        Assert.Equal(["Short", "Middle", "Long"], book.ByEnd().Select(t => t.Name));
    }

    [Fact]
    public void An_ended_timer_stays_for_a_few_seconds_and_then_goes()
    {
        var book = new TimerBook();
        book.Start("Haste", 60, Now);
        book.Start("Regen", 600, Now);

        Assert.False(book.Prune(Now.AddSeconds(60 + TimerClock.DoneShownSeconds)));    // just ended: still on show
        Assert.Equal(2, book.Running.Count);
        Assert.True(book.Prune(Now.AddSeconds(60 + TimerClock.DoneShownSeconds + 1)));
        Assert.Equal(["Regen"], book.Running.Select(t => t.Name));
        Assert.True(book.Stop("regen"));
        Assert.False(book.Stop("regen"));
    }

    [Fact]
    public void The_book_is_saved_per_character_and_read_back_with_the_running_timers_still_running()
    {
        using var s = new Fixture.Scratch();
        var folder = Path.Combine(s.Path, "srv-Aldric");
        var book = new TimerBook();
        book.AddPreset("Haste", 90);
        book.AddPreset("Respawn: ogre", 1800);
        book.Start("Haste", 90, Now);

        Assert.True(book.Save(folder));
        Assert.True(File.Exists(Path.Combine(folder, "timers.json")));
        Assert.False(File.Exists(Path.Combine(folder, "timers.json.tmp")));

        var back = TimerBook.Load(folder, Now.AddSeconds(30));
        Assert.Equal(book.Presets, back.Presets);
        var running = Assert.Single(back.Running);
        Assert.Equal(60, TimerBook.Remaining(running, Now.AddSeconds(30)), 3);

        Assert.Empty(TimerBook.Load(folder, Now.AddSeconds(90 + TimerClock.DoneShownSeconds + 5)).Running);   // ended while the app was closed, long enough ago
        Assert.Equal(2, TimerBook.Load(folder, Now.AddHours(5)).Presets.Count);
    }

    [Fact]
    public void A_missing_or_damaged_file_is_an_empty_book_and_a_hand_edited_one_is_held_to_the_same_rules()
    {
        using var s = new Fixture.Scratch();
        Assert.Empty(TimerBook.Load(null, Now).Presets);
        Assert.Empty(TimerBook.Load(Path.Combine(s.Path, "nowhere"), Now).Presets);

        var folder = Path.Combine(s.Path, "c");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "timers.json"), "{ not json");
        Assert.Empty(TimerBook.Load(folder, Now).Presets);

        File.WriteAllText(Path.Combine(folder, "timers.json"), """
            { "presets": [ {"name":"Good","seconds":60}, {"name":"","seconds":60}, {"name":"Zero","seconds":0}, {"name":"Huge","seconds":999999}, null, {"name":"good","seconds":70} ],
              "running": [ {"name":"Live","endsUtc":"2026-10-26T12:05:00Z","seconds":300}, {"name":"live","endsUtc":"2026-10-26T12:06:00Z","seconds":360}, {"name":"","endsUtc":"2026-10-26T12:05:00Z","seconds":300}, {"name":"Bad","endsUtc":"2026-10-26T12:05:00Z","seconds":0}, null ] }
            """);
        var book = TimerBook.Load(folder, Now);
        Assert.Equal([new TimerPreset("good", 70)], book.Presets);
        Assert.Single(book.Running);
        Assert.Equal("Live", book.Running[0].Name);
    }

    [Fact]
    public void Saving_without_a_folder_or_where_nothing_can_be_written_just_says_no()
    {
        Assert.False(new TimerBook().Save(null));
        using var s = new Fixture.Scratch();
        var blocked = Path.Combine(s.Path, "file");
        File.WriteAllText(blocked, "a file where the folder should be");
        Assert.False(new TimerBook().Save(Path.Combine(blocked, "inside")));
    }
}
