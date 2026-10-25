using System.Text;
using System.Windows;
using LootWindow.Data;
using LootWindow.History;
using LootWindow.Loot;

namespace LootWindow;

internal sealed record ItemChangesData(IReadOnlyList<ItemMoveRow> Rows);

/// <summary>
/// Drops / Pickups / Destroys: what you did with items you held. Dropped is putting one on the ground, Picked up is taking one
/// off it (often your own item back), Destroyed is deleting it. Nothing here is about monsters; it is where items went.
/// </summary>
internal sealed class ItemChangesPage(LootStore store, HistoryData data, HistoryView view)
    : HistoryPage<ItemChangesData>(store, data, view, "Items you drop on the ground, pick up off it, or destroy appear here, so you can see where an item went.")
{
    protected override ItemChangesData Query(HistoryStore s, string server, string character, Period period) => new(s.ItemMoves(server, character, period));

    protected override void Render(ItemChangesData data)
    {
        var rows = data.Rows.Where(r => View.Matches(r.Item, r.Zone)).ToList();
        if (rows.Count == 0) { Empty("Drops / Pickups / Destroys", Scope("nothing dropped, picked up or destroyed")); return; }
        var t = ItemChangeSummary.Totals(rows);

        Head("Drops / Pickups / Destroys", Scope(Plural(rows.Count, "change")), $"{t.Dropped + t.Destroyed:N0}", "items dropped or destroyed");
        AddTile("Dropped", $"{t.Dropped:N0}", "put on the ground");
        AddTile("Picked up", $"{t.PickedUp:N0}", "taken off the ground");
        AddTile("Destroyed", $"{t.Destroyed:N0}", "deleted for good", t.Destroyed > 0 ? Palette.Taken : null);
        AddTile("Taken back", $"{t.TakenBack:N0}", "picked up soon after", t.TakenBack > 0 ? Palette.Heal : null);

        Tabs(["Log", "Most discarded"], tab => tab == "Log" ? Log(rows) : Discarded(rows), tab => [("Copy summary", () => Summary(t, rows))]);
    }

    private static string KindName(ItemMoveKind kind) => kind switch { ItemMoveKind.Dropped => "Dropped", ItemMoveKind.PickedUp => "Picked up", _ => "Destroyed" };

    private FrameworkElement Log(List<ItemMoveRow> rows)
    {
        var all = rows.OrderByDescending(r => r.Time).ToList();
        var table = Table.Build("changes-log", all.Take(Rows).ToList(), new Col<ItemMoveRow>[]
        {
            new("Time", 130, r => When(r.Time), r => r.Time, Right: false),
            new("What", 100, r => KindName(r.Kind), r => r.Kind.ToString(), Right: false),
            new("Item", 0, r => r.Item, r => r.Item, Right: false),
            new("Qty", 56, r => $"{r.Qty:N0}", r => r.Qty),
            new("Where", 200, r => r.Zone, r => r.Zone, Right: false),
        }, Rebuild, defaultSort: 0);
        return WithNote(LogNote(Math.Min(all.Count, Rows), all.Count), table);
    }

    private FrameworkElement Discarded(List<ItemMoveRow> rows) =>
        Table.Build("changes-discarded", ItemChangeSummary.MostDiscarded(rows), new Col<DiscardedItem>[]
        {
            new("Item", 0, i => i.Item, i => i.Item, Right: false),
            new("Dropped", 80, i => $"{i.Dropped:N0}", i => i.Dropped),
            new("Destroyed", 84, i => $"{i.Destroyed:N0}", i => i.Destroyed),
            new("Picked up", 84, i => $"{i.PickedUp:N0}", i => i.PickedUp),
            new("Gone", 74, i => $"{i.Lost:N0}", i => i.Lost, Tip: "Dropped plus destroyed, less what was picked up again"),
        }, Rebuild, defaultSort: 4, bar: i => i.Lost, color: _ => Palette.Taken);

    private string Summary(ItemChangeTotals t, List<ItemMoveRow> rows)
    {
        var text = new StringBuilder();
        text.AppendLine($"Drops / Pickups / Destroys · {View.Period.Label}");
        text.AppendLine($"Dropped {t.Dropped:N0}, picked up {t.PickedUp:N0}, destroyed {t.Destroyed:N0} ({t.TakenBack:N0} taken back)");
        foreach (var i in ItemChangeSummary.MostDiscarded(rows).Where(i => i.Lost > 0).Take(15)) text.AppendLine($"  {i.Item}: {i.Lost:N0} gone");
        return text.ToString().TrimEnd();
    }
}
