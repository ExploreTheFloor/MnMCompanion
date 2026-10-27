using MnMCompanion.Community;

namespace MnMCompanion.Tests;

/// <summary>The sharing rules: No by default, asked at every start while No, nothing sent and no community data unless Yes.</summary>
public class CommunityPolicyTests
{
    [Fact]
    public void A_yes_covers_only_the_file_the_player_was_shown()
    {
        // a Yes from before the layout was recorded (0) is a Yes to layout 1
        Assert.True(CommunityPolicy.Consented(true, answeredSchema: 0, currentSchema: 1));
        Assert.False(CommunityPolicy.Consented(true, answeredSchema: 0, currentSchema: 2));
        Assert.False(CommunityPolicy.Consented(true, answeredSchema: 1, currentSchema: 2));
        Assert.True(CommunityPolicy.Consented(true, answeredSchema: 2, currentSchema: 2));
        Assert.True(CommunityPolicy.Consented(true, answeredSchema: 3, currentSchema: 2));
        Assert.False(CommunityPolicy.Consented(false, answeredSchema: 2, currentSchema: 2));

        // only a Yes that has lapsed needs renewing; a No never does
        Assert.True(CommunityPolicy.NeedsRenewal(true, 0, 2));
        Assert.False(CommunityPolicy.NeedsRenewal(true, 2, 2));
        Assert.False(CommunityPolicy.NeedsRenewal(false, 0, 2));
    }

    [Fact]
    public void The_renewal_question_says_only_what_was_added_since_the_players_yes()
    {
        const int now = 4;
        var fromTrades = CommunityPolicy.RenewalNote(3, now);
        Assert.Contains("the server each of those is from", fromTrades);
        Assert.DoesNotContain("trades with other players", fromTrades);   // they already agreed to those
        Assert.DoesNotContain("running totals", fromTrades);
        Assert.StartsWith("You said Yes before. Companion now also shares ", fromTrades);
        Assert.Contains("Sharing is paused until you say Yes again", fromTrades);

        var fromTotals = CommunityPolicy.RenewalNote(2, now);
        Assert.Contains("trades with other players", fromTotals);
        Assert.Contains("the server", fromTotals);
        Assert.DoesNotContain("running totals", fromTotals);

        // a Yes from before the layout was recorded is a Yes to layout 1: everything since is new
        var fromTheStart = CommunityPolicy.RenewalNote(0, now);
        Assert.Contains("running totals", fromTheStart);
        Assert.Contains("trades with other players", fromTheStart);
        Assert.Contains("the server", fromTheStart);
        Assert.Equal(CommunityPolicy.NewSince(1, now), CommunityPolicy.NewSince(0, now));

        // nothing to say when the Yes already covers the layout, and each note matches the renewal rule
        Assert.Equal("", CommunityPolicy.RenewalNote(now, now));
        Assert.Empty(CommunityPolicy.NewSince(now, now));
        for (int answered = 0; answered <= now; answered++)
            Assert.Equal(CommunityPolicy.NeedsRenewal(true, answered, now), CommunityPolicy.RenewalNote(answered, now).Length > 0);
    }

    [Fact]
    public void The_current_file_layout_is_the_one_a_yes_is_given_to()
    {
        Assert.Equal(4, ContributionBuilder.Schema);
    }

    [Fact]
    public void Only_a_yes_may_send_or_use_community_data()
    {
        Assert.False(CommunityPolicy.MaySend(false));
        Assert.False(CommunityPolicy.MayUseCommunityData(false));
        Assert.True(CommunityPolicy.MaySend(true));
        Assert.True(CommunityPolicy.MayUseCommunityData(true));
    }

    [Fact]
    public void The_player_is_asked_at_every_start_while_the_answer_is_no_and_never_after_a_yes()
    {
        Assert.True(CommunityPolicy.ShouldAsk(sharing: false, askingIsAllowed: true));
        Assert.False(CommunityPolicy.ShouldAsk(sharing: true, askingIsAllowed: true));
    }

    [Fact]
    public void A_run_that_cannot_ask_anyone_never_asks()
    {
        Assert.False(CommunityPolicy.ShouldAsk(sharing: false, askingIsAllowed: false));
    }

    [Fact]
    public void The_status_line_says_what_is_true_in_each_state()
    {
        Assert.Contains("Nothing leaves your PC", CommunityPolicy.Status(false, sendingConnected: false));
        Assert.Contains("Nothing leaves your PC", CommunityPolicy.Status(false, sendingConnected: true));
        Assert.Contains("isn't connected yet", CommunityPolicy.Status(true, sendingConnected: false));
        Assert.Contains("sent automatically", CommunityPolicy.Status(true, sendingConnected: true));
        Assert.EndsWith("Last sent today.", CommunityPolicy.Status(true, sendingConnected: true, note: "Last sent today."));   // how the last send went
        Assert.DoesNotContain("Last sent", CommunityPolicy.Status(false, sendingConnected: true, note: "Last sent today."));   // never said while the answer is No
    }
}
