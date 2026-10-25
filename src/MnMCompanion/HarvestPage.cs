using System.Globalization;
using System.Text;
using System.Windows;
using MnMCompanion.Data;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion;

internal sealed record HarvestData(IReadOnlyList<HarvestRow> Rows);

/// <summary>
/// Harvest: what you gathered, how fast, and what it is worth at vendor prices (the same item values as the loot, so an estimate is
/// marked <c>~</c> and an item with no price <c>+</c>). The ledger names the item and the zone, not the node.
/// </summary>
internal sealed class HarvestPage(HistoryFocus focus, HistoryData data, HistoryView view)
    : HistoryPage<HarvestData>(focus, data, view, "Everything you gather (ore, wood, herbs and so on) appears here, with how many you get an hour and what it would fetch from a vendor.")
{
    protected override HarvestData Query(HistoryStore s, string? server, string? character, Period period) => new(s.Harvests(server, character, period));

    protected override void Render(HarvestData data)
    {
        var rows = data.Rows.Where(r => View.Matches(r.Item, r.Zone)).ToList();
        if (rows.Count == 0) { Empty("Harvest", Scope("nothing gathered")); return; }
        var totals = HarvestSummary.Totals(rows);
        var items = HarvestSummary.ByItem(rows);

        long worth = 0;
        bool estimated = false, unpriced = false;
        foreach (var i in items)
        {
            var v = Store.Values.Of(i.Item);
            if (!v.Known) unpriced = true;
            else { worth += v.Each * i.Units; estimated |= v.Estimated; }
        }
        string Worth() => LootSummary.Worth(worth, estimated, unpriced);

        Head("Harvest", Scope(Plural(totals.Harvests, "harvest")), Worth(), "worth at vendor prices");
        AddTile("Harvests", $"{totals.Harvests:N0}", $"{Plural(totals.Kinds, "kind")} of item");
        AddTile("Items", $"{totals.Units:N0}", "gathered in all");
        AddTile("Time gathering", totals.Active > TimeSpan.Zero ? Duration(totals.Active) : "—", "excluding long breaks");
        AddTile("Per hour", totals.PerHour is { } p ? p.ToString("N0", CultureInfo.CurrentCulture) : "—", "items an hour");
        AddTile("Worth", Worth(), unpriced ? "some unpriced" : estimated ? "includes estimates" : "at vendor prices");
        if (items.Count > 0) AddTile("Most gathered", items[0].Item, $"{items[0].Units:N0} items");

        Tabs(["Items", "Log"], tab => tab == "Items" ? ItemsTable(items, totals) : Log(rows), tab => [("Copy summary", () => Summary(totals, items, Worth()))]);
    }

    private FrameworkElement ItemsTable(IReadOnlyList<HarvestItem> items, HarvestTotals totals) =>
        Table.Build("harvest-items", items, new Col<HarvestItem>[]
        {
            new("Item", 0, i => i.Item, i => i.Item, Right: false),
            new("Harvests", 72, i => $"{i.Harvests:N0}", i => i.Harvests),
            new("Items", 64, i => $"{i.Units:N0}", i => i.Units),
            new("Per hour", 74, i => totals.Active.TotalMinutes >= 1 ? (i.Units / totals.Active.TotalHours).ToString("N0", CultureInfo.CurrentCulture) : "—", i => i.Units,
                Tip: "Items an hour over all the time spent gathering, so the rows add up to the total"),
            new("Each", 84, i => Price(i.Item), i => Store.Values.Of(i.Item).Each, Tip: "What a vendor pays for one. ~ = estimated; — = no price known yet"),
            new("Worth", 100, i => Store.Values.Of(i.Item) is { Known: true } v ? (v.Estimated ? "~" : "") + LootSummary.Coin(v.Each * i.Units) : "—", i => Store.Values.Of(i.Item).Each * i.Units),
            new("Where", 220, i => string.Join(", ", i.Zones.Take(3)) + (i.Zones.Count > 3 ? $" +{i.Zones.Count - 3}" : ""), i => i.Zones.Count > 0 ? i.Zones[0] : "", Right: false),
            new("Last", 120, i => When(i.Last), i => i.Last, Right: false),
        }, Rebuild, defaultSort: 2, bar: i => i.Units, color: _ => Palette.Heal);

    private string Price(string item) => Store.Values.Of(item) is { Known: true } v ? (v.Estimated ? "~" : "") + LootSummary.Coin(v.Each) : "—";

    private FrameworkElement Log(List<HarvestRow> rows)
    {
        var all = rows.OrderByDescending(r => r.Time).ToList();
        var table = Table.Build(TableId("harvest-log"), all.Take(Rows).ToList(), WithWho(new Col<HarvestRow>[]
        {
            new("Time", 130, r => When(r.Time), r => r.Time, Right: false),
            new("Item", 0, r => r.Item, r => r.Item, Right: false),
            new("Qty", 60, r => $"{r.Qty:N0}", r => r.Qty),
            new("Where", 200, r => r.Zone, r => r.Zone, Right: false),
        }, r => r.Character), Rebuild, defaultSort: 0);
        return WithNote(LogNote(Math.Min(all.Count, Rows), all.Count), table);
    }

    private string Summary(HarvestTotals t, IReadOnlyList<HarvestItem> items, string worth)
    {
        var text = new StringBuilder();
        text.AppendLine($"Harvest · {View.Period.Label}");
        text.AppendLine($"{Plural(t.Harvests, "harvest")}, {t.Units:N0} items" + (t.PerHour is { } p ? $", {p:N0} an hour" : "") + $", worth {worth}");
        foreach (var i in items.Take(15)) text.AppendLine($"  {i.Item} ×{i.Units:N0}");
        return text.ToString().TrimEnd();
    }
}
