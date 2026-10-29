using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MnMCompanion.Recipes;

/// <summary>One thing a recipe uses: a quantity of something, or (<see cref="Or"/>) of something else that does as well.</summary>
public sealed record Ingredient(string Name, int Qty, string? Or = null)
{
    /// <summary>"6 Copper Bar", or "1 Iron Bar or Steel Bar".</summary>
    public string Show() => $"{Qty} {Name}" + (Or is { Length: > 0 } alternative ? " or " + alternative : "");
}

/// <summary>A fact about a recipe that is not an ingredient: where it is made, what slot it fits, what it yields. Label and text, ready to show.</summary>
public sealed record RecipeNote(string Label, string Text);

/// <summary>
/// One recipe of the community wiki's tradeskill tables. <see cref="Level"/> is the wiki's "trivial" number: the tradeskill level at which the recipe
/// stops being worth making for skill. Null when the wiki doesn't know it yet (a "?" or a "-"); <see cref="LevelText"/> is what the wiki wrote ("5+").
/// </summary>
public sealed record Recipe(string Skill, string Group, string Result, int ResultQty, int? Level, string LevelText, IReadOnlyList<Ingredient> Ingredients, IReadOnlyList<RecipeNote> Notes)
{
    /// <summary>"Copper Chain Links" or "Fish Stock ×4".</summary>
    public string Title => ResultQty > 1 ? $"{Result} ×{ResultQty}" : Result;

    /// <summary>The ingredients in one line: "6 Copper Bar, 1 Smithing Pliers"; empty when the wiki lists none.</summary>
    public string IngredientLine => string.Join(", ", Ingredients.Select(i => i.Show()));

    /// <summary>The recipe as plain text for the clipboard.</summary>
    public string Text()
    {
        var sb = new StringBuilder();
        sb.Append(Title).Append(" (").Append(Skill);
        if (LevelText.Length > 0 && LevelText is not ("?" or "-")) sb.Append(' ').Append(LevelText);
        sb.Append(')');
        sb.Append(Ingredients.Count > 0 ? ": " + IngredientLine : ": ingredients not listed on the wiki yet");
        foreach (var note in Notes) sb.Append("; ").Append(note.Label).Append(' ').Append(note.Text);
        return sb.ToString();
    }
}

/// <summary>A tradeskill that has recipes: where it is done, with what, who teaches it, and what levels its recipes span.</summary>
public sealed record Skill(string Name, string Station, string Tools, string Trainers, int Count, int? LowLevel, int? HighLevel)
{
    /// <summary>"5–300", "20", or "" when no recipe has a level.</summary>
    public string LevelRange => LowLevel is not { } lo || HighLevel is not { } hi ? "" : lo == hi ? lo.ToString() : $"{lo}–{hi}";
}

/// <summary>
/// Every recipe of the community wiki's public tradeskills data (<c>/api/tradeskills</c>), read from the JSON the wiki's own pages load. Skills the wiki lists
/// without recipes (Mining, Riding, ...) are left out. Reading never throws: a document that isn't that data gives null.
/// </summary>
public sealed class RecipeBook
{
    public IReadOnlyList<Skill> Skills { get; }
    public IReadOnlyList<Recipe> Recipes { get; }

    private readonly Dictionary<string, List<Recipe>> _bySkill;

