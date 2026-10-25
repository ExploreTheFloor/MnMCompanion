using System.Text.RegularExpressions;

namespace MnMCompanion.Loot;

/// <summary>A zone as the ledger last named it, and when.</summary>
public sealed record ZoneSighting(string Zone, DateTime At);

/// <summary>
/// How a zone reads to a person. The ledger names the zone on every row in the game's own lower-case form ("fallenpass", "nightharborw").
/// </summary>
public static class ZoneMaps
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fallenpass"] = "Fallen Pass", ["valeofzintar"] = "Vale of Zintar", ["wyrmsbanetomb"] = "Tomb of the Last Wyrmsbane",
        ["shadeddunes"] = "Shaded Dunes", ["sungreetstrand"] = "Sungreet Strand", ["glassflats"] = "Glass Flats", ["ancientcrypt"] = "Ancient Crypt",
        ["telekir"] = "Tel Ekir", ["underdocks"] = "Underdocks", ["keepersbight"] = "Keeper's Bight", ["ailvorith"] = "Ail'Vorith",
        ["blindmidden"] = "Blind Midden", ["blacktidebay"] = "Blacktide Bay", ["graincellar"] = "Grain Cellar", ["grimtidesanctum"] = "Grimtide Sanctum",
        ["glintinghollow"] = "Glinting Hollow", ["greatcavernsea"] = "Great Cavern Sea", ["fallenwatch"] = "Fallen Watch", ["shallowshoals"] = "Shallow Shoals",
        ["sunkenresort"] = "Sunken Resort", ["faelindral"] = "Faelindral", ["rothold"] = "Rothold", ["scarwood"] = "Scarwood",
        ["nightharbor"] = "Night Harbor", ["nightharborw"] = "Night Harbor (west)", ["nightharbore"] = "Night Harbor (east)",
    };

    /// <summary>The zone as a person reads it: a known name, or the game's own spelling with a capital.</summary>
    public static string DisplayName(string zone)
    {
        var key = Normalize(zone);
        if (key.Length == 0) return "";
        return Names.TryGetValue(key, out var name) ? name : char.ToUpperInvariant(key[0]) + key[1..];
    }

    public static string Normalize(string zone) => Regex.Replace(zone.ToLowerInvariant(), "[^a-z0-9]", "");
}
