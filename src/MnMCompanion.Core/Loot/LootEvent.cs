using MnMCompanion.Ledger;

namespace MnMCompanion.Loot;

/// <summary>
/// The part of a divided purse that reached one ledger owner (a character of the player's), as that owner's ledger shows it. <see cref="Estimated"/>:
/// the ledger doesn't show it (the owner looted the purse themselves, and their ledger writes that as a purse kept whole), so it was worked out
/// from the shares of other purses (<see cref="GroupSplits"/>).
/// </summary>
public sealed record ShareTo(string Owner, long Copper, bool Estimated = false);

/// <summary>Item = one item taken off a corpse; Coin = a purse.</summary>
public enum LootKind : byte { Item, Coin }

/// <summary>
/// One loot row of the ledger. <see cref="Who"/> is the looter (<see cref="You"/> when it is the character whose ledger
/// this is); <see cref="Source"/> what was looted ("a Dustrend legionary", "your corpse"). For an item <see cref="Item"/> and
/// <see cref="Count"/> say what; for coin <see cref="Copper"/> is the whole purse and <see cref="Split"/> the share the
/// ledger's owner received of it (0 when there was none). <see cref="Class"/> is the looter's class code ("ftr") and
/// <see cref="Level"/> their level, as the ledger gives them. For a purse, <see cref="Copper"/> is the whole purse (what the group earned from it) and
/// <see cref="Shared"/> says the looter's autosplit divided it among the group, so the looter did not keep it all; <see cref="Shares"/> is what the
/// ledger shows of the division: the share of each ledger owner who saw it. What the other members got is never in a ledger.
/// </summary>
public sealed record LootEvent(
    DateTime Time, LootKind Kind, string Who, bool You, string Source, string Item, int Count, long Copper, long Split,
    string Zone = "", string Class = "", int Level = 0, string ItemId = "", bool Shared = false, IReadOnlyList<ShareTo>? Shares = null)
{
    public DateOnly Day => DateOnly.FromDateTime(Time);
}

/// <summary>
/// Turns ledger rows into loot. <c>act_13</c> is an item looted (<c>d01</c> count, <c>d02</c> the creature, <c>d04</c> the
/// item, <c>d05</c> its 24-hex id); <c>act_14</c> a purse taken off a corpse (<c>d13</c> the creature, <c>d12</c> the coin as
/// digits "pp,gp,sp,cp", <c>d15</c> the share the file's owner received). The owner's own loot is in the Character files;
/// the Social files hold the party's and repeat the owner's a moment later, so an owner's row in a Social file is taken only
/// when no Character row says the same thing within two seconds.
/// </summary>
public static class LootExtractor
{
    public const string ItemAct = "act_13";
    public const string CoinAct = "act_14";

    /// <summary>What a purse's creature is called when the ledger doesn't say.</summary>
    public const string UnknownCreature = "an unknown creature";

    /// <summary>
    /// The creature a purse came from: <c>d13</c>, or the row's name. The game's build of 2026-10-27 stopped naming it: <c>d13</c> and the name hold the word
    /// "party_split" for a divided purse and nothing for the owner's own (and the item rows are not written at all), so those words are not a creature.
    /// Empty when it isn't named.
    /// </summary>
    public static string PurseSource(string d13, string name)
    {
        foreach (var text in new[] { d13, name })
            if (text.Trim() is { Length: > 0 } t && !t.Equals("party_split", StringComparison.OrdinalIgnoreCase) && !t.Equals("npc_corpse", StringComparison.OrdinalIgnoreCase)) return t;
        return "";
    }

    private static readonly TimeSpan TwinWindow = TimeSpan.FromSeconds(2);

    public static List<LootEvent> Extract(IEnumerable<LedgerRow> source)
    {
        var rows = source as IReadOnlyList<LedgerRow> ?? source.ToList();
        var loot = rows.Where(r => r.Act is ItemAct or CoinAct).ToList();
        // What the owner's Character files say, to recognise the Social copies of it
        var own = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in loot.Where(r => r.File == LedgerFileKind.Character))
            (own.TryGetValue(Key(r), out var list) ? list : own[Key(r)] = []).Add(r.Time);