    private RecipeBook(List<Skill> skills, List<Recipe> recipes)
    {
        Skills = skills;
        Recipes = recipes;
        _bySkill = recipes.GroupBy(r => r.Skill, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The recipes of one skill (empty for a name that isn't one).</summary>
    public IReadOnlyList<Recipe> Of(string skill) => _bySkill.TryGetValue(skill, out var list) ? list : [];

    public Skill? SkillNamed(string name) => Skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>How many recipes have no ingredient list on the wiki yet.</summary>
    public int WithoutIngredients => Recipes.Count(r => r.Ingredients.Count == 0);

    // ------------------------------------------------------------------ reading

    private static readonly Regex Leading = new(@"^\s*(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The columns of a wiki row that are one stat each (resistances and attributes of a piece of jewelry): shown together as one "Stats" line, the zeros left out.</summary>
    private static readonly HashSet<string> StatKeys = new(StringComparer.OrdinalIgnoreCase)
        { "AC", "STR", "STA", "AGI", "DEX", "INT", "WIS", "CHA", "HP", "MP", "CR", "CorR", "DR", "ER", "FR", "MR", "PR" };

    public static RecipeBook? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("tradeskills", out var skills) || skills.ValueKind != JsonValueKind.Array) return null;
            var allRecipes = new List<Recipe>();
            var allSkills = new List<Skill>();
            foreach (var skill in skills.EnumerateArray())
            {
                if (skill.ValueKind != JsonValueKind.Object) continue;
                var name = Clean(Text(skill, "name"));
                if (name.Length == 0 || !skill.TryGetProperty("recipeGroups", out var groups) || groups.ValueKind != JsonValueKind.Array) continue;
                var mine = new List<Recipe>();
                foreach (var group in groups.EnumerateArray())
                {
                    if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty("recipes", out var recipes) || recipes.ValueKind != JsonValueKind.Array) continue;
                    var groupName = Clean(Text(group, "name"));
                    foreach (var recipe in recipes.EnumerateArray())
                        if (ReadRecipe(name, groupName, recipe) is { } r) mine.Add(r);
                }
                if (mine.Count == 0) continue;
                var levels = mine.Where(r => r.Level.HasValue).Select(r => r.Level!.Value).ToList();
                allSkills.Add(new Skill(name, Clean(Text(skill, "station")), Clean(Text(skill, "tools")), Clean(Text(skill, "trainer")), mine.Count,
                    levels.Count > 0 ? levels.Min() : null, levels.Count > 0 ? levels.Max() : null));
                allRecipes.AddRange(mine);
            }
            return allRecipes.Count == 0 ? null : new RecipeBook(allSkills.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList(), allRecipes);
        }
        catch (JsonException) { return null; }
    }

    private static Recipe? ReadRecipe(string skill, string group, JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var result = Clean(Text(e, "result"));
        if (result.Length == 0) return null;
        var levelText = Clean(Text(e, "trivial"));
        int? level = Leading.Match(levelText) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
        int qty = int.TryParse(Text(e, "result_qty"), out var q) && q > 0 ? q : 1;

        var ingredients = new List<Ingredient>();
        if (e.TryGetProperty("ingredients", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var name = Clean(Text(item, "name"));
                if (name.Length == 0) continue;
                var alternative = Clean(Text(item, "or"));
                ingredients.Add(new Ingredient(name, int.TryParse(Text(item, "qty"), out var iq) && iq > 0 ? iq : 1, alternative.Length > 0 ? alternative : null));
            }

        return new Recipe(skill, group, result, qty, level, levelText, ingredients, ReadNotes(e));
    }

    private static List<RecipeNote> ReadNotes(JsonElement e)
    {
        var notes = new List<RecipeNote>();
        if (!e.TryGetProperty("extra", out var extra) || extra.ValueKind != JsonValueKind.Object) return notes;
        var stats = new List<string>();
        var statText = "";
        foreach (var property in extra.EnumerateObject())
        {
            var value = Clean(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString());
            if (IsBlank(value)) continue;
            if (StatKeys.Contains(property.Name))
            {
                if (value != "0") stats.Add($"{property.Name} {value}");
            }
            else if (property.Name.Equals("Stats", StringComparison.OrdinalIgnoreCase)) statText = value;
            else notes.Add(new RecipeNote(Label(property.Name), value));
        }
        var statLine = string.Join(" · ", stats.Prepend(statText).Where(s => s.Length > 0));
        if (statLine.Length > 0) notes.Add(new RecipeNote("Stats", statLine));
        return notes;
    }

    private static string Label(string key) => key switch
    {
        "Notes / Yield" or "Source / Notes" => "Notes",
        _ => key,
    };

    private static bool IsBlank(string value) => value.Length == 0 || value is "-" or "?" or "??" or "—";

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        } : "";

    /// <summary>Blanks collapsed, and the replacement character the wiki's export leaves where it meant a dash shown as one.</summary>
    private static string Clean(string? text) => Regex.Replace((text ?? "").Replace('�', '–'), @"\s+", " ").Trim();
}
