using LootWindow.Data;
using LootWindow.History;
using Microsoft.Data.Sqlite;

namespace LootWindow.Tests;

/// <summary>The typed event tables worked out of the saved ledger rows, and what the History pages read from them.</summary>
public class EventTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 20, 0, 0, DateTimeKind.Local);

    private static string Detail(string act, string f02, object detail, DateTime when, string who = "Aldric") => Fixture.Row(act, f02, detail, when, who, who);

    private static string Purchase(DateTime when, string item, int count, string coin, string who = "Aldric")
        => Detail("act_28", item, new { d01 = count, d02 = "", d03 = Fixture.B64(coin), d04 = item, d05 = "" }, when, who);

    private static string Harvest(DateTime when, string item, int count = 1, string who = "Aldric")
        => Detail("act_27", "ref_9", new { d01 = count, d02 = "", d03 = Fixture.B64("harvest"), d04 = item, d05 = "43e9d5b85dbdc81fa268da3a" }, when, who);

    private static string Move(string act, DateTime when, string item, int count = 1, string why = "", string who = "Aldric")
        => Detail(act, "ref_7", new { d01 = count, d02 = "", d03 = Fixture.B64(why), d04 = item, d05 = "b57628c324c0c571c04c152a" }, when, who);

    private static string Trade(string act, DateTime when, string partner, string coin, string summary, params (string Name, int Qty)[] items)
        => Detail(act, "name_" + Fixture.B64("x"), new
        {
            partner, itemCount = items.Length, currency = Fixture.B64(coin), summary = Fixture.B64(summary),
            tradingPp = 0, tradingGp = 0, tradingSp = 0, tradingCp = 0,
            items = items.Select(i => new { publicLinkID = "4674d8c8e77842b6166de680", name = i.Name, quantity = i.Qty }).ToArray(),
        }, when);

    private static (LedgerDb Db, HistoryStore Store) Open(Fixture.Scratch s)
    {
        var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        return (db, new HistoryStore(db));
    }

    private static string Ledger(Fixture.Scratch s, string character, string name, params string[] rows)
        => s.Write(Path.Combine("alpha", character, "Ledger", name), Fixture.File(rows));

    private static void Fill(Fixture.Scratch s, string character = "Aldric")
        => Ledger(s, character, $"{character}_Character_2026-10-24.json",
            Fixture.Sale(T0, character, "Rat Ear", 6, "0 platinum 0 gold 0 silver 24 copper"),
            Fixture.Sale(T0.AddMinutes(1), character, "Odd Thing", 1, ""),
            Purchase(T0.AddMinutes(2), "Water Flask", 20, "0 platinum 0 gold 1 silver 0 copper", character),
            Harvest(T0.AddMinutes(3), "Wood", 1, character), Harvest(T0.AddMinutes(4), "Copper Ore", 2, character),
            Move("act_11", T0.AddMinutes(5), "Spear", 1, "user_confirmed_drop", character),
            Move("act_12", T0.AddMinutes(6), "Spear", 1, "", character),
            Move("act_15", T0.AddMinutes(7), "Incarnal Residue", 3, "user_confirmed_destroy", character),
            Trade("act_16", T0.AddMinutes(8), "ref_20917", "0,0,7,0", "trade_completed_with_0_items"),
            Trade("act_20", T0.AddMinutes(8), "ref_20917", "0,0,0,0", "received_from_trade", ("Rawhide Backpack", 1), ("Rawhide Backpack", 1)),
            Fixture.Item(T0.AddMinutes(9), character, character, "a rat", "Rat Tooth"));   // loot is not one of these histories

    // ------------------------------------------------------------------

    [Fact]
    public void Each_kind_of_row_becomes_its_own_typed_event()
    {
        using var s = new Fixture.Scratch();
        Fill(s);
        var (db, store) = Open(s);
        using var _ = db;
        LedgerArchiver.Run(db, s.Path);

        var built = EventBuilder.Update(db);

        Assert.Equal(10, built.Scanned);   // everything but the loot row
        Assert.Equal(10, built.Added);
        Assert.False(built.Rebuilt);
        var vendor = store.Vendor("alpha", "Aldric");
        Assert.Equal(3, vendor.Count);
        var ear = vendor.Single(v => v.Item == "Rat Ear");
        Assert.Equal((VendorDirection.Sold, 6, 24L, 4L), (ear.Direction, ear.Qty, ear.TotalCopper, ear.Each));
        var flask = vendor.Single(v => v.Item == "Water Flask");
        Assert.Equal((VendorDirection.Bought, 20, 100L), (flask.Direction, flask.Qty, flask.TotalCopper));
        Assert.Equal(0, vendor.Single(v => v.Item == "Odd Thing").TotalCopper);   // a sale with no coin read is kept, with no price
        Assert.Equal(["Wood", "Copper Ore"], store.Harvests("alpha", "Aldric").Select(h => h.Item));
        Assert.Equal(2, store.Harvests("alpha", "Aldric")[1].Qty);
        var moves = store.ItemMoves("alpha", "Aldric");
        Assert.Equal([ItemMoveKind.Dropped, ItemMoveKind.PickedUp, ItemMoveKind.Destroyed], moves.Select(m => m.Kind));
        Assert.Equal(3, moves[2].Qty);
        var counts = store.Counts("alpha", "Aldric");
        Assert.Equal((3, 2, 3, 2), (counts.Vendor, counts.Harvest, counts.ItemMoves, counts.Trades));
    }

    [Fact]
    public void A_trade_keeps_its_sides_its_coin_and_its_item_lines()
    {
        using var s = new Fixture.Scratch();
        Fill(s);
        var (db, store) = Open(s);
        using var _ = db;
        LedgerArchiver.Run(db, s.Path);
        EventBuilder.Update(db);

        var rows = store.Trades("alpha", "Aldric");
        var entry = Assert.Single(TradeSummary.Pair(rows));

        Assert.Equal(2, rows.Count);
        Assert.Equal("ref_20917", entry.Partner);
        Assert.Equal(700, entry.GaveCoin);
        Assert.Equal(0, entry.ReceivedCoin);
        Assert.Equal([new TradeLine("Rawhide Backpack", 2)], entry.ReceivedItems);
        Assert.Equal("7sp", TradeSummary.Describe(entry.GaveCoin, entry.GaveItems));
        Assert.Equal("Rawhide Backpack ×2", TradeSummary.Describe(entry.ReceivedCoin, entry.ReceivedItems));
    }

    [Fact]
    public void A_second_pass_adds_nothing_and_a_later_one_adds_only_what_is_new()
    {
        using var s = new Fixture.Scratch();
        Fill(s);
        var (db, store) = Open(s);
        using var _ = db;
        LedgerArchiver.Run(db, s.Path);
        EventBuilder.Update(db);

        Assert.Equal(0, EventBuilder.Update(db).Added);

        Ledger(s, "Aldric", "Aldric_Character_2026-10-25.json", Harvest(T0.AddDays(1), "Resin", 4));
        LedgerArchiver.Run(db, s.Path);
        var third = EventBuilder.Update(db);

        Assert.Equal(1, third.Scanned);
        Assert.Equal(1, third.Added);
        Assert.Equal(3, store.Harvests("alpha", "Aldric").Count);
    }

    [Fact]
    public void Raising_the_version_rebuilds_the_tables_from_the_saved_rows_without_doubling_anything()
    {
        using var s = new Fixture.Scratch();
        Fill(s);
        var (db, store) = Open(s);
        using var _ = db;
        LedgerArchiver.Run(db, s.Path);
        EventBuilder.Update(db);
        using (var conn = new SqliteConnection($"Data Source={db.FilePath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '0' WHERE key = 'events_version'";   // as if an older program had built them
            cmd.ExecuteNonQuery();
        }

        var again = EventBuilder.Update(db);

        Assert.True(again.Rebuilt);
        Assert.Equal(10, again.Added);
        Assert.Equal(3, store.Vendor("alpha", "Aldric").Count);
        Assert.Equal(2, store.Trades("alpha", "Aldric").Count);
    }

    [Fact]
    public void Only_a_characters_own_rows_feed_the_histories_and_each_character_has_its_own()
    {
        using var s = new Fixture.Scratch();
        Fill(s);
        Fill(s, "Wendell");
        // A Social file can't be this character's own doing, whatever act it names
        s.Write(Path.Combine("alpha", "Aldric", "Ledger", "Aldric_Social_2026-10-24.json"), Fixture.File(Harvest(T0.AddMinutes(30), "Wood", 1, "Hobart")));
        var (db, store) = Open(s);
        using var _ = db;
        LedgerArchiver.Run(db, s.Path);

        EventBuilder.Update(db);

        Assert.Equal(2, store.Harvests("alpha", "Aldric").Count);
        Assert.Equal(2, store.Harvests("alpha", "Wendell").Count);
        Assert.Empty(store.Harvests("alpha", "Hobart"));
        Assert.Empty(store.Harvests("beta", "Aldric"));
    }

    [Fact]
    public void A_period_keeps_only_the_rows_inside_it()
    {
        using var s = new Fixture.Scratch();
        Ledger(s, "Aldric", "Aldric_Character_2026-10-24.json",
            Harvest(T0, "Wood"), Harvest(T0.AddHours(3), "Wood"), Harvest(T0.AddDays(1), "Wood"), Harvest(T0.AddDays(-3), "Wood"));
        var (db, store) = Open(s);
        using var _ = db;
        LedgerArchiver.Run(db, s.Path);
        EventBuilder.Update(db);
        var day = Period.Of(DateOnly.FromDateTime(T0), DateOnly.FromDateTime(T0));

        Assert.Equal(2, store.Harvests("alpha", "Aldric", day).Count);
        Assert.Equal(4, store.Harvests("alpha", "Aldric").Count);
        Assert.Equal(3, store.Harvests("alpha", "Aldric", new Period("from the 24th", T0.Date, null)).Count);
        Assert.Single(store.Harvests("alpha", "Aldric", new Period("before", null, T0.Date)));
        Assert.Equal([DateOnly.FromDateTime(T0.AddDays(1)), DateOnly.FromDateTime(T0), DateOnly.FromDateTime(T0.AddDays(-3))], store.Days("alpha", "Aldric"));
    }

    [Fact]
    public void The_price_book_draws_on_every_character_on_the_pc()
    {
        using var s = new Fixture.Scratch();
        Ledger(s, "Aldric", "Aldric_Character_2026-10-24.json", Fixture.Sale(T0, "Aldric", "Rat Ear", 6, "0 platinum 0 gold 0 silver 24 copper"));
        Ledger(s, "Wendell", "Wendell_Character_2026-10-24.json", Fixture.Sale(T0.AddMinutes(1), "Wendell", "Rat Ear", 2, "0 platinum 0 gold 0 silver 10 copper"));
        var (db, store) = Open(s);
        using var _ = db;
        LedgerArchiver.Run(db, s.Path);
        EventBuilder.Update(db);

        var ear = Assert.Single(VendorSummary.PriceBook(store.VendorAll()));

        Assert.Equal(2, ear.Samples);
        Assert.Equal(5, ear.Median);    // 4 and 5, the upper middle
        Assert.Equal((4L, 5L), (ear.Min, ear.Max));
        Assert.Equal(8, ear.Units);
        Assert.Empty(store.Vendor("alpha", "Hobart"));
    }

    [Fact]
    public void An_empty_database_answers_every_question_with_nothing()
    {
        using var s = new Fixture.Scratch();
        var (db, store) = Open(s);
        using var _ = db;

        Assert.Equal(0, EventBuilder.Update(db).Added);
        Assert.Empty(store.Vendor("alpha", "Aldric"));
        Assert.Empty(store.Trades("alpha", "Aldric"));
        Assert.Empty(store.Days("alpha", "Aldric"));
        Assert.Equal(new HistoryCounts(0, 0, 0, 0), store.Counts("alpha", "Aldric"));
    }
}
