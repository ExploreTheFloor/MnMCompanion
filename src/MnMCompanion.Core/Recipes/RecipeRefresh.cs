using System.Text.Json;

namespace MnMCompanion.Recipes;

/// <summary>
/// What Companion remembers about asking the wiki, kept beside the list (<c>recipes.meta.json</c>) so the rules below hold across restarts: the list's
/// version tag (so the next question can be "has it changed?", which the wiki answers with a few bytes when it hasn't), when the kept list was last
/// confirmed current, when the wiki was last asked at all, and the time before which it must not be asked again (it said to wait).
/// </summary>
public sealed class RecipeMeta
{
    public string? ETag { get; set; }
    public DateTime? CheckedUtc { get; set; }
    public DateTime? TriedUtc { get; set; }
    public DateTime? NotBeforeUtc { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PathFor(string? listPath = null) =>
        Path.ChangeExtension(listPath ?? RecipeCache.DefaultPath, ".meta.json");

    /// <summary>What was remembered; an empty record when there is nothing or it is damaged (the worst that costs is one more question to the wiki).</summary>
    public static RecipeMeta Load(string? listPath = null)
    {
        try
        {
            var path = PathFor(listPath);
            if (File.Exists(path) && JsonSerializer.Deserialize<RecipeMeta>(File.ReadAllText(path)) is { } meta) return meta;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new RecipeMeta();
    }

    public bool Save(string? listPath = null)
    {
        try
        {
            var path = PathFor(listPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The wiki said to wait (or is answering with errors): do not ask again before <paramref name="wait"/> has passed, at most a day.</summary>
    public void BackOff(TimeSpan? wait, DateTime nowUtc) =>
        NotBeforeUtc = nowUtc + Clamp(wait ?? RecipeRefresh.DefaultBackoff, TimeSpan.FromMinutes(1), RecipeRefresh.MaxBackoff);

    private static TimeSpan Clamp(TimeSpan value, TimeSpan low, TimeSpan high) => value < low ? low : value > high ? high : value;
}

public enum RefreshStep
{
    /// <summary>The kept list was confirmed current within the day: nothing to ask.</summary>
    Fresh,
    /// <summary>Not yet: the wiki was asked a moment ago, or told us to wait.</summary>
    TooSoon,
    /// <summary>Ask the wiki now.</summary>
    Ask,
}

public sealed record RefreshPlan(RefreshStep Step, string Why = "");

/// <summary>
/// When the wiki may be asked for its recipe list. It is a community wiki, so the rules are gentle: by itself Companion asks at most once a day (when the
/// Recipes page is opened and the kept list was last confirmed more than a day ago), never twice within an hour even when an answer failed, never before
/// a time the wiki asked us to wait until, and the refresh button no more than once in ten minutes. The question carries the list's version tag, so
/// when nothing changed the answer is empty. All of it is remembered across restarts (<see cref="RecipeMeta"/>).
/// </summary>
public static class RecipeRefresh
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromDays(1);
    public static readonly TimeSpan BetweenTries = TimeSpan.FromHours(1);
    public static readonly TimeSpan BetweenManual = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultBackoff = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromDays(1);

    /// <param name="haveList">A list is kept on this PC.</param>
    /// <param name="listTimeUtc">When the kept list was written (used when nothing else says when it was confirmed).</param>
    /// <param name="manual">The player pressed the refresh button (or Try again).</param>
    public static RefreshPlan Decide(DateTime nowUtc, bool haveList, RecipeMeta meta, DateTime? listTimeUtc, bool manual)
    {
        if (meta.NotBeforeUtc is { } notBefore && nowUtc < notBefore)
            return new RefreshPlan(RefreshStep.TooSoon, $"The wiki asked to be left alone for a while: it won't be asked again for about {Minutes(notBefore - nowUtc)}.");
        if (manual)
        {
            if (meta.TriedUtc is { } last && nowUtc - last < BetweenManual)
                return new RefreshPlan(RefreshStep.TooSoon, $"The wiki was asked {Minutes(nowUtc - last)} ago: it is only asked again after {BetweenManual.TotalMinutes:0} minutes.");
            return new RefreshPlan(RefreshStep.Ask);
        }
        var confirmed = meta.CheckedUtc ?? listTimeUtc;
        if (haveList && confirmed is { } at && nowUtc - at <= FreshFor) return new RefreshPlan(RefreshStep.Fresh);
        if (meta.TriedUtc is { } tried && nowUtc - tried < BetweenTries)
            return new RefreshPlan(RefreshStep.TooSoon, "The wiki was asked a short while ago.");
        return new RefreshPlan(RefreshStep.Ask);
    }

    private static string Minutes(TimeSpan span)
    {
        int m = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes));
        return m == 1 ? "1 minute" : m < 120 ? $"{m} minutes" : $"{(int)Math.Ceiling(span.TotalHours)} hours";
    }
}
