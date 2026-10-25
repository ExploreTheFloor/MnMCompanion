using MnMCompanion.Loot;

namespace MnMCompanion.History;

/// <summary>Turns "this day, this session" (what the small window shows) into the stretch of time the event histories are asked for.</summary>
public static class SessionPeriods
{
    /// <summary>
    /// The day, or the part of it a session covers: from the session's start (or the start of the day when it began earlier, as the first
    /// session of all does) to its end (or the end of the day while it is still going).
    /// </summary>
    public static Period Of(DateOnly day, LootSession? session)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        if (session is null) return new Period("Whole day", start, end);
        var from = session.Start > start ? session.Start : start;
        var to = session.End is { } e && e < end ? e : end;
        return new Period(session.Open ? "This session" : "Session", from, to);
    }
}
