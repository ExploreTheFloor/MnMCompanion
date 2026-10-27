using MnMCompanion.Loot;

namespace MnMCompanion.Timers;

/// <summary>
/// One looted corpse's timer. <see cref="Label"/> is what the bar says (the timer's name and the monster, "Corpse: a bog crawler"),
/// <see cref="Id"/> tells one corpse from another so the bar can be kept while it counts down.
/// </summary>
public sealed record CorpseTimer(string Id, string Label, string Monster, string Zone, DateTime EndsLocal, int Seconds, bool You);

/// <summary>
/// The corpse timer: when it is on, every monster corpse that is looted gets a timer of the player's own name and length, starting when the loot
/// was taken. Nothing is stored for it. It is worked out from the ledger's loot every time, so it is the same after a restart, and it also covers
/// a corpse looted while Companion was closed (as long as its time has not run out).
/// </summary>
public static class CorpseTimers
{
    /// <summary>The window shows this many (the ones ending soonest); the rest are counted.</summary>
    public const int MaxShown = 8;

    /// <summary>
    /// The corpse timers that are running, or ended less than <see cref="TimerClock.DoneShownSeconds"/> ago, the one ending soonest first. A player's
    /// corpse is not a monster and gets none. Other looters' corpses (a group's) are counted only when the player chose <see cref="CorpseTimerSetting.Group"/>.
    /// </summary>
    public static IReadOnlyList<CorpseTimer> Active(IEnumerable<LootedCorpse> corpses, CorpseTimerSetting setting, DateTime nowLocal)
    {
        if (!setting.On) return [];
        var name = TimerBook.CleanName(setting.Name) ?? CorpseTimerSetting.DefaultName;
        int seconds = Math.Clamp(setting.Seconds, 1, TimerClock.MaxSeconds);
        return corpses
            .Where(c => c.Mob && (c.You || setting.Group))
            .Select(c => new CorpseTimer($"{c.Start.Ticks}|{c.Who}|{c.Source}", $"{name}: {c.Source}", c.Source, c.Zone, c.Start.AddSeconds(seconds), seconds, c.You))
            .Where(t => (t.EndsLocal - nowLocal).TotalSeconds > -TimerClock.DoneShownSeconds)
            .OrderBy(t => t.EndsLocal).ThenBy(t => t.Monster, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
