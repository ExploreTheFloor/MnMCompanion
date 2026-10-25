using System.Globalization;
using System.Text.Json;
using LootWindow.Ledger;
using Microsoft.Data.Sqlite;

namespace LootWindow.Data;

/// <summary>What one pass of <see cref="EventBuilder"/> did: rows looked at, events written, and whether the tables were rebuilt from scratch.</summary>
public sealed record BuildResult(int Scanned, int Added, bool Rebuilt);

/// <summary>
/// Works the typed event tables (vendor trades, harvests, item moves, trades) out of the saved ledger rows. Only a character's own
/// rows (the Character files) feed them. It carries on from the last row it handled, so a pass costs what is new; and because
/// everything it writes comes from <c>ledger_row</c>, raising <see cref="Version"/> (when the way an event is read changes) empties
/// the tables and builds them again, losing nothing.
/// </summary>
public static class EventBuilder
{
    /// <summary>Raise this when an event is read differently, and the tables are rebuilt from the saved rows on the next pass.</summary>
    public const int Version = 1;

    private static readonly string[] Tables = ["trade_item", "trade", "item_move", "harvest", "vendor_trade"];

    public static BuildResult Update(LedgerDb db) => db.With(conn =>
    {
        using var tx = conn.BeginTransaction();
        var had = Meta(conn, tx, "events_version");
        long upto = long.TryParse(Meta(conn, tx, "events_built_upto"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        bool rebuilt = false;
        if (had != Version.ToString(CultureInfo.InvariantCulture))
        {
            foreach (var table in Tables) Run(conn, tx, $"DELETE FROM {table}");
            upto = 0;
            rebuilt = had is not null;
        }

        long max;
        using (var cmd = Command(conn, tx, "SELECT coalesce(max(id), 0) FROM ledger_row")) max = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);

        int scanned = 0, added = 0;
        using (var read = Command(conn, tx, """
            SELECT id, server, character, act, at_utc, name, zone, level, detail FROM ledger_row
            WHERE id > $from AND id <= $to AND kind = 'Character' AND act IN ('act_11','act_12','act_15','act_16','act_20','act_24','act_27','act_28')
            ORDER BY id
            """))
        {
            read.Parameters.AddWithValue("$from", upto);
            read.Parameters.AddWithValue("$to", max);
            // Read them all first: the inserts below use the same connection
            var rows = new List<(long Id, string Server, string Character, string Act, string At, string Name, string Zone, int Level, string Detail)>();
            using (var r = read.ExecuteReader())
                while (r.Read()) rows.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetInt32(7), r.GetString(8)));
            foreach (var row in rows)
            {
                scanned++;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(row.Detail); }
                catch (JsonException) { continue; }   // no detail to read
                using (doc)
                    if (doc.RootElement.ValueKind == JsonValueKind.Object) added += Insert(conn, tx, row, doc.RootElement);
            }
        }

