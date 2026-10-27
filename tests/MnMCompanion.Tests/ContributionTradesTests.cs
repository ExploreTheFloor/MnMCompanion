using System.Text.Json;
using System.Text.RegularExpressions;
using MnMCompanion.Community;
using MnMCompanion.History;

namespace MnMCompanion.Tests;

/// <summary>
/// The trades with other players (schema 3): each trade is both of the ledger's rows put together, with what each side handed over, the day and the zone, and nothing
/// that says who the other player was. They are sent as a third document, a "trades" part.
/// </summary>
public class ContributionTradesTests
{
    private const string Id = "0123456789abcdef0123456789abcdef", Me = "Aldric", Partner = "ref_71358";
    private static readonly DateTime Noon = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Made = new(2026, 10, 25);

    private static TradeRow Gave(DateTime at, long coin = 0, string zone = "nightharborw", string partner = Partner, params (string Name, int Qty)[] items) =>
        new(at, Me, true, partner, coin, items.Select(i => new TradeLine(i.Name, i.Qty)).ToList(), zone);

    private static TradeRow Got(DateTime at, long coin = 0, string zone = "nightharborw", string partner = Partner, params (string Name, int Qty)[] items) =>
        new(at, Me, false, partner, coin, items.Select(i => new TradeLine(i.Name, i.Qty)).ToList(), zone);

    private static Contribution Make(IEnumerable<TradeRow> trades, DateOnly? made = null) =>
        ContributionBuilder.Build([], [], [], [], Id, "MnMCompanion 0.4.0", made ?? Made, null, trades);

    [Fact]
    public void The_two_rows_of_a_trade_become_one_with_what_each_side_handed_over()
    {
        var c = Make([
            Gave(Noon, 0, "nightharborw", Partner, ("Rawhide Backpack", 1)),
            Got(Noon.AddSeconds(1), 700),
        ]);

        var t = Assert.Single(c.Trades);
        Assert.Equal("2026-10-24", t.Day);
        Assert.Equal("nightharborw", t.Zone);
        Assert.Equal(0, t.Gave.Coin);
        Assert.Equal([new ContributionTradeItem("Rawhide Backpack", 1)], t.Gave.Items);
        Assert.Equal(700, t.Got.Coin);
        Assert.Empty(t.Got.Items);
    }

    [Fact]
    public void An_item_for_items_trade_and_a_coin_and_item_trade_keep_every_item_with_how_many()
    {
        var c = Make([
            Gave(Noon, 50, "z1", Partner, ("Bat Wing", 3), ("Rat Ear", 20)),
            Got(Noon.AddSeconds(2), 0, "z1", Partner, ("Ration", 2), ("Water Flask", 1)),
        ]);

        var t = Assert.Single(c.Trades);
        Assert.Equal(50, t.Gave.Coin);
        Assert.Equal([new ContributionTradeItem("Rat Ear", 20), new ContributionTradeItem("Bat Wing", 3)], t.Gave.Items);   // the most first
        Assert.Equal([new ContributionTradeItem("Ration", 2), new ContributionTradeItem("Water Flask", 1)], t.Got.Items);
    }

    [Fact]
    public void Nothing_says_who_the_other_player_was_or_when_in_the_day_or_who_the_player_is()
    {
        var c = Make([Gave(Noon.AddHours(5).AddMinutes(17).AddSeconds(41), 0, "z1", "ref_55555", ("Bat Wing", 3)), Got(Noon.AddHours(5).AddMinutes(17).AddSeconds(42), 9, "z1", "ref_55555")]);

        foreach (var json in new[] { c.ToTradesPartJson(), c.ToTradesPartJson(indented: true), c.ToJson() })
        {
            Assert.DoesNotContain("ref_", json);
            Assert.DoesNotContain("55555", json);
            Assert.DoesNotContain(Me, json);
            Assert.DoesNotContain("partner", json, StringComparison.OrdinalIgnoreCase);
            Assert.False(Regex.IsMatch(json, @"\d\d:\d\d"), "a time of day got into the file");
        }
    }

