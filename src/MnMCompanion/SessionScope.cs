using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion;

/// <summary>
/// Which day, and which session of it, the small window shows. Every page of it (loot, vendor, trades, items, harvest, map) reads the same
/// one, so choosing "this session" or an earlier one on one page applies to all of them. A session is a stretch of time between two
/// markers the player made ("New session"); nothing is deleted when one starts, the ledger keeps everything.
/// </summary>
internal sealed class SessionScope(LootStore store)
{
    private DateOnly? _picked;       // null = follow today
    private DateTime? _scopeStart;   // a session picked from the menu (by its start), when not the whole day
    private bool _wholeDay;

    /// <summary>The day or session on show changed.</summary>
    public event Action? Changed;

    /// <summary>Something the player should be told (a session was archived, taken back).</summary>
    public event Action<string>? Said;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public DateOnly Day => _picked ?? Today;

    /// <summary>Changes whenever what is on show does, so a page can tell it is out of date.</summary>
    public string Key => $"{Day.DayNumber}|{(_wholeDay ? "whole" : _scopeStart?.Ticks.ToString() ?? "auto")}|{store.Sessions.Marks.Count}";

    /// <summary>The session on show: null for the whole day. By default today's current session, and a whole day for an earlier one.</summary>
    public LootSession? ActiveSession()
    {
        var sessions = store.Sessions;
        if (sessions.Marks.Count == 0 || _wholeDay) return null;
        if (_scopeStart is { } start) return sessions.All.FirstOrDefault(s => s.Start == start);
        return Day == Today ? sessions.Current : null;
    }

    /// <summary>What is on show as a stretch of time, for asking the event histories.</summary>
    public Period Period => SessionPeriods.Of(Day, ActiveSession());

    /// <summary>The chip's text: "This session: 8:09 PM to now", "Session 7:00 PM to 8:09 PM", "Whole day".</summary>
    public string ScopeLabel()
    {
        var session = ActiveSession();
        var events = store.Events(Day, session);
        DateTime? first = events.Count > 0 ? events.Min(e => e.Time) : null;
        return session is null ? "Whole day" : session.Open ? "This session: " + session.Label(first) : "Session " + session.Label(first);
    }

    public void ShowDay(DateOnly day)
    {
        _picked = day >= Today ? null : day;
        Reset(keepDay: true);
    }

    /// <summary>Back to today's current session (when the character changes).</summary>
    public void Reset(bool keepDay = false)
    {
        if (!keepDay) _picked = null;
        _wholeDay = false;
        _scopeStart = null;
        Changed?.Invoke();
    }

    public void ShowWholeDay()
    {
        _wholeDay = true;
        _scopeStart = null;
        Changed?.Invoke();
    }

    public void Step(int direction)
    {
        var days = store.Days();
        var day = Day;
        DateOnly? target = direction < 0
            ? days.Where(d => d < day).Select(d => (DateOnly?)d).FirstOrDefault()
            : days.Where(d => d > day).Select(d => (DateOnly?)d).LastOrDefault();
        if (target is null && direction > 0 && day < Today) target = Today;   // today, though it has nothing yet
        if (target is null) return;
        _picked = target >= Today ? null : target;
        Reset(keepDay: true);
    }

    public void StartSession()
    {
        var closed = store.NewSession("manual");
        _picked = null;
        Reset(keepDay: true);
        Said?.Invoke(closed is null ? "Nothing looted in this session yet: nothing to archive." : "Session archived (a text file in the sessions folder). Starting from nothing.");
    }

    /// <summary>
    /// A session was started by itself (a player joined the group): the pages redraw and the player is told.
    /// </summary>
    public void AutoStarted(string text)
    {
        Changed?.Invoke();   // by default what is on show is today's current session, so it moves to the new one; a session or the whole day the player picked stays
        Said?.Invoke(text);
    }

    public void UndoSession()
    {
        var undone = store.Sessions.RemoveLast();
        Reset(keepDay: true);
        if (undone) Said?.Invoke("The last session is merged back into the one before.");
    }

    public void OpenMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var active = ActiveSession();
        var dayEvents = store.Events(Day);
        MenuItem Item(string header, Action click, bool check = false, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsCheckable = false, IsChecked = check, IsEnabled = enabled };
            mi.Click += (_, _) => click();
            return mi;
        }
        foreach (var s in store.Sessions.All.Reverse())
        {
            var inside = dayEvents.Where(e => s.Contains(e.Time)).ToList();
            if (inside.Count == 0 && !(s.Open && Day == Today)) continue;
            var label = s.Label(inside.Count > 0 ? inside.Min(e => e.Time) : null) + (inside.Count > 0 ? $"  ·  {LootSummary.Totals(inside).Mobs} mobs" : "");
            var start = s.Start;
            menu.Items.Add(Item(s.Open ? "This session: " + label : "Session " + label, () => { _wholeDay = false; _scopeStart = start; Changed?.Invoke(); }, check: active is not null && active.Start == s.Start));
        }
        menu.Items.Add(Item("Whole day", ShowWholeDay, check: active is null));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("New session: archive this one and start again", StartSession));
        menu.Items.Add(Item("Undo the last new session (merge it back)", UndoSession, enabled: store.Sessions.Marks.Count > 0));
        menu.IsOpen = true;
    }
}
