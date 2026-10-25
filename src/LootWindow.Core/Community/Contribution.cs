using System.Text.Json;
using System.Text.Json.Serialization;
using LootWindow.History;
using LootWindow.Loot;

namespace LootWindow.Community;

/// <summary>An item off a monster: in how many of the corpses looted it was taken, and how many units.</summary>
public sealed record ContributionItem(string Item, int Corpses, int Units);

/// <summary>One kind of monster in one zone: corpses looted, how many had a purse and its coin, and what came off them.</summary>
public sealed record ContributionDrop(string Mob, string Zone, int Corpses, int Purses, long Copper, IReadOnlyList<ContributionItem> Items);

/// <summary>
/// What vendors paid (<see cref="Sold"/>) and charged (<see cref="Bought"/>) for one item in one zone, as a tally: the price for one,
/// in copper, mapped to how many stacks went at that price. Vendors' names aren't in the ledger, so the zone is the nearest we get.
/// </summary>
public sealed record ContributionPrice(string Item, string Zone, IReadOnlyDictionary<string, int> Sold, IReadOnlyDictionary<string, int> Bought);

/// <summary>What was gathered of one item in one zone.</summary>
public sealed record ContributionHarvest(string Item, string Zone, int Harvests, int Units);

/// <summary>An item's name with the 24-character id the game gives it.</summary>
public sealed record ContributionName(string Name, string Id);

/// <summary>Headline counts of a contribution, for the preview.</summary>
public sealed record ContributionTotals(int Monsters, int Corpses, int Sales, int Purchases, int Harvests, int Items);

/// <summary>
/// What a player shares with the community data: aggregates of their OWN loot, sales and gathering, and nothing else. It holds no
/// character or player name, no individual rows, no trade partners and no times beyond the day it was made. <see cref="Contributor"/>
/// is a random id made on this PC (not derived from anything about the player), so sending the file again replaces what they sent
/// before instead of adding to it. See SCHEMA.md in the data repository.
/// </summary>
public sealed record Contribution(
    int Schema, string Contributor, string App, string Generated, string License, ContributionTotals Totals,
    IReadOnlyList<ContributionDrop> Drops, IReadOnlyList<ContributionPrice> Prices, IReadOnlyList<ContributionHarvest> Harvest, IReadOnlyList<ContributionName> Items)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static Contribution? FromJson(string json) => JsonSerializer.Deserialize<Contribution>(json, Options);
}

public static class ContributionBuilder
{
    /// <summary>The version of the file layout; the data repository refuses layouts it doesn't know.</summary>
    public const int Schema = 1;

    /// <summary>The licence a contributor gives their file under, stated in the file itself.</summary>
    public const string License = "CC-BY-NC-SA-4.0";

    private const int MaxName = 80;

    /// <summary>
    /// Builds a contribution from one player's own rows: the loot they took (items and purses, with the corpse rule of
    /// <see cref="LootSummary.Corpses"/>), what they sold and bought, what they gathered, and the item names with their ids.
    /// Only the player's OWN rows belong here (their Character files), because the same loot is written into every party member's
    /// social file and would otherwise be counted once per member.
    /// </summary>
    public static Contribution Build(IEnumerable<LootEvent> ownLoot, IEnumerable<VendorRow> vendor, IEnumerable<HarvestRow> harvest,
        IEnumerable<ContributionName> names, string contributor, string app, DateOnly generated)
    {
        var drops = DropRates.ByMob(ownLoot)
            .Where(m => Ok(m.Mob))
            .Select(m => new ContributionDrop(Clean(m.Mob), Clean(m.Zone), m.Corpses, m.WithPurse, m.Copper,
                m.Drops.Where(d => Ok(d.Item)).Select(d => new ContributionItem(Clean(d.Item), d.Corpses, d.Units)).ToList()))
            .OrderByDescending(d => d.Corpses).ThenBy(d => d.Mob, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Zone, StringComparer.OrdinalIgnoreCase).ToList();

        var rows = vendor.Where(v => v.TotalCopper > 0 && Ok(v.Item)).ToList();
        var prices = rows.GroupBy(v => (Clean(v.Item).ToLowerInvariant(), Clean(v.Zone).ToLowerInvariant()))
            .Select(g => new ContributionPrice(Clean(g.First().Item), Clean(g.First().Zone), Tally(g, VendorDirection.Sold), Tally(g, VendorDirection.Bought)))
            .OrderBy(p => p.Item, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Zone, StringComparer.OrdinalIgnoreCase).ToList();

        var gathered = harvest.Where(h => Ok(h.Item)).GroupBy(h => (Clean(h.Item).ToLowerInvariant(), Clean(h.Zone).ToLowerInvariant()))
            .Select(g => new ContributionHarvest(Clean(g.First().Item), Clean(g.First().Zone), g.Count(), g.Sum(h => h.Qty)))
            .OrderByDescending(h => h.Units).ThenBy(h => h.Item, StringComparer.OrdinalIgnoreCase).ToList();

        var catalogue = names.Where(n => Ok(n.Name) && IsId(n.Id)).Select(n => new ContributionName(Clean(n.Name), n.Id.ToLowerInvariant()))
            .DistinctBy(n => (n.Name.ToLowerInvariant(), n.Id)).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();

        var totals = new ContributionTotals(drops.Count, drops.Sum(d => d.Corpses), rows.Count(v => v.Direction == VendorDirection.Sold),
            rows.Count(v => v.Direction == VendorDirection.Bought), gathered.Sum(h => h.Harvests), catalogue.Count);
        return new Contribution(Schema, contributor, app, generated.ToString("yyyy-MM-dd"), License, totals, drops, prices, gathered, catalogue);
    }

    private static Dictionary<string, int> Tally(IEnumerable<VendorRow> rows, VendorDirection direction) =>
        rows.Where(r => r.Direction == direction).GroupBy(r => r.Each).OrderBy(g => g.Key)
            .ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Count());

    private static bool Ok(string text) => text.Trim().Length > 0;

    private static string Clean(string text)
    {
        text = text.Trim();
        return text.Length <= MaxName ? text : text[..MaxName];
    }

    private static bool IsId(string id) => id.Length == 24 && id.All(char.IsAsciiHexDigit);

    /// <summary>The preview a player reads before saving: what is in the file, in a few lines.</summary>
    public static IReadOnlyList<string> Describe(Contribution c)
    {
        var t = c.Totals;
        return
        [
            $"{t.Monsters:N0} kinds of monster, from {t.Corpses:N0} looted corpses: what came off each, how often, and the coin in their purses",
            $"{c.Prices.Count:N0} item prices by zone, from {t.Sales:N0} sales and {t.Purchases:N0} purchases",
            $"{c.Harvest.Count:N0} things gathered by zone, from {t.Harvests:N0} harvests",
            $"{t.Items:N0} item names with their game ids",
        ];
    }
}
