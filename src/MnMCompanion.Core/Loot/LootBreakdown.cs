namespace MnMCompanion.Loot;

/// <summary>The class names and the 3-letter codes the ledger's <c>f10</c> carries.</summary>
public static class GameClasses
{
    private static readonly Dictionary<string, string> NameByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["arc"] = "Archer", ["brd"] = "Bard", ["bst"] = "Beastmaster", ["clr"] = "Cleric", ["dru"] = "Druid",
        ["ele"] = "Elementalist", ["enc"] = "Enchanter", ["ftr"] = "Fighter", ["inq"] = "Inquisitor", ["mnk"] = "Monk",
        ["nec"] = "Necromancer", ["pal"] = "Paladin", ["rng"] = "Ranger", ["rog"] = "Rogue", ["shd"] = "Shadow Knight",
        ["shm"] = "Shaman", ["spb"] = "Spellblade", ["wiz"] = "Wizard",
    };

    /// <summary>"nec" → "Necromancer"; a code not in the table is shown in capitals.</summary>
    public static string NameOf(string code) => NameByCode.TryGetValue(code, out var name) ? name : code.ToUpperInvariant();
}

/// <summary>One kind of mob one looter took from: how many, and the coin and items they gave.</summary>
public sealed record MemberMob(string Mob, int Looted, long Copper, int Items)
{
    public double CopperEach => Looted > 0 ? (double)Copper / Looted : 0;
    public double ItemsEach => Looted > 0 ? (double)Items / Looted : 0;
}

/// <summary>
/// An item one looter took: how many, how much of the group's of that item that is, where it mostly came from, and what a
/// vendor pays for one (<see cref="Value"/>).
/// </summary>
public sealed record MemberItem(string Item, int Count, int GroupCount, string MostlyFrom, ItemValue Value = default)
{
    public double Share => GroupCount > 0 ? (double)Count / GroupCount : 0;
    public long Total => Value.Each * Count;
}

/// <summary>An item the group took: how many, what a vendor pays for one, and who took them.</summary>
public sealed record ItemWorth(string Item, int Count, ItemValue Value, IReadOnlyList<(string Who, int Count)> By)
{
    public long Total => Value.Each * Count;
}

/// <summary>
/// One person on a day: <see cref="Mobs"/> corpses, <see cref="Items"/> taken, and the coin counted for them, <see cref="Copper"/>: the purses they
/// looted that were not divided, whole, plus (for a ledger's owner) the shares that reached them (<see cref="Received"/>). A divided purse is
/// not counted for its looter: they kept only part and the ledger doesn't say how much (<see cref="SplitPurses"/> of them looted).
/// <see cref="Purses"/> is the whole of every purse they looted, and <see cref="Split"/> the part of those that reached the ledger's owner.
/// <see cref="Class"/> and <see cref="Level"/> come from the ledger (empty / 0 when it never named them).
/// </summary>
public sealed record LootMember(string Name, bool You, string Class, int Level, int Mobs, int Items, long Copper, long Split,
    IReadOnlyList<MemberItem> Taken, IReadOnlyList<MemberMob> MobList, DateTime First, DateTime Last,
    long ItemValue = 0, int Priced = 0, int Estimated = 0, int Unpriced = 0, long Purses = 0, long Received = 0, int SplitPurses = 0, int EstimatedShares = 0, int Unrecorded = 0)
{
    /// <summary>The coin counted for them plus what a vendor would pay for the items (the priced and the estimated ones; <see cref="Unpriced"/> add nothing).</summary>
    public long Total => Copper + ItemValue;

    /// <summary>What the purses they looted held, a mob's worth on average (the whole purse, whoever kept it).</summary>
    public double CopperPerMob => Mobs > 0 ? (double)Purses / Mobs : 0;
    public double ItemsPerMob => Mobs > 0 ? (double)Items / Mobs : 0;

    /// <summary>"Fighter 28", "lvl 28", "Fighter" or "".</summary>
    public string ClassLevel => Level > 0 ? (Class.Length > 0 ? $"{Class} {Level}" : $"lvl {Level}") : Class;
}

