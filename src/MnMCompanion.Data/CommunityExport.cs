using System.Globalization;
using System.Text.Json;
using MnMCompanion.Community;
using MnMCompanion.History;
using MnMCompanion.Ledger;
using MnMCompanion.Loot;

namespace MnMCompanion.Data;

/// <summary>
/// Builds the file a player can share with the community data from the saved database: their OWN rows only (the Character files of
/// every character on this PC), as aggregates. Other players' loot, which the Social files hold, is deliberately left out: it
/// would be counted again by every party member who sent theirs, and it names people.
/// </summary>
public static class CommunityExport
{
    public static Contribution Build(LedgerDb db, string app, DateOnly? generated = null)
    {
        var store = new HistoryStore(db);
        return ContributionBuilder.Build(store.OwnLoot(), store.VendorAll(), store.HarvestsAll(), store.OwnNames(),
            db.ContributorId(), app, generated ?? DateOnly.FromDateTime(DateTime.Now));
    }
}

public sealed partial class HistoryStore
{
    /// <summary>
    /// Every loot row, items and purses, of every character's own Character file, as loot events (the looter is that character).
    /// The rows are exactly the ones the player took, so a party's loot is not counted once for each member.
    /// </summary>
    public IReadOnlyList<LootEvent> OwnLoot() => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT character, at_utc, act, zone, name, detail FROM ledger_row WHERE kind = 'Character' AND act IN ('act_13', 'act_14') ORDER BY at_utc, id";
        var list = new List<LootEvent>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(r.GetString(5)); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var d = doc.RootElement;
                if (d.ValueKind != JsonValueKind.Object) continue;
                var who = r.GetString(0);
                var time = Time(r.GetString(1));
                var zone = r.GetString(3);
                if (r.GetString(2) == "act_13")
                {
                    var item = Text(d, "d04").Trim();
                    if (item.Length > 0) list.Add(new LootEvent(time, LootKind.Item, who, true, Text(d, "d02"), item, Math.Max(1, Number(d, "d01")), 0, 0, zone));
                }
                else
                {
                    // d12 is the coin as digits, a hundred to the next coin (d11 counts ten and is wrong)
                    long purse = LedgerText.Packed(Text(d, "d12"));
                    var mob = Text(d, "d13") is { Length: > 0 } named ? named : r.GetString(4);
                    if (purse > 0) list.Add(new LootEvent(time, LootKind.Coin, who, true, mob, "", 0, purse, 0, zone));
                }
            }
        }
        return list;
    });

    /// <summary>Every harvest of every character on this PC.</summary>
    public IReadOnlyList<HarvestRow> HarvestsAll() => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT at_utc, character, item, qty, zone FROM harvest ORDER BY at_utc";
        var list = new List<HarvestRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new HarvestRow(Time(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4)));
        return list;
    });

    /// <summary>The item names the player's own rows have met, with the 24-character id the game gave each.</summary>
    public IReadOnlyList<ContributionName> OwnNames() => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT json_extract(detail, '$.d04'), json_extract(detail, '$.d05') FROM ledger_row
            WHERE kind = 'Character' AND act IN ('act_11', 'act_12', 'act_13', 'act_15', 'act_27') AND json_valid(detail)
            """;
        var list = new List<ContributionName>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (!r.IsDBNull(0) && !r.IsDBNull(1)) list.Add(new ContributionName(r.GetString(0), r.GetString(1)));
        return list;
    });

    private static string Text(JsonElement d, string name) =>
        d.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind is JsonValueKind.Null ? "" : v.GetRawText() : "";

    private static int Number(JsonElement d, string name)
    {
        if (!d.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;
    }
}
