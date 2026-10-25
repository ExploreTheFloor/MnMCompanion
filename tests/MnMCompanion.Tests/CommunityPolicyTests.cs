using MnMCompanion.Community;

namespace MnMCompanion.Tests;

/// <summary>The sharing rules: No by default, asked at every start while No, nothing sent and no community data unless Yes.</summary>
public class CommunityPolicyTests
{
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
    }
}