        var events = new List<LootEvent>();
        foreach (var r in loot)
        {
            if (r.Actor.Length == 0) continue;
            if (r.File == LedgerFileKind.Social && r.ByOwner && own.TryGetValue(Key(r), out var times) && times.Any(t => (t - r.Time).Duration() <= TwinWindow)) continue;
            if (ToEvent(r) is { } e) events.Add(e);
        }
        return OwnPurses(events.OrderBy(e => e.Time).ToList(), rows);
    }

    private static readonly string[] PartyActs = ["act_31", "act_32", "act_33", "act_34", "act_35"];

    /// <summary>
    /// A purse the owner looted themselves is written as theirs whole (d14 "npc_corpse", no d15) even when their group's autosplit divided it, so what
    /// the ledger says is not what reached them. In a group, such a purse is a divided one: it goes whole into the group's coin and only the owner's
    /// share, estimated from what they were handed of others' purses (<see cref="GroupSplits"/>), counts as theirs. Alone, it stays theirs whole.
    /// </summary>
    private static List<LootEvent> OwnPurses(List<LootEvent> events, IReadOnlyList<LedgerRow> rows)
    {
        if (!events.Any(e => e.Kind == LootKind.Coin && e.You && !e.Shared)) return events;
        var byOwner = new Dictionary<string, GroupSplits>(StringComparer.OrdinalIgnoreCase);
        GroupSplits Splits(string owner)
        {
            if (byOwner.TryGetValue(owner, out var known)) return known;
            var party = rows.Where(r => r.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase) && PartyActs.Contains(r.Act))
                .Select(r => new PartyChange(r.Time, r.Act, r.ByOwner, r.Int("d62")));
            var divided = events.Where(e => e.Kind == LootKind.Coin && e.Shared && !e.Who.Equals(owner, StringComparison.OrdinalIgnoreCase))
                .Select(e => (e, Share: e.Shares?.FirstOrDefault(s => s.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase) && !s.Estimated)))
                .Where(x => x.Share is { Copper: > 0 })
                .Select(x => new DividedPurse(x.e.Time, x.e.Copper, x.Share!.Copper));
            return byOwner[owner] = new GroupSplits(party, divided);
        }

        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.Kind != LootKind.Coin || !e.You || e.Shared) continue;
            var splits = Splits(e.Who);
            if (!splits.InGroup(e.Time)) continue;
            long share = splits.ShareOf(e.Time, e.Copper);
            events[i] = e with { Shared = true, Split = share, Shares = [new ShareTo(e.Who, share, Estimated: true)] };
        }
        return events;
    }

    private static string Key(LedgerRow r) =>
        r.Act == ItemAct ? $"{r.Act}|{r.Get("d04")}|{r.Text("d02")}" : $"{r.Act}|{LedgerText.Packed(r.Text("d12"))}|{r.Text("d13")}";

    public static LootEvent? ToEvent(LedgerRow r)
    {
        if (r.Act == ItemAct)
        {
            var item = r.Get("d04").Trim();
            return item.Length == 0 ? null
                : new LootEvent(r.Time, LootKind.Item, r.Actor, r.ByOwner, r.Text("d02"), item, Math.Max(1, r.Int("d01")), 0, 0, r.Zone, r.Class, r.Level, r.Get("d05"));
        }
        // d11 counts ten copper to a silver and is wrong; d12 is the coin as digits, a hundred to the next coin
        long purse = LedgerText.Packed(r.Text("d12"));
        var source = PurseSource(r.Text("d13"), r.Name) is { Length: > 0 } named ? named : UnknownCreature;
        // d14 says whether the looter's autosplit divided the purse among the group ("party_split") or the looter kept it whole ("npc_corpse");
        // d15 is the ledger owner's share of a divided one. What the others got is not in the ledger.
        long share = LedgerText.Packed(r.Text("d15"));
        bool shared = r.Text("d14").Equals("party_split", StringComparison.OrdinalIgnoreCase);
        return purse <= 0 ? null : new LootEvent(r.Time, LootKind.Coin, r.Actor, r.ByOwner, source, "", 0, purse, share, r.Zone, r.Class, r.Level, "",
            shared, share > 0 ? new[] { new ShareTo(r.Owner, share) } : null);
    }
}
