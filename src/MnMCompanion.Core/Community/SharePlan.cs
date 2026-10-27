using System.Security.Cryptography;
using System.Text;

namespace MnMCompanion.Community;

/// <summary>What the sharing loop does next.</summary>
public enum ShareStep
{
    /// <summary>Nothing: sharing is off, or it is not time yet.</summary>
    Wait,
    /// <summary>There is nothing worth sending yet (nothing looted, sold, gathered or heard).</summary>
    NothingToShare,
    /// <summary>The file is the same as the last one sent: sending it again would change nothing.</summary>
    Unchanged,
    /// <summary>Send the file now.</summary>
    Send,
}

/// <summary>
/// When a sharing player's totals are sent, in one place so the app and the tests agree. Only on Yes; the first time soon after start, then
/// no more often than the service takes a file (every 6 hours, so a little more); a file identical to the last one sent is not sent again
/// (the service already has it); and after a refusal or a failure the loop waits instead of hammering.
/// </summary>
public static class SharePlan
{
    /// <summary>The least time between two sends: the service takes one file per id every 6 hours.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(6) + TimeSpan.FromMinutes(5);

    /// <summary>Even an unchanged file is sent again after this long, so the service never forgets a player who is still playing.</summary>
    public static readonly TimeSpan Refresh = TimeSpan.FromDays(7);

    public static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AfterRefusal = TimeSpan.FromHours(6);

    /// <summary>Whether it is time to look at all (cheap: no file is built for this).</summary>
    public static bool Due(bool sharing, DateTime now, DateTime? lastSent, DateTime? notBefore) =>
        CommunityPolicy.MaySend(sharing) && (notBefore is null || now >= notBefore) && (lastSent is null || now - lastSent >= Every);

    /// <summary>What to do with the file just built.</summary>
    public static ShareStep Decide(Contribution file, string? lastFingerprint, DateTime now, DateTime? lastSent)
    {
        if (Nothing(file)) return ShareStep.NothingToShare;
        if (lastFingerprint is not null && lastFingerprint == Fingerprint(file) && lastSent is { } sent && now - sent < Refresh) return ShareStep.Unchanged;
        return ShareStep.Send;
    }

    /// <summary>No loot, sale, harvest, trade or NPC line in it: not worth a request.</summary>
    public static bool Nothing(Contribution file) =>
        file.Totals is { Corpses: 0, Sales: 0, Purchases: 0, Harvests: 0 } && file.Npcs.Count == 0 && (file.Trades ?? []).Count == 0;

    /// <summary>
    /// A fingerprint of what the file says, leaving out the day it was made, so a file that only differs by its date counts as unchanged.
    /// </summary>
    public static string Fingerprint(Contribution file) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((file with { Generated = "" }).ToCoreJson())));

    /// <summary>The same for the NPC dialogue, which is sent as its own document and so is judged unchanged on its own.</summary>
    public static string NpcFingerprint(Contribution file) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((file with { Generated = "" }).ToNpcPartJson())));

    /// <summary>The same for the trades, which are sent as a document of their own too.</summary>
    public static string TradesFingerprint(Contribution file) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((file with { Generated = "" }).ToTradesPartJson())));

    /// <summary>What to do with the trades document: nothing to send when there are no trades, nothing new when it is the one last sent, else send it.</summary>
    public static ShareStep DecideTrades(Contribution file, string? lastFingerprint, DateTime now, DateTime? lastSent)
    {
        if ((file.Trades ?? []).Count == 0) return ShareStep.NothingToShare;
        if (lastFingerprint is not null && lastFingerprint == TradesFingerprint(file) && lastSent is { } sent && now - sent < Refresh) return ShareStep.Unchanged;
        return ShareStep.Send;
    }

    /// <summary>What to do with the dialogue document: nothing to send when there are no NPC lines, nothing new when it is the one last sent, else send it.</summary>
    public static ShareStep DecidePart(Contribution file, string? lastFingerprint, DateTime now, DateTime? lastSent)
    {
        if (file.Npcs.Count == 0) return ShareStep.NothingToShare;
        if (lastFingerprint is not null && lastFingerprint == NpcFingerprint(file) && lastSent is { } sent && now - sent < Refresh) return ShareStep.Unchanged;
        return ShareStep.Send;
    }
}
