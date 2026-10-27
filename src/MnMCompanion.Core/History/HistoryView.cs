namespace MnMCompanion.History;

/// <summary>
/// What the History window is showing: the period and the filter text. The pages share one, so moving between them keeps both.
/// <see cref="Changed"/> fires only when something really changed.
/// </summary>
public sealed class HistoryView
{
    public HistoryView(DateTime? now = null) => Period = Period.Presets(now ?? DateTime.Now)[0];

    public Period Period { get; private set; }

    public string Filter { get; private set; } = "";

    public event Action? Changed;

    public void Set(Period period)
    {
        if (period == Period) return;
        Period = period;
        Changed?.Invoke();
    }

    public void SetFilter(string text)
    {
        text = text.Trim();
        if (text == Filter) return;
        Filter = text;
        Changed?.Invoke();
    }

    /// <summary>
    /// Whether the texts (the columns of one row) have every word of the filter between them, in any order and any case
    /// (always, when there is no filter): see <see cref="Loot.NameSearch.MatchesAny"/>.
    /// </summary>
    public bool Matches(params string[] fields) => Loot.NameSearch.MatchesAny(Filter, fields);
}
