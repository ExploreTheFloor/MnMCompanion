using System.Text.RegularExpressions;
using MnMCompanion.Loot;

namespace MnMCompanion.Recipes;

/// <summary>
/// The search box over the recipes. Every word typed has to be somewhere in the recipe (what it makes, an ingredient, its skill, its kind, a note), in any
/// case and any order, and a part of a word is enough ("copp bar" finds Copper Bar). A word like <c>50-75</c> is a range of levels instead, and a
/// plain number also finds the recipes of exactly that level: "iron 60-90" is the iron recipes for levels 60 to 90.
/// </summary>
public static class RecipeSearch
{
    private static readonly Regex Range = new(@"^(\d{1,3})\s*-\s*(\d{1,3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool Matches(Recipe recipe, string? query)
    {
        var words = NameSearch.Words(query);
        if (words.Length == 0) return true;
        string[]? fields = null;
        foreach (var word in words)
        {
            if (Range.Match(word) is { Success: true } m)
            {
                int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
                if (a > b) (a, b) = (b, a);
                if (recipe.Level is not { } level || level < a || level > b) return false;
                continue;
            }
            if (int.TryParse(word, out var exact) && recipe.Level == exact) continue;
            fields ??= Fields(recipe);
            if (!fields.Any(f => f.Contains(word, StringComparison.OrdinalIgnoreCase))) return false;
        }
        return true;
    }

    public static IReadOnlyList<Recipe> Filter(IEnumerable<Recipe> recipes, string? query) =>
        NameSearch.Words(query).Length == 0 ? recipes.ToList() : recipes.Where(r => Matches(r, query)).ToList();

    private static string[] Fields(Recipe r)
    {
        var fields = new List<string> { r.Result, r.Skill, r.Group };
        foreach (var i in r.Ingredients)
        {
            fields.Add(i.Name);
            if (i.Or is { Length: > 0 } alternative) fields.Add(alternative);
        }
        foreach (var n in r.Notes) fields.Add(n.Text);
        return [.. fields];
    }
}