    [Fact]
    public void The_trades_travel_as_a_part_of_their_own_and_not_in_the_totals_file()
    {
        var c = Make([Gave(Noon, 0, "z1", Partner, ("Bat Wing", 3)), Got(Noon.AddSeconds(1), 9, "z1")]);

        using var core = JsonDocument.Parse(c.ToCoreJson());
        Assert.False(core.RootElement.TryGetProperty("trades", out _));
        Assert.Equal(4, core.RootElement.GetProperty("schema").GetInt32());

        var part = c.ToTradesPartJson();
        Assert.DoesNotContain("\n", part);
        using var doc = JsonDocument.Parse(part);
        var root = doc.RootElement;
        Assert.Equal(["schema", "part", "contributor", "app", "generated", "license", "trades"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal((4, "trades", Id, "2026-10-25", "CC-BY-NC-SA-4.0"),
            (root.GetProperty("schema").GetInt32(), root.GetProperty("part").GetString(), root.GetProperty("contributor").GetString(), root.GetProperty("generated").GetString(), root.GetProperty("license").GetString()));
        var trade = root.GetProperty("trades")[0];
        Assert.Equal(["day", "zone", "gave", "got", "server"], trade.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["coin", "items"], trade.GetProperty("gave").EnumerateObject().Select(p => p.Name));
        Assert.Equal(["item", "qty"], trade.GetProperty("gave").GetProperty("items")[0].EnumerateObject().Select(p => p.Name));

        Assert.Contains("\"trades\"", c.ToJson());   // the picture of everything in it has them
    }

    [Fact]
    public void The_day_is_the_utc_day_the_trade_happened_and_the_newest_trade_comes_first()
    {
        var late = new DateTime(2026, 10, 24, 23, 59, 0, DateTimeKind.Utc);
        var early = new DateTime(2026, 10, 3, 0, 1, 0, DateTimeKind.Utc);
        var c = Make([Gave(early, 5, "z1", "ref_1"), Gave(late, 7, "z1", "ref_2")]);

        Assert.Equal(["2026-10-24", "2026-10-03"], c.Trades.Select(t => t.Day));
        Assert.Equal([7L, 5L], c.Trades.Select(t => t.Gave.Coin));
    }

    [Fact]
    public void A_file_holds_the_newest_three_thousand_trades_and_says_so_in_one_place()
    {
        var rows = Enumerable.Range(0, ContributionBuilder.MaxTrades + 50).Select(i => Gave(Noon.AddMinutes(-i * 10), i + 1, "z1", "ref_" + i)).ToList();

        var c = Make(rows);

        Assert.Equal(ContributionBuilder.MaxTrades, c.Trades.Count);
        Assert.Equal(1, c.Trades[0].Gave.Coin);   // the newest
        Assert.Equal(ContributionBuilder.MaxTrades, c.Trades[^1].Gave.Coin);   // the oldest kept
    }

    [Fact]
    public void A_trade_with_nothing_on_either_side_or_from_before_2025_is_left_out_and_a_clock_ahead_is_held_to_the_day_the_file_was_made()
    {
        var c = Make([
            Gave(Noon, 0, "z1", "ref_1"),   // nothing at all
            Gave(new DateTime(2024, 12, 31, 12, 0, 0, DateTimeKind.Utc), 9, "z1", "ref_2"),
            Gave(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc), 4, "z1", "ref_3"),
        ]);

        var t = Assert.Single(c.Trades);
        Assert.Equal(4, t.Gave.Coin);
        Assert.Equal("2026-10-25", t.Day);   // the file's own day, never after it
    }

    [Fact]
    public void Items_are_cleaned_merged_and_kept_inside_what_the_service_accepts()
    {
        var many = Enumerable.Range(1, 45).Select(i => ($"Item {i:00}", 1)).ToArray();
        var c = Make([
            Gave(Noon, 0, "z1", "ref_1", [("Bat Wing", 2), ("bat wing", 3), ("  Rat Ear  ", 4), ("Gone", 0), ("   ", 5), ("bad\u0007name", 1), ("Big Pile", 20000)]),
            Gave(Noon.AddHours(1), 1, "z1", "ref_2", many),
            Gave(Noon.AddHours(2), 1, "", "ref_3"),
            Gave(Noon.AddHours(3), 1, "bad\u0007zone", "ref_4"),
            Gave(Noon.AddHours(4), -50, "z1", "ref_5", ("Bat Wing", 1)),
        ]);

        var byDay = c.Trades.OrderBy(t => t.Day).ThenBy(t => t.Gave.Coin).ToList();
        var first = c.Trades.Single(t => t.Gave.Items.Any(i => i.Item == "Big Pile"));
        Assert.Equal([new ContributionTradeItem("Big Pile", ContributionBuilder.MaxTradeQty), new ContributionTradeItem("Bat Wing", 5), new ContributionTradeItem("Rat Ear", 4)], first.Gave.Items);
        Assert.Equal(ContributionBuilder.MaxTradeItems, c.Trades.Single(t => t.Gave.Items.Count > 3 && t.Gave.Items.Any(i => i.Item.StartsWith("Item "))).Gave.Items.Count);
        Assert.Contains(c.Trades, t => t.Zone == "unknown" && t.Gave.Coin == 1 && t.Day == "2026-10-24");
        Assert.DoesNotContain(c.Trades, t => t.Zone.Any(char.IsControl));
        Assert.Contains(c.Trades, t => t.Gave.Coin == 0 && t.Gave.Items.Count == 1);   // a negative coin counts as none, the item still counts
        Assert.NotEmpty(byDay);
    }

    [Fact]
    public void A_player_with_no_trades_has_none_and_the_file_still_describes_it()
    {
        var c = Make([]);
        Assert.Empty(c.Trades);
        Assert.Contains(ContributionBuilder.Describe(c), l => l.StartsWith("0 trades with other players"));
        using var doc = JsonDocument.Parse(c.ToTradesPartJson());
        Assert.Equal(0, doc.RootElement.GetProperty("trades").GetArrayLength());
    }

    // ------------------------------------------------------------------ when they are sent, and who has agreed

    [Fact]
    public void The_trades_are_sent_when_there_are_some_and_they_are_not_the_ones_last_sent()
    {
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var none = Make([]);
        var some = Make([Gave(Noon, 0, "z1", Partner, ("Bat Wing", 3)), Got(Noon.AddSeconds(1), 9, "z1")]);

        Assert.Equal(ShareStep.NothingToShare, SharePlan.DecideTrades(none, null, now, null));
        Assert.Equal(ShareStep.Send, SharePlan.DecideTrades(some, null, now, null));

        var print = SharePlan.TradesFingerprint(some);
        Assert.Equal(ShareStep.Unchanged, SharePlan.DecideTrades(some, print, now, now.AddHours(-7)));
        Assert.Equal(ShareStep.Send, SharePlan.DecideTrades(some, print, now, now.AddDays(-8)));   // sent again after a week so the service doesn't forget
        Assert.Equal(ShareStep.Send, SharePlan.DecideTrades(some, "something else", now, now.AddHours(-7)));
    }

    [Fact]
    public void The_fingerprint_follows_the_trades_and_not_the_day_the_file_was_made()
    {
        var rows = new[] { Gave(Noon, 0, "z1", Partner, ("Bat Wing", 3)), Got(Noon.AddSeconds(1), 9, "z1") };
        var a = SharePlan.TradesFingerprint(Make(rows, new DateOnly(2026, 10, 25)));
        var b = SharePlan.TradesFingerprint(Make(rows, new DateOnly(2026, 10, 26)));
        var c = SharePlan.TradesFingerprint(Make([.. rows, Gave(Noon.AddDays(1), 5, "z1", "ref_2")], new DateOnly(2026, 10, 26)));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void A_player_who_only_traded_has_something_to_share_and_the_dialogue_is_not_sent_again_because_of_a_trade()
    {
        var onlyTrades = Make([Gave(Noon, 0, "z1", Partner, ("Bat Wing", 3)), Got(Noon.AddSeconds(1), 9, "z1")]);
        Assert.False(SharePlan.Nothing(onlyTrades));
        Assert.True(SharePlan.Nothing(Make([])));

        var one = Make([Gave(Noon, 5, "z1", "ref_1")]);
        var two = Make([Gave(Noon, 5, "z1", "ref_1"), Gave(Noon.AddDays(1), 6, "z1", "ref_2")]);
        Assert.Equal(SharePlan.NpcFingerprint(one), SharePlan.NpcFingerprint(two));   // a new trade doesn't resend the dialogue
        Assert.NotEqual(SharePlan.Fingerprint(one), SharePlan.Fingerprint(two));       // the totals file holds the weekly trade counts, so it has changed
        Assert.NotEqual(SharePlan.TradesFingerprint(one), SharePlan.TradesFingerprint(two));
    }

    [Fact]
    public void A_yes_given_before_trades_were_shared_has_to_be_given_again()
    {
        Assert.False(CommunityPolicy.Consented(true, 2, ContributionBuilder.Schema));
        Assert.True(CommunityPolicy.NeedsRenewal(true, 2, ContributionBuilder.Schema));
        Assert.False(CommunityPolicy.Consented(true, 3, ContributionBuilder.Schema), "schema 4 added the server, so a Yes to trades alone has lapsed too");
        Assert.True(CommunityPolicy.Consented(true, ContributionBuilder.Schema, ContributionBuilder.Schema));
        Assert.False(CommunityPolicy.NeedsRenewal(false, 2, ContributionBuilder.Schema), "a No is not asked to renew: it is simply asked again");
        Assert.False(CommunityPolicy.MaySend(false));
    }
}
