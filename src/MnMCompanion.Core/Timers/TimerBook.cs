using System.Globalization;
using System.Text.Json;

namespace MnMCompanion.Timers;

/// <summary>
/// A timer the player keeps to hand: a name (a buff, a cooldown, a respawn), how long it runs, and optionally the key that starts it
/// (<see cref="Hotkey"/> text such as "Ctrl+Alt+1").
/// </summary>
public sealed record TimerPreset(string Name, int Seconds, string? Key = null);

/// <summary>
/// The corpse timer: with <see cref="On"/>, every monster corpse that is looted gets a timer called <see cref="Name"/> that runs for
/// <see cref="Seconds"/> from the loot (see <see cref="CorpseTimers"/>). <see cref="Group"/> also counts the corpses the rest of the group loots.
/// </summary>
public sealed class CorpseTimerSetting
{
    public const string DefaultName = "Corpse";
    public const int DefaultSeconds = 600;

    public bool On { get; set; }
    public string Name { get; set; } = DefaultName;
    public int Seconds { get; set; } = DefaultSeconds;
    public bool Group { get; set; }

    /// <summary>The same setting with a usable name and length (what a hand-edited or damaged file is brought to).</summary>
    public CorpseTimerSetting Cleaned() => new()
    {
        On = On, Group = Group,
        Name = TimerBook.CleanName(Name) ?? DefaultName,
        Seconds = Seconds is >= 1 and <= TimerClock.MaxSeconds ? Seconds : DefaultSeconds,
    };
}

/// <summary>
/// A timer that is running. It holds when it ends, not how long is left, so it carries on across closing the app and a restart.
/// <see cref="Seconds"/> is how long it was set for (the bar's full length).
/// </summary>
public sealed record RunningTimer(string Name, DateTime EndsUtc, int Seconds);

/// <summary>Where a running timer is: counting down, in its last half minute, in its last ten seconds, or ended.</summary>
public enum TimerPhase { Running, Warning, Ending, Done }

/// <summary>Reading, showing and judging the times of timers. Pure, so the window and the tests agree.</summary>
public static class TimerClock
{
    /// <summary>The warning comes this long before the end, and the last stretch (flashing) this long.</summary>
    public const int WarnSeconds = 30, EndingSeconds = 10;

    /// <summary>The longest a timer may run: a day.</summary>
    public const int MaxSeconds = 24 * 3600;

    /// <summary>An ended timer stays on show this long, then goes.</summary>
    public const int DoneShownSeconds = 15;

    public static TimerPhase Phase(double remainingSeconds) =>
        remainingSeconds <= 0 ? TimerPhase.Done
        : remainingSeconds <= EndingSeconds ? TimerPhase.Ending
        : remainingSeconds <= WarnSeconds ? TimerPhase.Warning
        : TimerPhase.Running;

    /// <summary>
    /// The countdown as it is read at a glance: 4.2 under ten seconds, 45 under a minute, 2:05 under an hour, 1:05:30 after that
    /// (the clock of a game-overlay aura, except that minutes keep their seconds).
    /// </summary>
    public static string Format(double seconds)
    {
        if (seconds <= 0) return "0.0";
        if (seconds < 10) return seconds.ToString("0.0", CultureInfo.InvariantCulture);
        int whole = (int)Math.Ceiling(seconds);
        if (whole < 60) return whole.ToString(CultureInfo.InvariantCulture);
        if (whole < 3600) return $"{whole / 60}:{whole % 60:00}";
        return $"{whole / 3600}:{whole % 3600 / 60:00}:{whole % 60:00}";
    }

