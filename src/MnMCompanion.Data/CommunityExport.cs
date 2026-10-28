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
        // The rows of each server on their own, so each row of the file says which server it is from. Characters of one server are added together, as before.
        var servers = store.Servers().Select(server => new ServerRows(server, store.OwnLoot(server), store.Vendor(server, null), store.Harvests(server, null),
            store.Trades(server, null), store.ItemMoves(server, null))).ToList();
        return ContributionBuilder.Build(servers, store.OwnNames(), db.ContributorId(), app, generated ?? DateOnly.FromDateTime(DateTime.Now), store.OwnNpcDialogue());
    }
}

public sealed partial class HistoryStore
{
    /// <summary>The servers this PC has ledger files for, by the name of the game's folder for each (case as the game wrote it).</summary>
    public IReadOnlyList<string> Servers() => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT server FROM ledger_file ORDER BY server COLLATE NOCASE, server";
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    });

    /// <summary>
    /// Every loot row, items and purses, of every character's own Character file, as loot events (the looter is that character).
    /// The rows are exactly the ones the player took, so a party's loot is not counted once for each member. <paramref name="server"/>
    /// narrows it to the characters of one server (null: all of them).
    /// </summary>
    public IReadOnlyList<LootEvent> OwnLoot(string? server = null) => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT character, at_utc, act, zone, name, detail FROM ledger_row WHERE kind = 'Character' AND act IN ('act_13', 'act_14')"
            + (server is null ? "" : " AND server = $server") + " ORDER BY at_utc, id";
        if (server is not null) cmd.Parameters.AddWithValue("$server", server);
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
                    var mob = LootExtractor.PurseSource(Text(d, "d13"), r.GetString(4));
                    if (purse > 0) list.Add(new LootEvent(time, LootKind.Coin, who, true, mob, "", 0, purse, 0, zone));
                }
            }
        }
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
