using Avalonia.Media;
using Avalonia.Media.Immutable;
using MnMCompanion.Recipes;

namespace MnMCompanion;

/// <summary>
/// The colours of the Recipes page. A recipe's <b>level</b> gets a colour by how far into its skill it is, green for the first levels through teal, blue and purple to
/// orange and pink for the last, so the page reads as a progression from top to bottom. A <b>skill</b> gets a colour of its own (the same every time), used for
/// the skill list, the picker and the group headings of a search across skills. All of them are light enough for the dark window, and are used as
/// a stripe, a tinted pill or text, never as a solid block.
/// </summary>
internal static class RecipeColors
{
    private static readonly Color[] TierColors =
    [
        Color.FromRgb(0x3D, 0xD6, 0x8C),   // up to 50: green
        Color.FromRgb(0x2D, 0xD4, 0xBF),   // 51-100: teal
        Color.FromRgb(0x5B, 0x9D, 0xFF),   // 101-150: blue
        Color.FromRgb(0xB3, 0x8B, 0xFA),   // 151-200: purple
        Color.FromRgb(0xF5, 0xA6, 0x23),   // 201-250: orange
        Color.FromRgb(0xF4, 0x72, 0xB6),   // 251 and up: pink
    ];

    private static readonly Dictionary<string, Color> Skills = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alchemy"] = Color.FromRgb(0xB3, 0x8B, 0xFA), ["Blacksmithing"] = Color.FromRgb(0xF5, 0xA6, 0x23), ["Carpentry"] = Color.FromRgb(0xB0, 0x89, 0x68),
        ["Cooking"] = Color.FromRgb(0xF0, 0x8A, 0x5D), ["Disenchanting"] = Color.FromRgb(0x94, 0xA3, 0xB8), ["Enchanting"] = Color.FromRgb(0x7C, 0x8C, 0xFF),
        ["Fletching"] = Color.FromRgb(0xA3, 0xE6, 0x35), ["Jewelcrafting"] = Color.FromRgb(0x2D, 0xD4, 0xBF), ["Leatherworking"] = Color.FromRgb(0xC7, 0x7D, 0x4F),
        ["Lumberjacking"] = Color.FromRgb(0x5F, 0xBF, 0x6B), ["Poison Making"] = Color.FromRgb(0x84, 0xCC, 0x16), ["Smelting"] = Color.FromRgb(0xEF, 0x6F, 0x4C),
        ["Stone Cutting"] = Color.FromRgb(0xA8, 0xB3, 0xC4), ["Survival"] = Color.FromRgb(0x4A, 0xDE, 0x80), ["Tailoring"] = Color.FromRgb(0xF4, 0x72, 0xB6),
        ["Tanning"] = Color.FromRgb(0xCA, 0x8A, 0x04), ["Tinkering"] = Color.FromRgb(0xFA, 0xCC, 0x15), ["Woodworking"] = Color.FromRgb(0xA9, 0x81, 0x5B),
    };

    /// <summary>The colour of a level (muted grey when the wiki has none).</summary>
    public static Color OfLevel(int? level) => RecipeLevels.Tier(level) is var tier and >= 0 ? TierColors[tier] : Palette.MutedColor;

    /// <summary>The colour of a skill: its own if it is one of the wiki's known ones, otherwise one picked from its name (the same one every time).</summary>
    public static Color OfSkill(string skill)
    {
        if (Skills.TryGetValue(skill, out var known)) return known;
        int hash = 17;
        foreach (var c in skill) hash = unchecked(hash * 31 + char.ToLowerInvariant(c));
        return TierColors[(hash & 0x7FFFFFFF) % TierColors.Length];
    }

    private static readonly Dictionary<(Color, byte), IBrush> Cache = [];

    /// <summary>A frozen brush of this colour, faded to <paramref name="alpha"/> (255 is solid). Brushes are made once and shared.</summary>
    public static IBrush Solid(Color color, byte alpha = 255)
    {
        if (Cache.TryGetValue((color, alpha), out var brush)) return brush;
        return Cache[(color, alpha)] = new ImmutableSolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    /// <summary>Labels in the lists ("Made at", "Kind", "Slot"): a cool grey-blue, so they read as labels and not as values.</summary>
    public static readonly IBrush Label = Solid(Color.FromRgb(0x8F, 0xA6, 0xC4));

    /// <summary>An ingredient's name: lighter than the muted text it sits in, so what a recipe takes stands out from its punctuation.</summary>
    public static readonly IBrush Ingredient = Solid(Color.FromRgb(0xC3, 0xCC, 0xD9));

    /// <summary>The marker behind the words a search found.</summary>
    public static readonly IBrush Found = Solid(Palette.AccentColor, 0x58);
}
