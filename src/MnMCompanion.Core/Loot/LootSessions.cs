using System.Globalization;
using System.Text.Json;

namespace MnMCompanion.Loot;

/// <summary>
/// A stretch of looting between two markers: from <see cref="Start"/> up to <see cref="End"/> (null = still going). The first
/// session starts at <see cref="DateTime.MinValue"/> (everything before any marker was made).
/// </summary>
public sealed record LootSession(DateTime Start, DateTime? End, string Reason)
{
    public bool Open => End is null;

    public bool Contains(DateTime time) => time >= Start && (End is null || time < End);

    /// <summary>
    /// "9:41 AM to 11:20 AM", "9:41 AM to now". The first session has no start of its own, so it uses
    /// <paramref name="firstEvent"/> (the first loot it holds) when given.
    /// </summary>
    public string Label(DateTime? firstEvent = null)
    {
        string T(DateTime t) => t.ToString("t", CultureInfo.CurrentCulture);
        var from = Start == DateTime.MinValue ? firstEvent : Start;
        if (from is null) return End is null ? "this session" : $"until {T(End.Value)}";
        return End is null ? $"{T(from.Value)} to now" : $"{T(from.Value)} to {T(End.Value)}";
    }
}

/// <summary>
/// The markers that cut the loot into sessions: "archive today's stats and start again". A marker deletes nothing; the
/// ledger keeps every event, and a session is just the events between two markers. Kept in <c>sessions.json</c> in this
/// app's own folder (one per character), so it survives a restart.
/// </summary>
public sealed class LootSessions
{
    /// <summary>Markers closer together than this are one (a double click).</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(5);

    private readonly string _path;
    private readonly List<Mark> _marks;
    private readonly object _gate = new();

    public sealed record Mark(DateTime At, string Reason);

    public LootSessions(string folder)
    {
        _path = Path.Combine(folder, "sessions.json");
        _marks = Load(_path);
    }

    public IReadOnlyList<Mark> Marks
    {
        get { lock (_gate) return _marks.ToList(); }
    }

    /// <summary>Every session, oldest first: one more than there are markers.</summary>
    public IReadOnlyList<LootSession> All
    {
        get
        {
            lock (_gate)
            {
                var list = new List<LootSession>();
                var start = DateTime.MinValue;
                var reason = "";
                foreach (var m in _marks)
                {
                    list.Add(new LootSession(start, m.At, reason));
                    (start, reason) = (m.At, m.Reason);
                }
                list.Add(new LootSession(start, null, reason));
                return list;
            }
        }
    }

    /// <summary>The session in progress.</summary>
    public LootSession Current => All[^1];

    /// <summary>
    /// Starts a new session at <paramref name="at"/>, closing the one in progress. Returns the session that was closed, or
    /// null when the marker was too close to the last one (<see cref="MinGap"/>).
    /// </summary>
    public LootSession? Start(DateTime at, string reason = "manual")
    {
        lock (_gate)
        {
            if (_marks.Count > 0 && at - _marks[^1].At < MinGap) return null;
            var closing = All[^1] with { End = at };
            _marks.Add(new Mark(at, reason));
            Save();
            return closing;
        }
    }

    /// <summary>Takes back the last marker: the last two sessions are one again. False when there is none.</summary>
    public bool RemoveLast()
    {
        lock (_gate)
        {
            if (_marks.Count == 0) return false;
            _marks.RemoveAt(_marks.Count - 1);
            Save();
            return true;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_marks.Select(m => new Row(m.At.ToString("O", CultureInfo.InvariantCulture), m.Reason)).ToList()));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record Row(string At, string Reason);

    private static List<Mark> Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(path)) is { } rows)
                return rows.Where(r => DateTime.TryParse(r.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
                    .Select(r => new Mark(DateTime.Parse(r.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), r.Reason ?? "")).OrderBy(m => m.At).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return [];
    }
}
