namespace MnMCompanion.Community;

/// <summary>
/// The rules about sharing, in one place so the app and the tests agree. A player either shares their own totals with the community
/// data (Yes) or doesn't (No, the default). While it is No nothing leaves their PC, they are asked again every time the app starts,
/// and the community data isn't used for them.
/// </summary>
public static class CommunityPolicy
{
    /// <summary>
    /// Whether the app may ask for or show community data for this player: only if they share theirs. (The data is kept privately by the
    /// project, never published; this is the app's own rule about what it does for the player.)
    /// </summary>
    public static bool MayUseCommunityData(bool sharing) => sharing;

    /// <summary>Whether the app may send this player's totals: only on Yes.</summary>
    public static bool MaySend(bool sharing) => sharing;

    /// <summary>
    /// Whether a player's Yes still covers what the app would send. A Yes is given to the file layout the player was shown
    /// (<see cref="ContributionBuilder.Schema"/>); a Yes from before a layout that carries more than they saw (no layout recorded, so 1)
    /// is not a Yes to the new one, and the app asks again, sending and using community data in the meantime as it does on a No.
    /// </summary>
    public static bool Consented(bool yes, int answeredSchema, int currentSchema) => yes && Math.Max(1, answeredSchema) >= currentSchema;

    /// <summary>Whether a Yes was given and now needs renewing because the file has grown since: the question then says why.</summary>
    public static bool NeedsRenewal(bool yes, int answeredSchema, int currentSchema) => yes && !Consented(yes, answeredSchema, currentSchema);

    /// <summary>
    /// What each layout added to what is shared, in the words the question uses (it says "Companion now also shares ..."): the layout a Yes was given to has
    /// everything up to it, so a player is told only what came after.
    /// </summary>
    private static readonly (int Schema, string Added)[] Additions =
    [
        (2, "your running totals, counted by week (never a day or a time)"),
        (3, "your trades with other players (what each side handed over, the items and the coin, with the day and the zone, and never who the other player was) and what you dropped and destroyed"),
        (4, "the server each of those is from (the game's own name for it), because what players pay each other differs between servers"),
    ];

    /// <summary>What a Yes given to layout <paramref name="answeredSchema"/> does not cover yet, up to <paramref name="currentSchema"/>; empty when it covers everything.</summary>
    public static IReadOnlyList<string> NewSince(int answeredSchema, int currentSchema) =>
        Additions.Where(a => a.Schema > Math.Max(1, answeredSchema) && a.Schema <= currentSchema).Select(a => a.Added).ToList();

    /// <summary>The sentence the renewal question opens with, or "" when nothing has been added since the player's Yes.</summary>
    public static string RenewalNote(int answeredSchema, int currentSchema)
    {
        var added = NewSince(answeredSchema, currentSchema);
        return added.Count == 0
            ? ""
            : "You said Yes before. Companion now also shares " + string.Join(", and ", added) + ". Sharing is paused until you say Yes again. \"What is shared\" lists everything.";
    }

    /// <summary>
    /// Whether to ask at start-up: only while the answer is No, and never for a run with no window (a picture, a report, a headless
    /// export), which can't ask anyone.
    /// </summary>
    public static bool ShouldAsk(bool sharing, bool askingIsAllowed) => !sharing && askingIsAllowed;

    /// <summary>One line saying where things stand, for the sharing window and the menu.</summary>
    public static string Status(bool sharing, bool sendingConnected, string? note = null) =>
        !sharing ? "Not sharing. Nothing leaves your PC, and community data isn't used."
        : sendingConnected ? "Sharing is on. Your totals are sent automatically, no more than every few hours, and replace your last ones; you can use what everyone has shared."
            + (note is { Length: > 0 } ? " " + note : "")
        : "Sharing is on. Sending isn't connected yet: your Yes is saved, and it starts once the community intake is connected.";
}
