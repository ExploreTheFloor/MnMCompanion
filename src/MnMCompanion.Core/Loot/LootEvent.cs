using MnMCompanion.Ledger;

namespace MnMCompanion.Loot;

/// <summary>Item = one item taken off a corpse; Coin = a purse.</summary>
public enum LootKind : byte { Item, Coin }

/// <summary>
/// One loot row of the ledger. <see cref="Who"/> is the looter (<see cref="You"/> when it is the character whose ledger
/// this is); <see cref="Source"/> what was looted ("a Dustrend legionary", "your corpse"). For an item <see cref="Item"/> and
/// <see cref="Count"/> say what; for coin <see cref="Copper"/> is the whole purse and <see cref="Split"/> the share the
/// ledger's owner received of it (0 when there was none). <see cref="Class"/> is the looter's class code ("ftr") and
/// <see cref="Level"/> their level, as the ledger gives them.
/// </summary>
public sealed record LootEvent(
    DateTime Time, LootKind Kind, string Who, bool You, string Source, string Item, int Count, long Copper, long Split,
    string Zone = "", string Class = "", int Level = 0, string ItemId = "")
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

    private static readonly TimeSpan TwinWindow = TimeSpan.FromSeconds(2);

    public static List<LootEvent> Extract(IEnumerable<LedgerRow> rows)
    {
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
        return events.OrderBy(e => e.Time).ToList();
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
        var source = r.Text("d13") is { Length: > 0 } s ? s : r.Name;
        return purse <= 0 ? null : new LootEvent(r.Time, LootKind.Coin, r.Actor, r.ByOwner, source, "", 0, purse, LedgerText.Packed(r.Text("d15")), r.Zone, r.Class, r.Level);
    }
}
