namespace MnMCompanion.Loot;

/// <summary>What a search found on one day: the matching items taken, how many kinds, by how many looters, and what a vendor pays for them.</summary>
public sealed record DayTaken(DateOnly Day, int Taken, int Kinds, int Looters, long Worth, bool Estimated, bool Unpriced);

/// <summary>
/// The Loot page's search across every day: the item lines whose looter, item or monster has every word typed (in any order and any
/// case, <see cref="NameSearch"/>) from all the days together, worked out like a day (<see cref="Answer"/>) so the same totals,
/// looters, items and monsters come out of it, plus a line per day (<see cref="ByDay"/>). Purses are left out: a search is about items.
/// </summary>
public static class LootSearch
{
    /// <summary>The day an answer is filed under: it is not a real day, it holds the matching lines of all of them.</summary>
    public static readonly DateOnly AnswerDay = DateOnly.MinValue;

    public static bool IsAnswer(LootDay day) => day.Day == AnswerDay;

    /// <summary>The item lines (oldest first) that match; none when nothing is typed.</summary>
    public static IReadOnlyList<LootEvent> Matching(IEnumerable<LootEvent> events, string? query)
    {
        var words = NameSearch.Words(query);
        if (words.Length == 0) return [];
        return events.Where(e => e.Kind == LootKind.Item && words.All(w => Has(e, w))).OrderBy(e => e.Time).ToList();
    }

    private static bool Has(LootEvent e, string word) =>
        e.Item.Contains(word, StringComparison.OrdinalIgnoreCase) || e.Who.Contains(word, StringComparison.OrdinalIgnoreCase) ||
        e.Source.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>The matching lines of every day, worked out as one made-up day; null when nothing matches.</summary>
    public static LootDay? Answer(IEnumerable<LootEvent> events, string? query, ItemValues? values = null)
    {
        var hits = Matching(events, query);
        return hits.Count == 0 ? null : LootDay.Of(AnswerDay, hits, values);
    }

    /// <summary>How many days the lines are from.</summary>
    public static int Days(IEnumerable<LootEvent> events) => events.Select(e => e.Day).Distinct().Count();

    /// <summary>Per day, newest first: what was taken that day.</summary>
    public static IReadOnlyList<DayTaken> ByDay(IEnumerable<LootEvent> hits, ItemValues? values = null)
    {
        values ??= ItemValues.Empty;
        return hits.Where(e => e.Kind == LootKind.Item).GroupBy(e => e.Day).Select(g =>
        {
            long worth = 0;
            bool estimated = false, unpriced = false;
            foreach (var e in g)
            {
                var v = values.Of(e.Item);
                if (v.Known) { worth += v.Each * e.Count; estimated |= v.Estimated; }
                else unpriced = true;
            }
            return new DayTaken(g.Key, g.Sum(e => e.Count), g.Select(e => e.Item).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                g.Select(e => e.Who).Distinct(StringComparer.OrdinalIgnoreCase).Count(), worth, estimated, unpriced);
        }).OrderByDescending(d => d.Day).ToList();
    }

    /// <summary>An answer as text for the clipboard: what was searched for, the totals, then a line per item (<see cref="LootSummary.ItemLine"/>).</summary>
    public static string Text(string query, LootDay answer)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"LOOT SEARCH  “{query}”  ·  {answer.First:d MMM yyyy} to {answer.Last:d MMM yyyy}");
        sb.AppendLine($"{answer.Totals.Items:N0} taken  ·  {answer.Items.Count:N0} kinds  ·  {Days(answer.Events):N0} days  ·  {answer.Members.Count:N0} looters");
        foreach (var i in answer.ItemsWorth) sb.AppendLine(LootSummary.ItemLine(i));
        return sb.ToString();
    }
}
