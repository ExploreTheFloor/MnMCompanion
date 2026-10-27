using System.Globalization;
using System.Text;
using System.Windows;
using MnMCompanion.Data;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion;

internal sealed record VendorsData(IReadOnlyList<VendorRow> Rows, IReadOnlyList<PriceEntry> Book);

/// <summary>
/// Vendors: what you sold and bought and what the vendors paid and charged. Sales and Purchases total each item over the period;
/// the Price book is every price this PC has seen (all characters, all time), the very figures item values are worked out from;
/// Log is every stack.
/// </summary>
internal sealed class VendorsPage(HistoryFocus focus, HistoryData data, HistoryView view)
    : HistoryPage<VendorsData>(focus, data, view, "Sales to vendors and purchases from them appear here, with the price of each. Sell something to a vendor and it will show up within a few seconds.")
{
    protected override VendorsData Query(HistoryStore s, string? server, string? character, Period period) =>
        new(s.Vendor(server, character, period), VendorSummary.PriceBook(s.VendorAll()));

    protected override void Render(VendorsData data)
    {
        var rows = data.Rows.Where(r => View.Matches(r.Item, r.Zone, Who(r.Character))).ToList();
        if (rows.Count == 0 && data.Book.Count == 0) { Empty("Vendors", Scope("nothing sold or bought")); return; }
        var t = VendorSummary.Totals(rows);

        Head("Vendors", Scope(Plural(rows.Count, "stack") + " sold or bought"), rows.Count == 0 ? "" : Signed(t.Net), rows.Count == 0 ? "" : "sold minus bought");
        AddTile("Sold for", LootSummary.Coin(t.SoldCopper), $"{Plural(t.SoldUnits, "item")} in {Plural(t.SoldStacks, "stack")}", Palette.Heal);
        AddTile("Bought for", LootSummary.Coin(t.BoughtCopper), $"{Plural(t.BoughtUnits, "item")} in {Plural(t.BoughtStacks, "stack")}");
        AddTile("Net", Signed(t.Net), "sold minus bought", t.Net >= 0 ? Palette.Heal : Palette.Taken);
        if (t.BestSeller.Length > 0) AddTile("Best seller", t.BestSeller, LootSummary.Coin(t.BestSellerCopper) + " in all");
        AddTile("Price book", $"{data.Book.Count:N0}", "items with a price");

        Tabs(["Sales", "Purchases", "Price book", "Log"], tab => tab switch
        {
            "Sales" => Items(rows, VendorDirection.Sold),
            "Purchases" => Items(rows, VendorDirection.Bought),
            "Price book" => Book(data.Book),
            _ => Log(rows),
        }, tab => [("Copy summary", () => Summary(t, rows))]);
    }

    private FrameworkElement Items(List<VendorRow> rows, VendorDirection direction)
    {
        var sold = direction == VendorDirection.Sold;
        return Table.Build(sold ? "vendor-sales" : "vendor-purchases", VendorSummary.ByItem(rows, direction), new Col<VendorItem>[]
        {
            new("Item", 0, i => i.Item, i => i.Item, Right: false),
            new("Stacks", 64, i => $"{i.Stacks:N0}", i => i.Stacks),
            new("Items", 64, i => $"{i.Units:N0}", i => i.Units),
            new(sold ? "Paid each" : "Cost each", 92, i => i.Each > 0 ? LootSummary.Coin(i.Each) : "—", i => i.Each, Tip: "The middle price for one"),
            new("Total", 110, i => LootSummary.Coin(i.Total), i => i.Total),
            new(sold ? "Last sold" : "Last bought", 130, i => When(i.Last), i => i.Last, Right: false),
        }, Rebuild, defaultSort: 4, bar: i => i.Total, color: _ => sold ? Palette.Heal : Palette.Taken);
    }

    private FrameworkElement Book(IReadOnlyList<PriceEntry> book)
    {
        var shown = book.Where(p => View.Matches(p.Item)).ToList();
        var table = Table.Build("vendor-book", shown, new Col<PriceEntry>[]
        {
            new("Item", 0, p => p.Item, p => p.Item, Right: false),
            new("Pays", 90, p => p.Median > 0 ? LootSummary.Coin(p.Median) : "—", p => p.Median, Tip: "What a vendor pays for one: the middle of every sale. This is the figure item values are worked out from"),
            new("Range", 130, p => p.Samples == 0 ? "—" : p.Min == p.Max ? LootSummary.Coin(p.Min) : $"{LootSummary.Coin(p.Min)} – {LootSummary.Coin(p.Max)}", p => p.Max,
                Tip: "The lowest and highest paid for one. Vendors differ, and small prices are rounded"),
            new("Sales", 60, p => $"{p.Samples:N0}", p => p.Samples, Tip: "How many stacks of it have been sold"),
            new("Items sold", 80, p => $"{p.Units:N0}", p => p.Units),
            new("Last sold", 130, p => p.Samples == 0 ? "—" : p.Last.ToString("MMM d  HH:mm", CultureInfo.CurrentCulture), p => p.Last, Right: false),
            new("Costs", 90, p => p.BoughtMedian > 0 ? LootSummary.Coin(p.BoughtMedian) : "—", p => p.BoughtMedian, Tip: "What one cost when you bought it from a vendor"),
        }, Rebuild, defaultSort: 1, bar: p => p.Median, color: _ => Palette.Gold,
        copy: p => $"{p.Item}: vendors pay {LootSummary.Coin(p.Median)}" + (p.Min != p.Max ? $" ({LootSummary.Coin(p.Min)} to {LootSummary.Coin(p.Max)})" : "") + $", {p.Samples:N0} sales");
        return WithNote("Every price this PC has seen: all your characters, all time, not just the period above. Sell one of an item to learn its price.", table);
    }

    private FrameworkElement Log(List<VendorRow> rows)
    {
        var all = rows.OrderByDescending(r => r.Time).ToList();
        var table = Table.Build(TableId("vendor-log"), all.Take(Rows).ToList(), WithWho(new Col<VendorRow>[]
        {
            new("Time", 120, r => When(r.Time), r => r.Time, Right: false),
            new("What", 70, r => r.Direction == VendorDirection.Sold ? "Sold" : "Bought", r => r.Direction.ToString(), Right: false),
            new("Item", 0, r => r.Item, r => r.Item, Right: false),
            new("Qty", 56, r => $"{r.Qty:N0}", r => r.Qty),
            new("Each", 90, r => r.Each > 0 ? LootSummary.Coin(r.Each) : "—", r => r.Each),
            new("Total", 110, r => r.TotalCopper > 0 ? LootSummary.Coin(r.TotalCopper) : "—", r => r.TotalCopper),
        }, r => r.Character), Rebuild, defaultSort: 0);
        return WithNote(LogNote(Math.Min(all.Count, Rows), all.Count), table);
    }

    private string Summary(VendorTotals t, List<VendorRow> rows)
    {
        var text = new StringBuilder();
        text.AppendLine($"Vendors · {View.Period.Label}");
        text.AppendLine($"Sold {Plural(t.SoldUnits, "item")} for {LootSummary.Coin(t.SoldCopper)}; bought {Plural(t.BoughtUnits, "item")} for {LootSummary.Coin(t.BoughtCopper)}; net {Signed(t.Net)}");
        foreach (var i in VendorSummary.ByItem(rows, VendorDirection.Sold).Take(15)) text.AppendLine($"  {i.Item} ×{i.Units:N0}  {LootSummary.Coin(i.Total)}");
        return text.ToString().TrimEnd();
    }
}
