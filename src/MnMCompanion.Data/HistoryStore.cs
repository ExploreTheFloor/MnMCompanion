using System.Globalization;
using MnMCompanion.History;
using Microsoft.Data.Sqlite;

namespace MnMCompanion.Data;

/// <summary>How much each history holds for one character, over all time (the left rail's small print).</summary>
public sealed record HistoryCounts(int Vendor, int Harvest, int ItemMoves, int Trades);

/// <summary>
/// Reads the event tables for the History pages: a character's rows over a <see cref="Period"/> (local times in, local times out),
/// every sale on the PC for the price book, counts and the days that have anything. Nothing here writes.
/// </summary>
public sealed partial class HistoryStore(LedgerDb db)
{
    public IReadOnlyList<VendorRow> Vendor(string server, string character, Period? period = null) => db.With(conn =>
    {
        using var cmd = Query(conn, "SELECT at_utc, character, direction, item, qty, total_copper, zone FROM vendor_trade WHERE server = $s AND character = $c", server, character, period);
        return Read(cmd, r => new VendorRow(Time(r.GetString(0)), r.GetString(1), r.GetString(2) == "sold" ? VendorDirection.Sold : VendorDirection.Bought,
            r.GetString(3), r.GetInt32(4), r.GetInt64(5), r.GetString(6)));
    });

    /// <summary>Every sale and purchase of every character on this PC, for the price book (vendors pay the same whoever sells).</summary>
    public IReadOnlyList<VendorRow> VendorAll() => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT at_utc, character, direction, item, qty, total_copper, zone FROM vendor_trade ORDER BY at_utc";
        return Read(cmd, r => new VendorRow(Time(r.GetString(0)), r.GetString(1), r.GetString(2) == "sold" ? VendorDirection.Sold : VendorDirection.Bought,
            r.GetString(3), r.GetInt32(4), r.GetInt64(5), r.GetString(6)));
    });

    public IReadOnlyList<HarvestRow> Harvests(string server, string character, Period? period = null) => db.With(conn =>
    {
        using var cmd = Query(conn, "SELECT at_utc, character, item, qty, zone FROM harvest WHERE server = $s AND character = $c", server, character, period);
        return Read(cmd, r => new HarvestRow(Time(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4)));
    });

    public IReadOnlyList<ItemMoveRow> ItemMoves(string server, string character, Period? period = null) => db.With(conn =>
    {
        using var cmd = Query(conn, "SELECT at_utc, character, kind, item, qty, zone FROM item_move WHERE server = $s AND character = $c", server, character, period);
        return Read(cmd, r => new ItemMoveRow(Time(r.GetString(0)), r.GetString(1),
            r.GetString(2) switch { "dropped" => ItemMoveKind.Dropped, "picked_up" => ItemMoveKind.PickedUp, _ => ItemMoveKind.Destroyed },
            r.GetString(3), r.GetInt32(4), r.GetString(5)));
    });

    public IReadOnlyList<TradeRow> Trades(string server, string character, Period? period = null) => db.With(conn =>
    {
        using var cmd = Query(conn, """
            SELECT t.row_id, t.at_utc, t.character, t.direction, t.partner, t.coin_copper, t.zone, i.name, i.qty
            FROM trade t LEFT JOIN trade_item i ON i.trade_row_id = t.row_id
            WHERE t.server = $s AND t.character = $c
            """, server, character, period, column: "t.at_utc");
        var byRow = new Dictionary<long, (DateTime Time, string Character, bool Gave, string Partner, long Coin, string Zone, List<TradeLine> Items)>();
        var order = new List<long>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long id = r.GetInt64(0);
            if (!byRow.TryGetValue(id, out var t))
            {
                t = (Time(r.GetString(1)), r.GetString(2), r.GetString(3) == "gave", r.GetString(4), r.GetInt64(5), r.GetString(6), []);
                byRow[id] = t;
                order.Add(id);
            }
            if (!r.IsDBNull(7)) t.Items.Add(new TradeLine(r.GetString(7), r.GetInt32(8)));
        }
        return order.Select(id => byRow[id]).OrderBy(t => t.Time)
            .Select(t => new TradeRow(t.Time, t.Character, t.Gave, t.Partner, t.Coin, t.Items, t.Zone)).ToList();
    });

    public HistoryCounts Counts(string server, string character) => db.With(conn =>
    {
        int Count(string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT count(*) FROM {table} WHERE server = $s AND character = $c";
            cmd.Parameters.AddWithValue("$s", server);
            cmd.Parameters.AddWithValue("$c", character);
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        return new HistoryCounts(Count("vendor_trade"), Count("harvest"), Count("item_move"), Count("trade"));
    });

    /// <summary>The days (local) on which this character has anything in the four histories, newest first.</summary>
    public IReadOnlyList<DateOnly> Days(string server, string character) => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = string.Join(" UNION ", new[] { "vendor_trade", "harvest", "item_move", "trade" }.Select(t => $"SELECT at_utc FROM {t} WHERE server = $s AND character = $c"));
        cmd.Parameters.AddWithValue("$s", server);
        cmd.Parameters.AddWithValue("$c", character);
        return Read(cmd, r => DateOnly.FromDateTime(Time(r.GetString(0)))).Distinct().OrderByDescending(d => d).ToList();
    });

    // ------------------------------------------------------------------

    private static SqliteCommand Query(SqliteConnection conn, string select, string server, string character, Period? period, string column = "at_utc")
    {
        var cmd = conn.CreateCommand();
        var sql = select;
        cmd.Parameters.AddWithValue("$s", server);
        cmd.Parameters.AddWithValue("$c", character);
        if (period?.From is { } from) { sql += $" AND {column} >= $from"; cmd.Parameters.AddWithValue("$from", LedgerDb.Iso(from)); }
        if (period?.To is { } to) { sql += $" AND {column} < $to"; cmd.Parameters.AddWithValue("$to", LedgerDb.Iso(to)); }
        cmd.CommandText = sql + $" ORDER BY {column}";
        return cmd;
    }

    private static List<T> Read<T>(SqliteCommand cmd, Func<SqliteDataReader, T> map)
    {
        var list = new List<T>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private static DateTime Time(string utc) => DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
}