    /// <summary>
    /// What a player types for a length: "90", "90s", "1:30" (minutes and seconds), "1:02:03", "5m", "2h", "1h30m", "1h 30m 10s".
    /// Null if it isn't one of those, or it's nothing or more than <see cref="MaxSeconds"/>.
    /// </summary>
    public static int? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().ToLowerInvariant();
        long total = 0;
        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length is 0 or > 5 || !p.All(char.IsAsciiDigit))) return null;
            foreach (var p in parts) total = total * 60 + long.Parse(p, CultureInfo.InvariantCulture);
        }
        else if (text.All(char.IsAsciiDigit))
        {
            if (text.Length > 6) return null;
            total = long.Parse(text, CultureInfo.InvariantCulture);
        }
        else
        {
            long number = -1;
            bool any = false;
            foreach (char c in text)
            {
                if (char.IsAsciiDigit(c))
                {
                    number = (number < 0 ? 0 : number) * 10 + (c - '0');
                    if (number > 999_999) return null;
                }
                else if (c is 'h' or 'm' or 's' && number >= 0)
                {
                    total += number * (c == 'h' ? 3600 : c == 'm' ? 60 : 1);
                    number = -1;
                    any = true;
                }
                else if (c != ' ') return null;
            }
            if (number >= 0 || !any) return null;   // digits left over with no unit after them, or no unit at all
        }
        return total is >= 1 and <= MaxSeconds ? (int)total : null;
    }

    /// <summary>A length as it is typed back: 90 → "1:30", 45 → "45s", 3600 → "1:00:00".</summary>
    public static string Spell(int seconds) =>
        seconds < 60 ? $"{seconds}s" : seconds < 3600 ? $"{seconds / 60}:{seconds % 60:00}" : $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}";
}

/// <summary>
/// One character's timers: the ones they keep to hand (<see cref="Presets"/>, started with a click or a key) and the ones running, and the
/// <see cref="Corpse"/> timer. Nothing here knows when a buff is cast: the game writes no cast to any file Companion reads, so the player
/// starts every timer except the corpse ones (those follow the ledger's loot, and are not kept here). Saved as
/// <c>timers.json</c> in the character's folder under the app's own; nothing leaves the PC.
/// </summary>
public sealed class TimerBook
{
    public const int MaxPresets = 100, MaxRunning = 50, MaxName = 40;
    public const string FileName = "timers.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public List<TimerPreset> Presets { get; set; } = [];
    public List<RunningTimer> Running { get; set; } = [];

    /// <summary>The timer every looted corpse gets, when the player wants one (off until they turn it on).</summary>
    public CorpseTimerSetting Corpse { get; set; } = new();

    /// <summary>A name as it is kept: trimmed, one line, at most <see cref="MaxName"/> characters; null if nothing is left.</summary>
    public static string? CleanName(string? name)
    {
        if (name is null) return null;
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length > MaxName) name = name[..MaxName].TrimEnd();
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Keeps (or replaces, by name) a timer to hand, with the key that starts it if one is given. False if the name, length or key isn't usable
    /// (see <see cref="KeyProblem"/>) or the list is full.
    /// </summary>
    public bool AddPreset(string? name, int seconds, string? key = null)
    {
        if (CleanName(name) is not { } clean || seconds is < 1 or > TimerClock.MaxSeconds) return false;
        int at = Presets.FindIndex(p => Same(p.Name, clean));
        if (at < 0 && Presets.Count >= MaxPresets) return false;
        if (KeyProblem(key, at) is not null) return false;
        var preset = new TimerPreset(clean, seconds, KeyText(key));
        if (at >= 0) Presets[at] = preset; else Presets.Add(preset);
        return true;
    }

