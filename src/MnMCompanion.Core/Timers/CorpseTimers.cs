using MnMCompanion.Loot;

namespace MnMCompanion.Timers;

/// <summary>
/// One looted corpse's timer. <see cref="Label"/> is what the bar says (the timer's name and the monster, "Corpse: a bog crawler"),
/// <see cref="Id"/> tells one corpse from another so the bar can be kept while it counts down (and so one can be left out, see <see cref="TimerBook.LeaveOutCorpse"/>).
/// </summary>
public sealed record CorpseTimer(string Id, string Label, string Monster, string Zone, DateTime EndsLocal, int Seconds, bool You);

/// <summary>
/// The corpse timer: when it is on, every monster corpse that is looted gets a timer of the player's own name and length, starting when the loot
/// was taken. Nothing is stored for the timers themselves. They are worked out from the ledger's loot every time, so they are the same after a restart, and
/// they also cover a corpse looted while Companion was closed (as long as its time has not run out). What is stored is what the player left out: single
/// corpses by <see cref="CorpseTimer.Id"/> (<see cref="TimerBook.LeftOutCorpses"/>) and whole kinds of monster by name (<see cref="CorpseTimerSetting.Skip"/>).
/// </summary>
public static class CorpseTimers
{
    /// <summary>The window shows this many (the ones ending soonest); the rest are counted.</summary>
    public const int MaxShown = 8;

    /// <summary>The id a corpse's timer goes by: when it was looted, by whom, and what it was.</summary>
    public static string IdOf(LootedCorpse c) => $"{c.Start.Ticks}|{c.Who}|{c.Source}";

    /// <summary>When the corpse of <paramref name="id"/> was looted (local time), or null for an id that isn't one of ours.</summary>
    public static DateTime? StartOf(string id)
    {
        int bar = id.IndexOf('|');
        if (bar <= 0 || !long.TryParse(id.AsSpan(0, bar), out long ticks) || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return null;
        return new DateTime(ticks);
    }

    /// <summary>
    /// The corpse timers that are running, or ended less than <see cref="TimerClock.DoneShownSeconds"/> ago, the one ending soonest first. A player's
    /// corpse is not a monster and gets none. Every other corpse that was looted does, whoever looted it, unless the player left that corpse out
    /// (<paramref name="leftOut"/>, by id) or stopped timing that kind of monster (<see cref="CorpseTimerSetting.Skip"/>).
    /// </summary>
    public static IReadOnlyList<CorpseTimer> Active(IEnumerable<LootedCorpse> corpses, CorpseTimerSetting setting, DateTime nowLocal, IReadOnlyCollection<string>? leftOut = null)
    {
        if (!setting.On) return [];
        var name = TimerBook.CleanName(setting.Name) ?? CorpseTimerSetting.DefaultName;
        int seconds = Math.Clamp(setting.Seconds, 1, TimerClock.MaxSeconds);
        var skipped = new HashSet<string>(setting.Skip, StringComparer.OrdinalIgnoreCase);
        var left = leftOut is null ? null : leftOut as ISet<string> ?? new HashSet<string>(leftOut, StringComparer.Ordinal);
        return corpses
            .Where(c => c.Mob && !skipped.Contains(c.Source))
            .Select(c => new CorpseTimer(IdOf(c), $"{name}: {c.Source}", c.Source, c.Zone, c.Start.AddSeconds(seconds), seconds, c.You))
            .Where(t => left is null || !left.Contains(t.Id))
            .Where(t => (t.EndsLocal - nowLocal).TotalSeconds > -TimerClock.DoneShownSeconds)
            .OrderBy(t => t.EndsLocal).ThenBy(t => t.Monster, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