        SetMeta(conn, tx, "events_version", Version.ToString(CultureInfo.InvariantCulture));
        SetMeta(conn, tx, "events_built_upto", max.ToString(CultureInfo.InvariantCulture));
        tx.Commit();
        return new BuildResult(scanned, added, rebuilt);
    });

    private static int Insert(SqliteConnection conn, SqliteTransaction tx,
        (long Id, string Server, string Character, string Act, string At, string Name, string Zone, int Level, string Detail) row, JsonElement d)
    {
        string item = Str(d, "d04") is { Length: > 0 } named ? named : row.Name;
        int qty = Math.Max(1, Int(d, "d01"));
        switch (row.Act)
        {
            case "act_24" or "act_28":
                return Write(conn, tx, """
                    INSERT OR IGNORE INTO vendor_trade (row_id, server, character, at_utc, direction, item, qty, total_copper, zone, level)
                    VALUES ($id, $server, $character, $at, $direction, $item, $qty, $total, $zone, $level)
                    """, row, ("$direction", row.Act == "act_24" ? "sold" : "bought"), ("$item", item), ("$qty", qty), ("$total", LedgerText.Coin(Str(d, "d03"))));
            case "act_27":
                return Write(conn, tx, """
                    INSERT OR IGNORE INTO harvest (row_id, server, character, at_utc, item, qty, zone, level)
                    VALUES ($id, $server, $character, $at, $item, $qty, $zone, $level)
                    """, row, ("$item", item), ("$qty", qty));
            case "act_11" or "act_12" or "act_15":
                return Write(conn, tx, """
                    INSERT OR IGNORE INTO item_move (row_id, server, character, at_utc, kind, item, qty, zone, level)
                    VALUES ($id, $server, $character, $at, $kind, $item, $qty, $zone, $level)
                    """, row, ("$kind", row.Act == "act_11" ? "dropped" : row.Act == "act_12" ? "picked_up" : "destroyed"), ("$item", item), ("$qty", qty));
            default:   // act_16 gave, act_20 received
                int added = Write(conn, tx, """
                    INSERT OR IGNORE INTO trade (row_id, server, character, at_utc, direction, partner, coin_copper, item_count, summary, zone, level)
                    VALUES ($id, $server, $character, $at, $direction, $partner, $coin, $count, $summary, $zone, $level)
                    """, row, ("$direction", row.Act == "act_16" ? "gave" : "received"), ("$partner", Str(d, "partner")),
                    ("$coin", LedgerText.Packed(Str(d, "currency"))), ("$count", Int(d, "itemCount")), ("$summary", Str(d, "summary")));
                if (added > 0 && d.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    int line = 0;
                    foreach (var i in items.EnumerateArray())
                    {
                        if (i.ValueKind != JsonValueKind.Object) continue;
                        using var cmd = Command(conn, tx, "INSERT OR IGNORE INTO trade_item (trade_row_id, line, name, qty, item_id) VALUES ($id, $line, $name, $qty, $iid)");
                        cmd.Parameters.AddWithValue("$id", row.Id);
                        cmd.Parameters.AddWithValue("$line", line++);
                        cmd.Parameters.AddWithValue("$name", Str(i, "name"));
                        cmd.Parameters.AddWithValue("$qty", Math.Max(1, Int(i, "quantity")));
                        cmd.Parameters.AddWithValue("$iid", Str(i, "publicLinkID"));
                        cmd.ExecuteNonQuery();
                    }
                }
                return added;
        }
    }

    private static int Write(SqliteConnection conn, SqliteTransaction tx, string sql,
        (long Id, string Server, string Character, string Act, string At, string Name, string Zone, int Level, string Detail) row, params (string Name, object Value)[] extra)
    {
        using var cmd = Command(conn, tx, sql);
        cmd.Parameters.AddWithValue("$id", row.Id);
        cmd.Parameters.AddWithValue("$server", row.Server);
        cmd.Parameters.AddWithValue("$character", row.Character);
        cmd.Parameters.AddWithValue("$at", row.At);
        cmd.Parameters.AddWithValue("$zone", row.Zone);
        cmd.Parameters.AddWithValue("$level", row.Level);
        foreach (var (name, value) in extra) cmd.Parameters.AddWithValue(name, value);
        return cmd.ExecuteNonQuery();
    }

    private static string Str(JsonElement d, string name) =>
        d.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : v.GetRawText() : "";

    private static int Int(JsonElement d, string name)
    {
        if (!d.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;
    }

    private static SqliteCommand Command(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Run(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = Command(conn, tx, sql);
        cmd.ExecuteNonQuery();
    }

    private static string? Meta(SqliteConnection conn, SqliteTransaction tx, string key)
    {
        using var cmd = Command(conn, tx, "SELECT value FROM meta WHERE key = $k");
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void SetMeta(SqliteConnection conn, SqliteTransaction tx, string key, string value)
    {
        using var cmd = Command(conn, tx, "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = $v");
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }
}
