using System.Text.RegularExpressions;

namespace MnMCompanion.Update;

/// <summary>
/// Where a zone is in the community atlas (MnM Atlas, by ocombe): the address of its page, for the player's own browser to open when they ask.
/// Nothing here fetches anything and the app never contacts the atlas itself.
/// </summary>
public static class Atlas
{
    public const string Origin = "https://www.mnmatlas.com";

    /// <summary>The ledger's name for a zone (lower case, letters only) to the atlas's name for its map, as listed in the atlas's own map list.</summary>
    private static readonly Dictionary<string, string> Maps = new()
    {
        ["ailvorith"] = "ail-vorith", ["blindmidden"] = "blind-midden", ["faelindral"] = "faelindral", ["sungreetstrand"] = "sungreet-strand",
        ["shadeddunes"] = "shaded-dunes", ["fallenpass"] = "fallen-pass", ["wyrmsbanetomb"] = "wyrmsbane-tomb", ["glassflats"] = "glass-flats",
        ["ancientcrypt"] = "ancient-crypt", ["infestedcrypt"] = "infested-crypt", ["blacktidebay"] = "blacktide-bay", ["scarwood"] = "scarwood",
        ["valeofzintar"] = "vale-of-zintar", ["graincellar"] = "grain-cellar", ["greatcavernsea"] = "great-cavern-sea", ["cavesofirem"] = "caves-of-irem",
        ["telekir"] = "tel-ekir", ["keepersbight"] = "keepers-bight", ["fallenwatch"] = "fallen-watch", ["shallowshoals"] = "shallow-shoals",
        ["glintinghollow"] = "glinting-hollow", ["sunkenresort"] = "sunken-resort", ["rothold"] = "rothold", ["grimtidesanctum"] = "grimtide-sanctum",
        ["underdocks"] = "underdocks", ["evershadeweald"] = "evershade-weald",
        // Night Harbor is two zones in the ledger and one map in the atlas
        ["nightharbor"] = "night-harbor", ["nightharborw"] = "night-harbor", ["nightharbore"] = "night-harbor",
    };

    /// <summary>The atlas page for a zone, or null when the atlas has no map of it.</summary>
    public static string? UrlFor(string zone) =>
        Maps.TryGetValue(Regex.Replace(zone.ToLowerInvariant(), "[^a-z]", ""), out var id) ? $"{Origin}/{id}/" : null;

    /// <summary>The same map as the atlas offers it for embedding: the one map with its levels and markers, without the map picker, search or editing tools.</summary>
    public static string? EmbedUrlFor(string zone) => UrlFor(zone) is { } url ? url + "?embed=map" : null;
}