/// <summary>
/// What reached the ledger's owner: <see cref="Copper"/> (in a group only their shares of divided purses, their own included; the purses they looted
/// alone, whole) and what a vendor would pay for the items they looted. This is the one player whose coin is known in full: of everyone
/// else the ledger shows only the purses they looted, never what an autosplit gave them, so the group is compared by items alone.
/// <see cref="EstimatedShares"/> of the shares in the coin were not in the ledger and are estimated (the owner's own purses in a group).
/// </summary>
public sealed record LootOurs(long Copper, long ItemValue, int Items, int Estimated, int Unpriced, int EstimatedShares = 0, int Unrecorded = 0)
{
    public long Total => Copper + ItemValue;

    /// <summary>The figure includes estimates (items valued by their family, not their own price; shares of the owner's own purses worked out, not read).</summary>
    public bool Approximate => Estimated > 0 || EstimatedShares > 0;

    public static LootOurs None { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>One kind of mob across the group: how many were looted, their coin and items, and who looted most of them.</summary>
public sealed record MobLoot(string Mob, int Looted, long Copper, int Items, string TopLooter)
{
    public double CopperEach => Looted > 0 ? (double)Copper / Looted : 0;
    public double ItemsEach => Looted > 0 ? (double)Items / Looted : 0;
}

/// <summary>
/// A day's loot arranged for a breakdown: the group's <see cref="Members"/> (the most valuable items looted first), what was looted
/// (<see cref="Items"/>, with who took them) and from which mobs (<see cref="Mobs"/>). Built from the day's
/// <see cref="LootEvent"/>s with <see cref="Of"/>.
/// </summary>
public sealed record LootDay(DateOnly Day, LootTotals Totals, IReadOnlyList<LootMember> Members, IReadOnlyList<ItemTakers> Items,
    IReadOnlyList<MobLoot> Mobs, IReadOnlyList<LootEvent> Events, IReadOnlyList<ItemWorth>? Worth = null)
{
    /// <summary>The items taken, most valuable first (those with no price after, most numerous first).</summary>
    public IReadOnlyList<ItemWorth> ItemsWorth => Worth ?? [];

    public long ItemValue => Members.Sum(m => m.ItemValue);
    public int Priced => Members.Sum(m => m.Priced);
    public int Estimated => Members.Sum(m => m.Estimated);
    public int Unpriced => Members.Sum(m => m.Unpriced);

    /// <summary>Purses plus what a vendor would pay for the items.</summary>
    public long Total => Totals.Copper + ItemValue;

    /// <summary>
    /// How many corpses' purses the ledger gives without the creature (<see cref="LootExtractor.UnknownCreature"/>). The game's ledger parser can't read a creature
    /// with an apostrophe in its name (Skri'Var, Mur'Hua): it writes no item rows for its corpses and doesn't name it, so their items are missing here.
    /// </summary>
    public int Unrecorded => Events.Count(e => e.Kind == LootKind.Coin && e.Source == LootExtractor.UnknownCreature);

    /// <summary>
    /// What reached the ledger's owner (their coin and the items they looted). When several of the player's own characters are in view it adds
    /// them up; <see cref="LootOurs.None"/> when nobody in the day is the owner.
    /// </summary>
    public LootOurs Ours
    {
        get
        {
            var mine = Members.Where(m => m.You).ToList();
            return mine.Count == 0 ? LootOurs.None
                : new LootOurs(mine.Sum(m => m.Copper), mine.Sum(m => m.ItemValue), mine.Sum(m => m.Items), mine.Sum(m => m.Estimated), mine.Sum(m => m.Unpriced),
                    mine.Sum(m => m.EstimatedShares), mine.Sum(m => m.Unrecorded));
        }
    }

    /// <summary>The total includes estimates (items valued by their family, not their own price).</summary>
    public bool Approximate => Estimated > 0;

    public DateTime First => Events.Count == 0 ? default : Events.Min(e => e.Time);
    public DateTime Last => Events.Count == 0 ? default : Events.Max(e => e.Time);

    public LootMember? Member(string? name) =>
        name is null ? null : Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Everything the day's members, items and mobs are called, lower case, for a filter box to look through.</summary>
    public string SearchText => string.Join(' ', Members.Select(m => m.Name).Concat(Items.Select(i => i.Item)).Concat(Mobs.Select(m => m.Mob))).ToLowerInvariant();

    public static LootDay Of(DateOnly day, IReadOnlyList<LootEvent> events, ItemValues? values = null)
    {
        values ??= ItemValues.Empty;
        var live = events.OrderBy(e => e.Time).ToList();
        var corpses = LootSummary.Corpses(live);
        var itemTakers = LootSummary.ByItem(live);
        var groupCount = itemTakers.ToDictionary(i => i.Item, i => i.Count, StringComparer.OrdinalIgnoreCase);
        var comparer = StringComparer.OrdinalIgnoreCase;

        var members = new List<LootMember>();
        foreach (var p in LootSummary.ByPlayer(live))
        {
            var mine = live.Where(e => e.Who.Equals(p.Who, StringComparison.OrdinalIgnoreCase)).ToList();
            var named = mine.LastOrDefault(e => e.Class.Length > 0 || e.Level > 0);
            var mobList = corpses.Where(c => c.Mob && c.Who.Equals(p.Who, StringComparison.OrdinalIgnoreCase))
                .GroupBy(c => c.Source, comparer)
                .Select(g => new MemberMob(g.Key, g.Count(), g.Sum(c => c.Copper), g.Sum(c => c.Items)))
                .OrderByDescending(m => m.Looted).ThenBy(m => m.Mob, comparer).ToList();
            var taken = p.Taken.Select(t => new MemberItem(t.Item, t.Count, groupCount.GetValueOrDefault(t.Item, t.Count),
                    mine.Where(e => e.Kind == LootKind.Item && e.Item.Equals(t.Item, StringComparison.OrdinalIgnoreCase))
                        .GroupBy(e => e.Source, comparer).OrderByDescending(g => g.Sum(e => e.Count)).Select(g => g.Key).FirstOrDefault() ?? "", values.Of(t.Item)))
                .ToList();
            long itemValue = taken.Sum(t => t.Total);
            int priced = taken.Where(t => t.Value.Known && !t.Value.Estimated).Sum(t => t.Count);
            int estimated = taken.Where(t => t.Value.Estimated).Sum(t => t.Count);
            int unpriced = taken.Where(t => !t.Value.Known).Sum(t => t.Count);
            members.Add(new LootMember(p.Who, p.You, named is null || named.Class.Length == 0 ? "" : GameClasses.NameOf(named.Class), named?.Level ?? 0,
                p.Mobs, p.Items, p.Copper, mine.Where(e => e.Kind == LootKind.Coin).Sum(e => e.Split), taken, mobList,
                mine.Count == 0 ? default : mine.Min(e => e.Time), mine.Count == 0 ? default : mine.Max(e => e.Time), itemValue, priced, estimated, unpriced,
                p.Purses, p.Received, p.SplitPurses, p.EstimatedShares,
                mine.Count(e => e.Kind == LootKind.Coin && e.Source == LootExtractor.UnknownCreature)));
        }
        // By what each took: coin is no basis for comparing people, since only the ledger owner's shares of divided purses are ever known
        members = members.OrderByDescending(m => m.ItemValue).ThenByDescending(m => m.Items).ThenByDescending(m => m.Mobs).ThenBy(m => m.Name, comparer).ToList();

        var mobs = corpses.Where(c => c.Mob).GroupBy(c => c.Source, comparer)
            .Select(g => new MobLoot(g.Key, g.Count(), g.Sum(c => c.Copper), g.Sum(c => c.Items),
                g.GroupBy(c => c.Who, comparer).OrderByDescending(w => w.Count()).Select(w => w.Key).First()))
            .OrderByDescending(m => m.Looted).ThenBy(m => m.Mob, comparer).ToList();

        var worth = itemTakers.Select(i => new ItemWorth(i.Item, i.Count, values.Of(i.Item), i.By))
            .OrderByDescending(i => i.Total).ThenByDescending(i => i.Count).ThenBy(i => i.Item, comparer).ToList();
        return new LootDay(day, LootSummary.Totals(live), members, itemTakers, mobs, live, worth);
    }
}
