namespace MnMCompanion.Market;

public enum MarketStep
{
    /// <summary>The kept snapshot is the current one: the site publishes every six hours and the next is not due.</summary>
    Fresh,
    /// <summary>Not yet: the site was asked a moment ago, or told us to wait.</summary>
    TooSoon,
    /// <summary>Ask the site now.</summary>
    Ask,
}

public sealed record MarketPlan(MarketStep Step, string Why = "");

/// <summary>
/// When mnm-db.com may be asked for its auction feed. The site publishes the feed every six hours (and takes a few minutes to put it up), so
/// a snapshot is current until six hours and a little have passed since it was made; after that the page asks once when it is opened, never
/// twice within an hour even when the answer failed or was "unchanged", never before a time the site asked us to wait until, and the refresh
/// button no more than once in ten minutes. The question carries the snapshot's version tag, so an unchanged feed costs the site almost nothing.
/// </summary>
public static class MarketRefresh
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(6);
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan BetweenTries = TimeSpan.FromHours(1);
    public static readonly TimeSpan BetweenManual = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultBackoff = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromDays(1);

    /// <param name="generatedAtUtc">When the kept snapshot was made (the feed says); null with no book.</param>
    /// <param name="manual">The player pressed the refresh button (or Try again).</param>
    public static MarketPlan Decide(DateTime nowUtc, DateTime? generatedAtUtc, MarketMeta meta, bool manual)
    {
        if (meta.NotBeforeUtc is { } notBefore && nowUtc < notBefore)
            return new MarketPlan(MarketStep.TooSoon, $"mnm-db.com asked to be left alone for a while: it won't be asked again for about {Minutes(notBefore - nowUtc)}.");
        if (manual)
        {
            if (meta.TriedUtc is { } last && nowUtc - last < BetweenManual)
                return new MarketPlan(MarketStep.TooSoon, $"mnm-db.com was asked {Minutes(nowUtc - last)} ago: it is only asked again after {BetweenManual.TotalMinutes:0} minutes.");
            return new MarketPlan(MarketStep.Ask);
        }
        if (generatedAtUtc is { } made && nowUtc - made < Every + Grace) return new MarketPlan(MarketStep.Fresh);
        if (meta.TriedUtc is { } tried && nowUtc - tried < BetweenTries)
            return new MarketPlan(MarketStep.TooSoon, "mnm-db.com was asked a short while ago.");
        return new MarketPlan(MarketStep.Ask);
    }

    /// <summary>"2 hours ago", "just now": how old a snapshot is, for the page's heading.</summary>
    public static string Age(DateTime whenUtc, DateTime? nowUtc = null)
    {
        var span = (nowUtc ?? DateTime.UtcNow) - whenUtc;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromHours(48)) return $"{(int)Math.Round(span.TotalHours)} h ago";
        return $"{(int)Math.Round(span.TotalDays)} days ago";
    }

    private static string Minutes(TimeSpan span)
    {
        int m = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes));
        return m == 1 ? "1 minute" : m < 120 ? $"{m} minutes" : $"{(int)Math.Ceiling(span.TotalHours)} hours";
    }
}
