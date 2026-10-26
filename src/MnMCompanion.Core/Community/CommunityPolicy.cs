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
    /// Whether to ask at start-up: only while the answer is No, and never for a run with no window (a picture, a report, a headless
    /// export), which can't ask anyone.
    /// </summary>
    public static bool ShouldAsk(bool sharing, bool askingIsAllowed) => !sharing && askingIsAllowed;

    /// <summary>One line saying where things stand, for the sharing window and the menu.</summary>
    public static string Status(bool sharing, bool sendingConnected) =>
        !sharing ? "Not sharing. Nothing leaves your PC, and community data isn't used."
        : sendingConnected ? "Sharing is on. Your totals are sent automatically and kept up to date, and community data is available to you."
        : "Sharing is on. Sending isn't connected yet: your Yes is saved, and it starts once the community intake is connected.";
}
