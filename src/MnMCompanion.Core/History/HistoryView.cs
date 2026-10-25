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

    /// <summary>Whether any of the texts contains the filter, ignoring case (always, when there is no filter).</summary>
    public bool Matches(params string[] fields) =>
        Filter.Length == 0 || fields.Any(f => f.Contains(Filter, StringComparison.OrdinalIgnoreCase));
}