    /// <summary>Why <paramref name="key"/> can't go on the timer at <paramref name="exceptIndex"/> (-1 for a new one): not a key, not allowed, or another timer has it. Null if it can, or there is none.</summary>
    public string? KeyProblem(string? key, int exceptIndex = -1)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (Hotkey.TryParse(key) is not { } parsed) return "That isn't a key. Press the keys you want, such as Ctrl+Alt+1.";
        if (Hotkey.Problem(parsed) is { } problem) return problem;
        var text = parsed.ToString();
        var other = Presets.Where((p, i) => i != exceptIndex && string.Equals(p.Key, text, StringComparison.Ordinal)).FirstOrDefault();
        return other is null ? null : $"{text} already starts “{other.Name}”.";
    }

    /// <summary>What is wrong with changing the timer at <paramref name="index"/> (-1 for a new one) to this (a sentence to show), or null if it can be done.</summary>
    public string? PresetProblem(int index, string? name, int seconds, string? key)
    {
        if (index < -1 || index >= Presets.Count) return "That timer is gone.";
        if (CleanName(name) is not { } clean) return "Type a name for the timer.";
        if (seconds is < 1 or > TimerClock.MaxSeconds) return "Type a length such as 90, 1:30, 5m or 1h30m (up to 24h).";
        if (Presets.Where((p, i) => i != index && Same(p.Name, clean)).FirstOrDefault() is { } same) return $"There is already a timer called “{same.Name}”.";
        return KeyProblem(key, index);
    }

    /// <summary>Changes the timer at <paramref name="index"/> (its name too). False, and nothing changed, if <see cref="PresetProblem"/> has something to say.</summary>
    public bool SetPreset(int index, string? name, int seconds, string? key)
    {
        if (PresetProblem(index, name, seconds, key) is not null) return false;
        Presets[index] = new TimerPreset(CleanName(name)!, seconds, KeyText(key));
        return true;
    }

    /// <summary>Adds a timer to the end of the list; the problems are those of <see cref="PresetProblem"/>, plus a full list. False if it couldn't.</summary>
    public bool AddNewPreset(string? name, int seconds, string? key, out string? problem)
    {
        problem = Presets.Count >= MaxPresets ? $"The list is full ({MaxPresets})." : PresetProblem(-1, name, seconds, key);
        if (problem is not null) return false;
        Presets.Add(new TimerPreset(CleanName(name)!, seconds, KeyText(key)));
        return true;
    }

    /// <summary>The key written the one way it is kept ("Ctrl+Alt+1"); null for none.</summary>
    private static string? KeyText(string? key) => Hotkey.TryParse(key)?.ToString();

    public bool RemovePreset(string name) => Presets.RemoveAll(p => Same(p.Name, name)) > 0;

    /// <summary>Starts a timer, or starts it over if one of that name is already running. Null if the name or length isn't usable.</summary>
    public RunningTimer? Start(string? name, int seconds, DateTime nowUtc)
    {
        if (CleanName(name) is not { } clean || seconds is < 1 or > TimerClock.MaxSeconds) return null;
        Running.RemoveAll(t => Same(t.Name, clean));
        if (Running.Count >= MaxRunning) return null;
        var timer = new RunningTimer(clean, nowUtc.AddSeconds(seconds), seconds);
        Running.Add(timer);
        return timer;
    }

    public bool Stop(string name) => Running.RemoveAll(t => Same(t.Name, name)) > 0;

    public static double Remaining(RunningTimer timer, DateTime nowUtc) => (timer.EndsUtc - nowUtc).TotalSeconds;

    /// <summary>The running timers, the one ending soonest first.</summary>
    public IReadOnlyList<RunningTimer> ByEnd() => Running.OrderBy(t => t.EndsUtc).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Takes away timers that ended more than <see cref="TimerClock.DoneShownSeconds"/> ago. True if any went.</summary>
    public bool Prune(DateTime nowUtc) => Running.RemoveAll(t => Remaining(t, nowUtc) < -TimerClock.DoneShownSeconds) > 0;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ the file

    /// <summary>The character's timers from <paramref name="folder"/>; an empty book if there is no file or it can't be read.</summary>
    public static TimerBook Load(string? folder, DateTime nowUtc)
    {
        var book = new TimerBook();
        if (folder is null) return book;
        TimerBook? read = null;
        try
        {
            var path = Path.Combine(folder, FileName);
            if (File.Exists(path)) read = JsonSerializer.Deserialize<TimerBook>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (read is null) return book;

        // Take each one through the same rules as a new one, so a hand-edited or damaged file can't hold anything the window can't show
        // A key that can't be used (or that another timer already has) is dropped, and the timer is kept without it
        foreach (var p in read.Presets ?? []) if (p is not null && !book.AddPreset(p.Name, p.Seconds, p.Key)) book.AddPreset(p.Name, p.Seconds);
        book.Corpse = (read.Corpse ?? new CorpseTimerSetting()).Cleaned();
        foreach (var t in (read.Running ?? []).Where(t => t is not null).OrderBy(t => t.EndsUtc))
        {
            if (CleanName(t.Name) is not { } clean || t.Seconds is < 1 or > TimerClock.MaxSeconds || book.Running.Count >= MaxRunning) continue;
            if (book.Running.Any(r => Same(r.Name, clean))) continue;
            book.Running.Add(new RunningTimer(clean, t.EndsUtc.ToUniversalTime(), t.Seconds));
        }
        book.Prune(nowUtc);
        return book;
    }

    /// <summary>Writes the book to <paramref name="folder"/> (made if need be). False if it couldn't be written; never throws.</summary>
    public bool Save(string? folder)
    {
        if (folder is null) return false;
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
