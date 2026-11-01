using MnMCompanion.Market;

namespace MnMCompanion.Tests;

/// <summary>
/// How rarely mnm-db.com is asked for its feed: a snapshot is current for six hours and a little (the site publishes every six hours); after that the
/// page asks once, never twice within an hour, never before a time the site asked us to wait until, and the refresh button no more than once in ten minutes.
/// </summary>
public class MarketRefreshTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 20, 0, 0, DateTimeKind.Utc);

    private static MarketStep Auto(DateTime? made, MarketMeta? meta = null) => MarketRefresh.Decide(Now, made, meta ?? new MarketMeta(), manual: false).Step;

    [Fact]
    public void A_snapshot_less_than_six_hours_and_twenty_minutes_old_is_current_however_often_the_page_is_opened()
    {
        for (int opens = 0; opens < 20; opens++) Assert.Equal(MarketStep.Fresh, Auto(Now.AddHours(-6).AddMinutes(-19)));
        Assert.Equal(MarketStep.Fresh, Auto(Now.AddMinutes(-1)));
    }

    [Fact]
    public void An_older_snapshot_is_asked_about_once()
    {
        Assert.Equal(MarketStep.Ask, Auto(Now.AddHours(-6).AddMinutes(-21)));
        Assert.Equal(MarketStep.Ask, Auto(Now.AddDays(-3)));
    }

    [Fact]
    public void With_no_book_the_site_is_asked()
    {
        Assert.Equal(MarketStep.Ask, Auto(null));
    }

    [Fact]
    public void Asked_within_the_hour_it_is_not_asked_again_whatever_the_answer_was()
    {
        var meta = new MarketMeta { TriedUtc = Now.AddMinutes(-30) };
        Assert.Equal(MarketStep.TooSoon, Auto(null, meta));
        Assert.Equal(MarketStep.TooSoon, Auto(Now.AddDays(-2), meta));
        Assert.Equal(MarketStep.Ask, Auto(Now.AddDays(-2), new MarketMeta { TriedUtc = Now.AddMinutes(-61) }));
    }

    [Fact]
    public void A_wait_the_site_asked_for_is_kept_by_the_page_and_by_the_button()
    {
        var meta = new MarketMeta { NotBeforeUtc = Now.AddMinutes(10) };
        Assert.Equal(MarketStep.TooSoon, Auto(null, meta));
        var manual = MarketRefresh.Decide(Now, null, meta, manual: true);
        Assert.Equal(MarketStep.TooSoon, manual.Step);
        Assert.Contains("10 minutes", manual.Why);
        Assert.Equal(MarketStep.Ask, MarketRefresh.Decide(Now.AddMinutes(10), null, meta, manual: true).Step);
    }

    [Fact]
    public void The_button_asks_at_most_once_in_ten_minutes_even_for_a_current_snapshot()
    {
        Assert.Equal(MarketStep.Ask, MarketRefresh.Decide(Now, Now.AddMinutes(-5), new MarketMeta(), manual: true).Step);
        var plan = MarketRefresh.Decide(Now, Now.AddMinutes(-5), new MarketMeta { TriedUtc = Now.AddMinutes(-3) }, manual: true);
        Assert.Equal(MarketStep.TooSoon, plan.Step);
        Assert.Contains("3 minutes ago", plan.Why);
        Assert.Equal(MarketStep.Ask, MarketRefresh.Decide(Now, Now.AddMinutes(-5), new MarketMeta { TriedUtc = Now.AddMinutes(-11) }, manual: true).Step);
    }

    [Fact]
    public void Ages_read_as_a_person_would_say_them()
    {
        Assert.Equal("just now", MarketRefresh.Age(Now.AddSeconds(-20), Now));
        Assert.Equal("45 min ago", MarketRefresh.Age(Now.AddMinutes(-45), Now));
        Assert.Equal("2 h ago", MarketRefresh.Age(Now.AddHours(-2.2), Now));
        Assert.Equal("3 days ago", MarketRefresh.Age(Now.AddDays(-3), Now));
    }
}
