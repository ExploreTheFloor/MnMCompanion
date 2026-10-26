using MnMCompanion.Community;
using MnMCompanion.History;

namespace MnMCompanion.Tests;

/// <summary>When a sharing player's totals are sent: only on Yes, shortly after start, no more than every few hours, and not again when nothing changed.</summary>
public class SharePlanTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private const string Id = "a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private static Contribution File(int harvests = 1, DateOnly? day = null, string[]? npcLines = null) =>
        ContributionBuilder.Build([], [], harvests == 0 ? [] : [new HarvestRow(Now, "A", "Wood", harvests)], [], Id, "MnMCompanion 0.2.0", day ?? new DateOnly(2026, 10, 26),
            npcLines is null ? null : [new ContributionNpc("Abbot Harrow", npcLines)]);

    [Fact]
    public void Nothing_is_ever_due_while_the_answer_is_no()
    {
        Assert.False(SharePlan.Due(sharing: false, Now, lastSent: null, notBefore: null));
        Assert.False(SharePlan.Due(sharing: false, Now, lastSent: Now.AddDays(-30), notBefore: null));
    }

    [Fact]
    public void A_yes_that_has_never_sent_is_due_at_once_then_not_until_the_service_would_take_another()
    {
        Assert.True(SharePlan.Due(true, Now, null, null));
        Assert.False(SharePlan.Due(true, Now, Now.AddHours(-1), null));
        Assert.False(SharePlan.Due(true, Now, Now.AddHours(-6), null), "the service takes one file per id every 6 hours: a little more is waited");
        Assert.True(SharePlan.Due(true, Now, Now.AddHours(-6).AddMinutes(-6), null));
        Assert.True(SharePlan.Due(true, Now, Now.AddDays(-3), null));
    }

    [Fact]
    public void A_wait_after_a_refusal_or_a_failure_holds_it_back_until_it_is_over()
    {
        Assert.False(SharePlan.Due(true, Now, null, notBefore: Now.AddMinutes(10)));
        Assert.True(SharePlan.Due(true, Now, null, notBefore: Now.AddMinutes(-1)));
        Assert.True(SharePlan.AfterFailure < SharePlan.AfterRefusal);
        Assert.True(SharePlan.AfterRefusal <= SharePlan.Every);
    }

    [Fact]
    public void A_file_with_nothing_in_it_is_not_sent()
    {
        Assert.Equal(ShareStep.NothingToShare, SharePlan.Decide(File(harvests: 0), null, Now, null));
        Assert.Equal(ShareStep.Send, SharePlan.Decide(File(harvests: 0, npcLines: ["Hello."]), null, Now, null));   // NPC lines alone are something to share
        Assert.Equal(ShareStep.Send, SharePlan.Decide(File(), null, Now, null));
    }

    [Fact]
    public void A_file_that_has_not_changed_since_the_last_send_is_not_sent_again_until_a_week_has_passed()
    {
        var sent = File();
        var fingerprint = SharePlan.Fingerprint(sent);

        Assert.Equal(ShareStep.Unchanged, SharePlan.Decide(File(), fingerprint, Now, Now.AddDays(-1)));
        Assert.Equal(ShareStep.Unchanged, SharePlan.Decide(File(day: new DateOnly(2026, 10, 27)), fingerprint, Now, Now.AddDays(-1)));   // only the date differs
        Assert.Equal(ShareStep.Send, SharePlan.Decide(File(harvests: 2), fingerprint, Now, Now.AddDays(-1)));                              // something new
        Assert.Equal(ShareStep.Send, SharePlan.Decide(File(), fingerprint, Now, Now.AddDays(-8)));                                         // refreshed after a week
        Assert.Equal(ShareStep.Send, SharePlan.Decide(File(), null, Now, null));                                                           // never sent
    }

    [Fact]
    public void The_fingerprint_follows_what_the_file_says_and_not_when_it_was_made()
    {
        Assert.Equal(SharePlan.Fingerprint(File()), SharePlan.Fingerprint(File(day: new DateOnly(2026, 10, 28))));
        Assert.Equal(64, SharePlan.Fingerprint(File()).Length);
    }

    [Fact]
    public void The_dialogue_is_judged_on_its_own_because_it_is_sent_on_its_own()
    {
        var plain = File();
        var talking = File(npcLines: ["Hello."]);
        var other = File(npcLines: ["Goodbye."]);

        // the totals' fingerprint doesn't change with the dialogue, and the dialogue's does
        Assert.Equal(SharePlan.Fingerprint(plain), SharePlan.Fingerprint(talking));
        Assert.NotEqual(SharePlan.NpcFingerprint(talking), SharePlan.NpcFingerprint(other));
        Assert.Equal(SharePlan.NpcFingerprint(talking), SharePlan.NpcFingerprint(File(npcLines: ["Hello."], day: new DateOnly(2026, 10, 28))));   // not the day

        var fingerprint = SharePlan.NpcFingerprint(talking);
        Assert.Equal(ShareStep.NothingToShare, SharePlan.DecidePart(plain, null, Now, null));
        Assert.Equal(ShareStep.Send, SharePlan.DecidePart(talking, null, Now, null));
        Assert.Equal(ShareStep.Unchanged, SharePlan.DecidePart(talking, fingerprint, Now, Now.AddDays(-1)));
        Assert.Equal(ShareStep.Send, SharePlan.DecidePart(other, fingerprint, Now, Now.AddDays(-1)));      // she said something new
        Assert.Equal(ShareStep.Send, SharePlan.DecidePart(talking, fingerprint, Now, Now.AddDays(-8)));    // refreshed after a week
    }
}
