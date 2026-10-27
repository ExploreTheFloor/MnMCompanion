using MnMCompanion.Timers;
using MnMCompanion.Update;

namespace MnMCompanion.Tests;

/// <summary>When a newer version installs by itself, when it waits, and that the timers start out switched off.</summary>
public class UpdatePolicyTests
{
    private static UpdateOffer Offer(string version = "0.4.2") =>
        new(Version.Parse(version), "v" + version, "", "", "", "", 0);

    [Fact]
    public void A_newer_version_installs_by_itself_when_nothing_says_otherwise()
        => Assert.True(UpdatePolicy.InstallWithoutAsking(installAutomatically: true, canInstall: true, Offer(), skippedVersion: null, lastInstalledVersion: null, declinedThisRun: false));

    [Fact]
    public void Switching_it_off_makes_it_only_an_offer()
        => Assert.False(UpdatePolicy.InstallWithoutAsking(false, true, Offer(), null, null, false));

    [Fact]
    public void A_copy_that_cannot_replace_itself_is_sent_to_the_download_page_instead()
        => Assert.False(UpdatePolicy.InstallWithoutAsking(true, false, Offer(), null, null, false));

    [Fact]
    public void A_skipped_version_is_not_installed_but_a_newer_one_is()
    {
        Assert.False(UpdatePolicy.InstallWithoutAsking(true, true, Offer("0.4.2"), "0.4.2", null, false));
        Assert.True(UpdatePolicy.InstallWithoutAsking(true, true, Offer("0.4.3"), "0.4.2", null, false));
    }

    [Fact]
    public void A_version_already_installed_once_is_never_installed_by_itself_again()
    {
        // the program that started after that install is still older: no restart loop
        Assert.False(UpdatePolicy.InstallWithoutAsking(true, true, Offer("0.4.2"), null, "0.4.2", false));
        Assert.True(UpdatePolicy.InstallWithoutAsking(true, true, Offer("0.4.3"), null, "0.4.2", false));
    }

    [Fact]
    public void Not_now_holds_it_back_for_the_rest_of_the_run()
        => Assert.False(UpdatePolicy.InstallWithoutAsking(true, true, Offer(), null, null, declinedThisRun: true));

    [Theory]
    [InlineData(false, false, true)]    // nothing running: restart
    [InlineData(true, false, false)]    // a timer is counting down: wait
    [InlineData(true, true, true)]      // the player pressed Restart now
    [InlineData(false, true, true)]
    public void The_restart_waits_for_running_timers_unless_asked_to_go(bool running, bool requested, bool restarts)
        => Assert.Equal(restarts, UpdatePolicy.MayRestart(running, requested));

    // ------------------------------------------------------------------ the new program waits for the old one to exit

    [Fact]
    public void The_new_program_waits_while_the_old_ones_file_is_locked_and_goes_on_when_it_is_released()
    {
        using var s = new Fixture.Scratch();
        var exe = s.Write("MnMCompanion.exe", "new");
        var old = s.Write("MnMCompanion.exe.old", "old");

        // a running program keeps its file open: a lock that nothing else can delete
        var running = new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Assert.False(UpdateService.WaitForPreviousToExit(exe, TimeSpan.FromMilliseconds(300)));
            Assert.True(File.Exists(old));
        }
        finally { running.Dispose(); }

        Assert.True(UpdateService.WaitForPreviousToExit(exe, TimeSpan.FromSeconds(5)));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task The_wait_ends_as_soon_as_the_old_program_lets_go()
    {
        using var s = new Fixture.Scratch();
        var exe = s.Write("MnMCompanion.exe", "new");
        var old = s.Write("MnMCompanion.exe.old", "old");
        var running = new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read);
        var letGo = Task.Run(async () => { await Task.Delay(400); running.Dispose(); });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(UpdateService.WaitForPreviousToExit(exe, TimeSpan.FromSeconds(10)));
        Assert.InRange(watch.ElapsedMilliseconds, 300, 5000);
        await letGo;
    }

    [Fact]
    public void Nothing_to_wait_for_returns_at_once()
    {
        using var s = new Fixture.Scratch();
        Assert.True(UpdateService.WaitForPreviousToExit(Path.Combine(s.Path, "MnMCompanion.exe"), TimeSpan.FromSeconds(5)));
        Assert.True(UpdateService.WaitForPreviousToExit(null, TimeSpan.FromSeconds(5)));
    }

    // ------------------------------------------------------------------ timers start switched off

    [Fact]
    public void A_new_timer_book_has_no_timers_no_keys_no_sounds_and_no_corpse_timer()
    {
        var book = new TimerBook();
        Assert.Empty(book.Presets);
        Assert.Empty(book.Running);
        Assert.False(book.Corpse.On);
        Assert.Null(book.Corpse.Sound);
        Assert.DoesNotContain(book.Presets, p => p.Key is not null || p.Sound is not null);
    }

    [Fact]
    public void A_timer_book_with_nothing_saved_loads_as_the_same_switched_off_book()
    {
        using var scratch = new Fixture.Scratch();
        var book = TimerBook.Load(Path.Combine(scratch.Path, "srv-Aldric"), new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc));
        Assert.Empty(book.Presets);
        Assert.Empty(book.Running);
        Assert.False(book.Corpse.On);
        Assert.Null(book.Corpse.Sound);
    }

    [Fact]
    public void A_corpse_timer_setting_is_off_without_a_sound_even_after_cleaning()
    {
        var cleaned = new CorpseTimerSetting().Cleaned();
        Assert.False(cleaned.On);
        Assert.Null(cleaned.Sound);
    }
}
