using MnMCompanion.Recipes;

namespace MnMCompanion.Tests;

/// <summary>
/// How rarely the community wiki is asked for its recipes: by itself at most once a day, never twice within an hour, never before a time it asked us to wait
/// until, the refresh button no more than once in ten minutes, and all of it remembered across restarts.
/// </summary>
public class RecipeRefreshTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static RefreshStep Auto(RecipeMeta meta, bool haveList = true, DateTime? listTime = null) =>
        RecipeRefresh.Decide(Now, haveList, meta, listTime, manual: false).Step;

    [Fact]
    public void A_list_confirmed_within_the_day_is_not_asked_about_however_often_the_page_is_opened()
    {
        var meta = new RecipeMeta { CheckedUtc = Now.AddHours(-23) };
        for (int opens = 0; opens < 20; opens++) Assert.Equal(RefreshStep.Fresh, Auto(meta));
    }

    [Fact]
    public void A_list_not_confirmed_for_more_than_a_day_is_asked_about_once()
    {
        Assert.Equal(RefreshStep.Ask, Auto(new RecipeMeta { CheckedUtc = Now.AddHours(-25) }));
    }

    [Fact]
    public void Where_nothing_says_when_it_was_confirmed_the_time_the_list_was_written_counts()
    {
        Assert.Equal(RefreshStep.Fresh, Auto(new RecipeMeta(), listTime: Now.AddHours(-5)));
        Assert.Equal(RefreshStep.Ask, Auto(new RecipeMeta(), listTime: Now.AddHours(-30)));
    }

    [Fact]
    public void With_no_list_at_all_the_wiki_is_asked()
    {
        Assert.Equal(RefreshStep.Ask, Auto(new RecipeMeta(), haveList: false));
        Assert.Equal(RefreshStep.Ask, Auto(new RecipeMeta { CheckedUtc = Now.AddHours(-1) }, haveList: false));   // a "checked" time with no list behind it proves nothing
    }

    [Fact]
    public void A_try_that_failed_is_not_repeated_within_the_hour_even_after_a_restart()
    {
        var meta = new RecipeMeta { CheckedUtc = Now.AddDays(-3), TriedUtc = Now.AddMinutes(-30) };
        Assert.Equal(RefreshStep.TooSoon, Auto(meta));
        Assert.Equal(RefreshStep.Ask, Auto(new RecipeMeta { CheckedUtc = Now.AddDays(-3), TriedUtc = Now.AddMinutes(-61) }));
    }

    [Fact]
    public void A_time_the_wiki_asked_to_wait_until_is_kept_to_by_the_page_and_by_the_button()
    {
        var meta = new RecipeMeta { CheckedUtc = Now.AddDays(-3), NotBeforeUtc = Now.AddMinutes(45) };
        Assert.Equal(RefreshStep.TooSoon, Auto(meta));
        var manual = RecipeRefresh.Decide(Now, true, meta, null, manual: true);
        Assert.Equal(RefreshStep.TooSoon, manual.Step);
        Assert.Contains("45 minutes", manual.Why);
        Assert.Equal(RefreshStep.Ask, Auto(new RecipeMeta { CheckedUtc = Now.AddDays(-3), NotBeforeUtc = Now.AddMinutes(-1) }));
    }

    [Fact]
    public void The_refresh_button_works_even_on_a_fresh_list_but_not_twice_in_ten_minutes()
    {
        var fresh = new RecipeMeta { CheckedUtc = Now.AddHours(-1), TriedUtc = Now.AddHours(-1) };
        Assert.Equal(RefreshStep.Ask, RecipeRefresh.Decide(Now, true, fresh, null, manual: true).Step);

        var justAsked = new RecipeMeta { CheckedUtc = Now.AddMinutes(-4), TriedUtc = Now.AddMinutes(-4) };
        var plan = RecipeRefresh.Decide(Now, true, justAsked, null, manual: true);
        Assert.Equal(RefreshStep.TooSoon, plan.Step);
        Assert.Contains("10 minutes", plan.Why);
        Assert.Equal(RefreshStep.Ask, RecipeRefresh.Decide(Now, true, new RecipeMeta { TriedUtc = Now.AddMinutes(-11) }, null, manual: true).Step);
    }

    [Theory]
    [InlineData(null, 60)]           // the wiki didn't say: an hour
    [InlineData(10, 1)]              // seconds, but never less than a minute
    [InlineData(600, 10)]
    [InlineData(432_000, 1440)]      // five days asked: never more than a day
    public void Backing_off_waits_as_asked_within_a_minute_and_a_day(int? seconds, int expectedMinutes)
    {
        var meta = new RecipeMeta();
        meta.BackOff(seconds is { } s ? TimeSpan.FromSeconds(s) : null, Now);
        Assert.Equal(Now.AddMinutes(expectedMinutes), meta.NotBeforeUtc);
    }

    [Fact]
    public void What_was_asked_is_remembered_beside_the_list_and_a_damaged_note_is_just_forgotten()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mnm-meta-" + Guid.NewGuid().ToString("N"));
        var list = Path.Combine(folder, "recipes.json");
        try
        {
            Assert.Equal(Path.Combine(folder, "recipes.meta.json"), RecipeMeta.PathFor(list));
            Assert.Null(RecipeMeta.Load(list).CheckedUtc);

            var meta = new RecipeMeta { ETag = "W/\"abc\"", CheckedUtc = Now, TriedUtc = Now.AddMinutes(1), NotBeforeUtc = Now.AddHours(2) };
            Assert.True(meta.Save(list));
            var back = RecipeMeta.Load(list);
            Assert.Equal(("W/\"abc\"", Now, Now.AddMinutes(1), Now.AddHours(2)), (back.ETag, back.CheckedUtc, back.TriedUtc, back.NotBeforeUtc));

            File.WriteAllText(RecipeMeta.PathFor(list), "{ half a file");
            Assert.Null(RecipeMeta.Load(list).ETag);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
