using System.Text;
using System.Windows;
using MnMCompanion.Data;
using MnMCompanion.History;
using MnMCompanion.Loot;

namespace MnMCompanion;

internal sealed record TradesData(IReadOnlyList<TradeRow> Rows);

/// <summary>
/// Trades: what you handed over and what you got, one line per trade. The ledger gives the other player as an id, never a
/// name, so a partner shows as "Trader #71358": the same player is the same number every time.
/// </summary>
internal sealed class TradesPage(HistoryFocus focus, HistoryData data, HistoryView view)
    : HistoryPage<TradesData>(focus, data, view, "Trades with other players appear here: what you gave, and what you got.")
{
    protected override TradesData Query(HistoryStore s, string? server, string? character, Period period) => new(s.Trades(server, character, period));

    protected override void Render(TradesData data)
    {
        var trades = TradeSummary.Pair(data.Rows)
            .Where(t => View.Matches(TradeSummary.Label(t.Partner), TradeSummary.Describe(t.GaveCoin, t.GaveItems), TradeSummary.Describe(t.ReceivedCoin, t.ReceivedItems), t.Zone, Who(t.Character))).ToList();
        if (trades.Count == 0) { Empty("Trades", Scope("no trades")); return; }

        var partners = trades.GroupBy(t => t.Partner).ToList();
        long gaveCoin = trades.Sum(t => t.GaveCoin), gotCoin = trades.Sum(t => t.ReceivedCoin);
        Head("Trades", Scope(Plural(trades.Count, "trade")), $"{trades.Count:N0}", "trades");
        AddTile("Coin given", LootSummary.Coin(gaveCoin), "handed over in trades", gaveCoin > 0 ? Palette.Taken : null);
        AddTile("Coin received", LootSummary.Coin(gotCoin), "got in trades", gotCoin > 0 ? Palette.Heal : null);
        AddTile("Items given", $"{trades.Sum(t => t.GaveCount):N0}", "handed over");
        AddTile("Items received", $"{trades.Sum(t => t.ReceivedCount):N0}", "got");
        AddTile("Traders", $"{partners.Count:N0}", $"{partners.Count(p => p.Count() > 1):N0} traded more than once");

        Tabs(["Trades"], _ => TradeTable(trades), _ => [("Copy summary", () => Summary(trades))]);
    }

    private FrameworkElement TradeTable(List<TradeEntry> trades) =>
        Table.Build(TableId("trades"), trades.Take(Rows).ToList(), WithWho(new Col<TradeEntry>[]
        {
            new("Time", 130, t => When(t.Time), t => t.Time, Right: false),
            new("Trader", 120, t => TradeSummary.Label(t.Partner), t => t.Partner, Right: false, Tip: "The game gives the other player as an id, not a name"),
            new("You gave", 0, t => TradeSummary.Describe(t.GaveCoin, t.GaveItems), t => t.GaveCoin + t.GaveCount, Right: false),
            new("You received", 0, t => TradeSummary.Describe(t.ReceivedCoin, t.ReceivedItems), t => t.ReceivedCoin + t.ReceivedCount, Right: false),
        }, t => t.Character), Rebuild, defaultSort: 0, copy: t => $"{When(t.Time)}  gave {TradeSummary.Describe(t.GaveCoin, t.GaveItems)}, got {TradeSummary.Describe(t.ReceivedCoin, t.ReceivedItems)}");

    private string Summary(List<TradeEntry> trades)
    {
        var text = new StringBuilder();
        text.AppendLine($"Trades · {View.Period.Label} · {trades.Count:N0}");
        foreach (var t in trades.Take(30)) text.AppendLine($"  {When(t.Time)}  {TradeSummary.Label(t.Partner)}: gave {TradeSummary.Describe(t.GaveCoin, t.GaveItems)}; got {TradeSummary.Describe(t.ReceivedCoin, t.ReceivedItems)}");
        return text.ToString().TrimEnd();
    }
}
