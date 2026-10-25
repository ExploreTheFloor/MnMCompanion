namespace MnMCompanion.Loot;

/// <summary>
/// One item off one kind of monster: in how many of its looted corpses it was taken, how many units, and so the share (with a
/// 95% range, which is wide when there are few corpses).
/// </summary>
public sealed record DropItem(string Item, int Corpses, int Units, double Share, double Low, double High);

/// <summary>
/// One kind of monster in one zone, over every corpse of it that was looted: how many, their coin and items, and what came off
/// them. <see cref="Few"/> says there are too few corpses for the shares to mean much.
/// </summary>
public sealed record MobDrops(string Mob, string Zone, int Corpses, int WithPurse, long Copper, int Items, IReadOnlyList<DropItem> Drops)
{
    public bool Few => Corpses < DropRates.FewCorpses;
    public double CopperEach => Corpses > 0 ? (double)Copper / Corpses : 0;
    public double ItemsEach => Corpses > 0 ? (double)Items / Corpses : 0;
}

/// <summary>One monster an item comes off: how often.</summary>
public sealed record DropSource(string Mob, string Zone, int Corpses, int OfCorpses, double Share, double Low, double High)
{
    public bool Few => OfCorpses < DropRates.FewCorpses;
}

/// <summary>An item and every kind of monster it was taken from, the most likely first.</summary>
public sealed record DroppedItem(string Item, int Corpses, IReadOnlyList<DropSource> Sources);

/// <summary>
/// How often each item comes off each kind of monster, worked out from the loot. A "corpse" here is one looter's visit to one
/// corpse (<see cref="LootSummary.Corpses"/>), so the share is the share of <b>looted corpses</b>, not of kills: a corpse nobody
/// opened leaves no trace, an item left behind is never recorded (so shares are floors), and a corpse two players both looted
/// counts twice. Monsters are kept apart by zone.
/// </summary>
public static class DropRates
{
    /// <summary>Fewer looted corpses than this and a share is a guess.</summary>
    public const int FewCorpses = 20;

    public static IReadOnlyList<MobDrops> ByMob(IEnumerable<LootEvent> events)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var mobs = new List<MobDrops>();
        foreach (var g in LootSummary.Corpses(events).Where(c => c.Mob).GroupBy(c => (c.Source.ToLowerInvariant(), c.Zone.ToLowerInvariant())))
        {
            var corpses = g.ToList();
            var withItem = new Dictionary<string, int>(comparer);
            var units = new Dictionary<string, int>(comparer);
            foreach (var c in corpses)
                foreach (var t in c.Taken ?? [])
                {
                    withItem[t.Item] = withItem.GetValueOrDefault(t.Item) + 1;
                    units[t.Item] = units.GetValueOrDefault(t.Item) + t.Count;
                }
            int n = corpses.Count;
            var drops = withItem.Select(kv =>
            {
                var (low, high) = Wilson(kv.Value, n);
                return new DropItem(kv.Key, kv.Value, units[kv.Key], (double)kv.Value / n, low, high);
            }).OrderByDescending(d => d.Share).ThenByDescending(d => d.Units).ThenBy(d => d.Item, comparer).ToList();
            mobs.Add(new MobDrops(corpses[0].Source, corpses[0].Zone, n, corpses.Count(c => c.Copper > 0), corpses.Sum(c => c.Copper), corpses.Sum(c => c.Items), drops));
        }
        return mobs.OrderByDescending(m => m.Corpses).ThenBy(m => m.Mob, comparer).ThenBy(m => m.Zone, comparer).ToList();
    }

    /// <summary>The same figures turned around: for each item, where it comes from.</summary>
    public static IReadOnlyList<DroppedItem> ByItem(IReadOnlyList<MobDrops> mobs)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        return mobs.SelectMany(m => m.Drops.Select(d => (Mob: m, Drop: d)))
            .GroupBy(x => x.Drop.Item, comparer)
            .Select(g => new DroppedItem(g.First().Drop.Item, g.Sum(x => x.Drop.Corpses),
                g.Select(x => new DropSource(x.Mob.Mob, x.Mob.Zone, x.Drop.Corpses, x.Mob.Corpses, x.Drop.Share, x.Drop.Low, x.Drop.High))
                    .OrderByDescending(s => s.Share).ThenByDescending(s => s.OfCorpses).ToList()))
            .OrderByDescending(i => i.Corpses).ThenBy(i => i.Item, comparer).ToList();
    }

    /// <summary>The 95% Wilson range for <paramref name="k"/> of <paramref name="n"/>: honest about small samples where a plain percentage is not.</summary>
    public static (double Low, double High) Wilson(int k, int n)
    {
        if (n <= 0) return (0, 0);
        const double z = 1.96;
        double p = (double)k / n, d = 1 + z * z / n;
        double centre = (p + z * z / (2 * n)) / d;
        double half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / d;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }
}
