namespace MnMCompanion.Loot;

/// <summary>
/// One item in the public price list, in copper: what a vendor pays for one (<see cref="Pays"/>, 0 when the list has none) and
/// the lowest an ordinary (not greedy, shady or shifty) vendor charges for one (<see cref="ChargeLow"/>, 0 when no vendor sells it).
/// </summary>
public readonly record struct PublicPrice(long Pays, long ChargeLow);

/// <summary>
/// The public price list as the app uses it: prices by item name, from a downloaded copy of the data set (see
/// <c>MnMCompanion.PriceList</c>; this type only holds what it read, so the core needs nothing to do that). Empty until a list
/// has been downloaded.
/// </summary>
public sealed class PublicPrices
{
    public static PublicPrices Empty { get; } = new([], null);

    private readonly Dictionary<string, PublicPrice> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When the list was made (UTC), as the data set says.</summary>
    public DateTime? Generated { get; }

    public PublicPrices(IEnumerable<(string Name, PublicPrice Price)> items, DateTime? generated)
    {
        Generated = generated;
        foreach (var (name, price) in items)
            if (name.Length > 0 && (price.Pays > 0 || price.ChargeLow > 0)) _items[name] = price;
    }

    public int Count => _items.Count;

    public IEnumerable<KeyValuePair<string, PublicPrice>> Items => _items;

    public bool TryGet(string name, out PublicPrice price) => _items.TryGetValue(name, out price);
}
