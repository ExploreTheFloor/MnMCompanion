using System.Text.Json;
using MnMCompanion.Data;
using Microsoft.Data.Sqlite;

namespace MnMCompanion.Tests;

/// <summary>The app's own database: every ledger row ever read is kept once, exactly as written, and nothing read again changes it.</summary>
public class ArchiveTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Local);

    private static string LedgerFile(Fixture.Scratch s, string server, string character, string name, params string[] rows)
        => s.Write(Path.Combine(server, character, "Ledger", name), Fixture.File(rows));

    private static string ArchiveFile(Fixture.Scratch s, string server, string character, string name, params string[] rows)
        => s.Write(Path.Combine(server, character, "Ledger", "Archive", name), Fixture.File(rows));

    private static T Scalar<T>(LedgerDb db, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={db.FilePath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
    }

    [Fact]
    public void Every_act_from_every_kind_of_file_is_kept_including_unknown_ones()
    {
        using var s = new Fixture.Scratch();
        LedgerFile(s, "alpha", "Aldric", "Aldric_Character_2026-10-24.json",
            Fixture.Item(T0, "Aldric", "Aldric", "a rat", "Rat Tooth"),
            Fixture.Sale(T0.AddMinutes(1), "Aldric", "Rat Tooth", 3, "0 platinum 0 gold 0 silver 12 copper"),
            Fixture.Row("act_27", "ref_5", new { d01 = 1, d02 = "", d03 = Fixture.B64("harvest"), d04 = "Wood", d05 = "" }, T0.AddMinutes(2), "Aldric", "Aldric"),
            Fixture.Row("act_01", "34", new { d21 = 34, d22 = 33, d23 = "" }, T0.AddMinutes(3), "Aldric", "Aldric"),
            Fixture.Row("act_02", "", new { }, T0.AddMinutes(4), "Aldric", "Aldric"));
        LedgerFile(s, "alpha", "Aldric", "Aldric_Social_2026-10-24.json",
            Fixture.Purse(T0, "Rosalind", "Aldric", "a rat", "0,0,0,5"),
            Fixture.Row("act_32", "name_" + Fixture.B64("Rosalind"), new { d61 = "", d62 = 2, d63 = Fixture.B64("PartyJoin"), d64 = "ref_9" }, T0.AddMinutes(5), "Rosalind", "Aldric"),
            Fixture.Row("act_99", "mystery", new { x = 1 }, T0.AddMinutes(6), "Rosalind", "Aldric"));
        ArchiveFile(s, "alpha", "Aldric", "ledger_Aldric_Social_archive_20261024_120000.json",
            Fixture.Item(T0.AddHours(-2), "Hobart", "Aldric", "a bandit", "Cloth Scraps", "clr"));

        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        var result = LedgerArchiver.Run(db, s.Path);

        Assert.Equal(9, result.RowsAdded);
        Assert.Equal(3, result.FilesRead);
        Assert.Empty(result.Problems);
        Assert.Equal(9, db.RowCount());
        var byAct = db.RowsByAct().ToDictionary(a => a.Act, a => a.Rows);
        Assert.Equal(2, byAct["act_13"]);
        Assert.Equal(1, byAct["act_14"]);
        Assert.Equal(1, byAct["act_27"]);
        Assert.Equal(1, byAct["act_02"]);
        Assert.Equal(1, byAct["act_99"]);   // an act nobody has described is still kept
        Assert.Equal(3, db.FileCount());
        Assert.Equal(1L, Scalar<long>(db, "SELECT count(*) FROM ledger_file WHERE archived = 1"));
        Assert.Equal(1L, Scalar<long>(db, "SELECT count(*) FROM ledger_row WHERE file LIKE 'Archive%'"));
    }

    [Fact]
    public void Reading_again_adds_nothing_and_does_not_reread_unchanged_files()
    {
        using var s = new Fixture.Scratch();
        LedgerFile(s, "alpha", "Aldric", "Aldric_Character_2026-10-24.json", Fixture.Item(T0, "Aldric", "Aldric", "a rat", "Rat Tooth"));
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        LedgerArchiver.Run(db, s.Path);

        var again = LedgerArchiver.Run(db, s.Path);

        Assert.Equal(0, again.FilesRead);
        Assert.Equal(0, again.RowsAdded);
        Assert.Equal(1, db.RowCount());
    }

    [Fact]
    public void A_row_the_game_appends_is_the_only_new_one()
    {
        using var s = new Fixture.Scratch();
        var first = Fixture.Item(T0, "Aldric", "Aldric", "a rat", "Rat Tooth");
        LedgerFile(s, "alpha", "Aldric", "Aldric_Character_2026-10-24.json", first);
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        LedgerArchiver.Run(db, s.Path);

        LedgerFile(s, "alpha", "Aldric", "Aldric_Character_2026-10-24.json", first, Fixture.Item(T0.AddMinutes(1), "Aldric", "Aldric", "a rat", "Rat Ear"));
        var result = LedgerArchiver.Run(db, s.Path);

        Assert.Equal(1, result.FilesRead);
        Assert.Equal(1, result.RowsAdded);
        Assert.Equal(2, db.RowCount());
    }

    [Fact]
    public void An_archived_row_that_repeats_a_dated_one_is_kept_once_and_archive_only_rows_are_added()
    {
        using var s = new Fixture.Scratch();
        var a = Fixture.Item(T0, "Hobart", "Aldric", "a bandit", "Cloth Scraps", "clr");
        var b = Fixture.Item(T0.AddMinutes(1), "Hobart", "Aldric", "a bandit", "Worn Bow", "clr");
        var c = Fixture.Item(T0.AddMinutes(2), "Hobart", "Aldric", "a bandit", "Cloth Scraps", "clr");
        LedgerFile(s, "alpha", "Aldric", "Aldric_Social_2026-10-24.json", a, b);
        ArchiveFile(s, "alpha", "Aldric", "ledger_Aldric_Social_archive_20261024_130000.json", b, c);
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        var result = LedgerArchiver.Run(db, s.Path);

        Assert.Equal(4, result.RowsSeen);
        Assert.Equal(3, result.RowsAdded);
        Assert.Equal(3, db.RowCount());
    }

    [Fact]
    public void Rows_the_game_moves_out_of_a_file_or_deletes_stay_in_the_database()
    {
        using var s = new Fixture.Scratch();
        var rows = Enumerable.Range(0, 5).Select(i => Fixture.Item(T0.AddMinutes(i), "Hobart", "Aldric", "a bandit", $"Item {i}", "clr")).ToArray();
        var dated = LedgerFile(s, "alpha", "Aldric", "Aldric_Social_2026-10-24.json", rows);
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        LedgerArchiver.Run(db, s.Path);

        // The game rotates the oldest rows out of the dated file, then the file goes away altogether
        LedgerFile(s, "alpha", "Aldric", "Aldric_Social_2026-10-24.json", rows[3], rows[4]);
        LedgerArchiver.Run(db, s.Path);
        File.Delete(dated);
        LedgerArchiver.Run(db, s.Path);

        Assert.Equal(5, db.RowCount());
    }

    [Fact]
    public void A_file_caught_half_written_is_reported_and_taken_on_a_later_pass()
    {
        using var s = new Fixture.Scratch();
        var row = Fixture.Item(T0, "Aldric", "Aldric", "a rat", "Rat Tooth");
        var path = s.Write(Path.Combine("alpha", "Aldric", "Ledger", "Aldric_Character_2026-10-24.json"), "{\"c01\":[" + row + ",{\"f01\":\"act_1");
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        var first = LedgerArchiver.Run(db, s.Path);
        Assert.Single(first.Problems);
        Assert.Equal(0, db.RowCount());

        File.WriteAllText(path, Fixture.File(row));
        var second = LedgerArchiver.Run(db, s.Path);

        Assert.Empty(second.Problems);
        Assert.Equal(1, db.RowCount());
    }

    [Fact]
    public void Two_identical_rows_inside_one_file_are_both_kept_and_stay_two_when_read_again()
    {
        using var s = new Fixture.Scratch();
        var row = Fixture.Item(T0, "Aldric", "Aldric", "a rat", "Rat Tooth");
        var path = LedgerFile(s, "alpha", "Aldric", "Aldric_Character_2026-10-24.json", row, row);
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        LedgerArchiver.Run(db, s.Path);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));   // looks changed, so it is read again
        var again = LedgerArchiver.Run(db, s.Path);

        Assert.Equal(1, again.FilesRead);
        Assert.Equal(0, again.RowsAdded);
        Assert.Equal(2, db.RowCount());
    }

    [Fact]
    public void The_same_row_in_two_characters_ledgers_is_kept_for_each()
    {
        using var s = new Fixture.Scratch();
        var row = Fixture.Item(T0, "Hobart", "Aldric", "a bandit", "Cloth Scraps", "clr");   // both saw the same party loot
        LedgerFile(s, "alpha", "Aldric", "Aldric_Social_2026-10-24.json", row);
        LedgerFile(s, "alpha", "Wendell", "Wendell_Social_2026-10-24.json", row);
        LedgerFile(s, "beta", "Other", "Other_Social_2026-10-24.json", row);
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        LedgerArchiver.Run(db, s.Path);

        Assert.Equal(3, db.RowCount());
        Assert.Equal(2L, Scalar<long>(db, "SELECT count(DISTINCT server) FROM ledger_row"));
    }

    [Fact]
    public void A_row_is_kept_as_written_with_its_detail_decoded_and_the_file_envelope_saved()
    {
        using var s = new Fixture.Scratch();
        var loot = Fixture.Item(T0, "Aldric", "Aldric", "a small cellar rat", "Rat Tooth");
        var trade = Fixture.Row("act_20", "name_" + Fixture.B64("Marguerite"), new
        {
            partner = "ref_71358", itemCount = 1, currency = Fixture.B64("0,0,6,0"), summary = Fixture.B64("received_from_trade"),
            tradingPp = 0, tradingGp = 0, tradingSp = 6, tradingCp = 0,
            items = new[] { new { publicLinkID = "4674d8c8e77842b6166de680", name = "Rawhide Backpack", quantity = 1 } },
        }, T0.AddMinutes(1), "Aldric", "Aldric");
        // No f10 at all, and a file envelope beside the row list, as the game writes them
        s.Write(Path.Combine("alpha", "Aldric", "Ledger", "Aldric_Character_2026-10-24.json"),
            "{\"c01\":[" + loot + "," + trade + "],\"c02\":\"2026-10-24T07:34:57-05:00\",\"c03\":\"2026-10-24T19:16:35-05:00\",\"c04\":0,\"c05\":\"Aldric\",\"c06\":1}");
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        LedgerArchiver.Run(db, s.Path);

        Assert.Equal("a small cellar rat", Scalar<string>(db, "SELECT json_extract(detail, '$.d02') FROM ledger_row WHERE act = 'act_13'"));
        Assert.Equal("Rat Tooth", Scalar<string>(db, "SELECT json_extract(detail, '$.d04') FROM ledger_row WHERE act = 'act_13'"));
        Assert.Equal("0,0,6,0", Scalar<string>(db, "SELECT json_extract(detail, '$.currency') FROM ledger_row WHERE act = 'act_20'"));
        Assert.Equal("Rawhide Backpack", Scalar<string>(db, "SELECT json_extract(detail, '$.items[0].name') FROM ledger_row WHERE act = 'act_20'"));
        Assert.Equal("Marguerite", Scalar<string>(db, "SELECT name FROM ledger_row WHERE act = 'act_20'"));
        Assert.Equal("testzone", Scalar<string>(db, "SELECT zone FROM ledger_row WHERE act = 'act_13'"));
        // The raw column is the row with nothing decoded or dropped
        using var raw = JsonDocument.Parse(Scalar<string>(db, "SELECT raw FROM ledger_row WHERE act = 'act_13'"));
        Assert.Equal("act_13", raw.RootElement.GetProperty("f01").GetString());
        Assert.Equal("Aldric", raw.RootElement.GetProperty("f07").GetString());
        Assert.StartsWith("zone_", raw.RootElement.GetProperty("f05").GetString());
        // The file's own fields
        using var envelope = JsonDocument.Parse(Scalar<string>(db, "SELECT envelope FROM ledger_file"));
        Assert.Equal("Aldric", envelope.RootElement.GetProperty("c05").GetString());
        Assert.Equal("2026-10-24T07:34:57-05:00", envelope.RootElement.GetProperty("c02").GetString());
    }

    [Fact]
    public void Times_are_stored_in_utc_so_they_sort_across_time_zones()
    {
        using var s = new Fixture.Scratch();
        var early = "{\"f01\":\"act_13\",\"f02\":\"ref_1\",\"f03\":\"{\\\"d01\\\":1,\\\"d02\\\":\\\"\\\",\\\"d04\\\":\\\"A\\\"}\",\"f04\":\"2026-10-24T07:34:57.2446986-05:00\",\"f05\":\"\",\"f06\":1,\"f07\":\"M\",\"f08\":0,\"f09\":\"M\"}";
        var late = early.Replace("07:34:57.2446986-05:00", "13:00:00.0000000+00:00").Replace("\"A\\\"", "\"B\\\"");
        s.Write(Path.Combine("alpha", "M", "Ledger", "M_Character_2026-10-24.json"), "{\"c01\":[" + early + "," + late + "]}");
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        LedgerArchiver.Run(db, s.Path);

        Assert.Equal("2026-10-24T12:34:57.2446986Z", Scalar<string>(db, "SELECT min(at_utc) FROM ledger_row"));
        Assert.Equal("2026-10-24T13:00:00.0000000Z", Scalar<string>(db, "SELECT max(at_utc) FROM ledger_row"));
    }

    [Fact]
    public void The_act_table_says_what_each_code_means_and_flags_what_is_not_understood()
    {
        using var s = new Fixture.Scratch();
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        Assert.Equal("Item looted", Scalar<string>(db, "SELECT meaning FROM act_code WHERE act = 'act_13'"));
        Assert.Equal("confirmed", Scalar<string>(db, "SELECT certainty FROM act_code WHERE act = 'act_13'"));
        Assert.Equal("unknown", Scalar<string>(db, "SELECT certainty FROM act_code WHERE act = 'act_02'"));
        Assert.Equal(ActCodes.All.Count, Scalar<int>(db, "SELECT count(*) FROM act_code"));
        Assert.Equal(ActCodes.All.Count, ActCodes.All.Select(a => a.Act).Distinct().Count());
    }

    [Fact]
    public void A_missing_game_folder_is_not_an_error()
    {
        using var s = new Fixture.Scratch();
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));

        var result = LedgerArchiver.Run(db, Path.Combine(s.Path, "nowhere"));

        Assert.Equal(0, result.Files);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Nothing_is_ever_removed_by_a_later_pass_over_an_empty_folder()
    {
        using var s = new Fixture.Scratch();
        var path = LedgerFile(s, "alpha", "Aldric", "Aldric_Character_2026-10-24.json", Fixture.Item(T0, "Aldric", "Aldric", "a rat", "Rat Tooth"));
        using var db = new LedgerDb(Path.Combine(s.Path, "ledger.db"));
        LedgerArchiver.Run(db, s.Path);

        Directory.Delete(Path.Combine(s.Path, "alpha"), true);
        LedgerArchiver.Run(db, s.Path);

        Assert.False(File.Exists(path));
        Assert.Equal(1, db.RowCount());
    }
}
