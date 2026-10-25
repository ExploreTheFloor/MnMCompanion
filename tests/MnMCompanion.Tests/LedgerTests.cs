using System.Text.Json;
using MnMCompanion.Ledger;
using static MnMCompanion.Tests.Fixture;

namespace MnMCompanion.Tests;

public class LedgerTextTests
{
    [Fact]
    public void Base64_text_is_decoded_and_plain_values_are_left_alone()
    {
        Assert.Equal("a Dustrend legionary", LedgerText.Decode("YSBEdXN0cmVuZCBsZWdpb25hcnk="));
        Assert.Equal("None", LedgerText.Decode("None"));                              // a word that is valid base64
        Assert.Equal("9a818e77c3484fe98efd9081", LedgerText.Decode("9a818e77c3484fe98efd9081"));   // an item id
        Assert.Equal("Rusty Iron Axe", LedgerText.Decode("Rusty Iron Axe"));
        Assert.Equal("", LedgerText.Decode(""));
    }

    [Fact]
    public void Prefixed_values_lose_their_prefix()
    {
        Assert.Equal("a Dustrend legionary", LedgerText.Prefixed("name_YSBEdXN0cmVuZCBsZWdpb25hcnk="));
        Assert.Equal("valeofzintar", LedgerText.Prefixed("zone_dmFsZW9memludGFy"));
        Assert.Equal("57073", LedgerText.Prefixed("ref_57073"));
    }

    [Fact]
    public void Coin_digits_count_a_hundred_to_the_next_coin()
    {
        Assert.Equal(144, LedgerText.Packed("0,0,1,44"));
        Assert.Equal(1_000_000 + 20_000 + 300 + 4, LedgerText.Packed("1,2,3,4"));
        Assert.Equal(0, LedgerText.Packed(""));
        Assert.Equal(0, LedgerText.Packed("1,2,3"));
        Assert.Equal(0, LedgerText.Packed("a,b,c,d"));
    }

    [Fact]
    public void Coin_words_count_the_same_way()
    {
        Assert.Equal(271, LedgerText.Coin("0 platinum 0 gold 2 silver 71 copper"));
        Assert.Equal(24, LedgerText.Coin("0 platinum 0 gold 0 silver 24 copper"));
        Assert.Equal(10_000 + 5, LedgerText.Coin("0 platinum 1 gold 0 silver 5 copper"));
        Assert.Equal(0, LedgerText.Coin("nothing"));
    }
}

public class LedgerParserTests
{
    private static readonly DateTime T = new(2026, 10, 24, 9, 0, 0);

    [Fact]
    public void A_row_gives_its_fields_and_the_detail_string_is_opened()
    {
        var rows = Parse(File(Item(T, "Brom", "Aldric", "a Dustrend legionary", "Rusty Iron Axe", "ftr", 31)));
        var r = Assert.Single(rows);
        Assert.Equal("act_13", r.Act);
        Assert.Equal("Brom", r.Actor);
        Assert.Equal("Aldric", r.Owner);
        Assert.Equal("ftr", r.Class);
        Assert.Equal(31, r.Level);
        Assert.Equal("testzone", r.Zone);
        Assert.Equal(T, r.Time);
        Assert.Equal("Rusty Iron Axe", r.Get("d04"));
        Assert.Equal("a Dustrend legionary", r.Text("d02"));
        Assert.Equal(1, r.Int("d01"));
        Assert.False(r.ByOwner);
    }

    [Fact]
    public void The_owner_is_known_by_name_and_class_None_means_none()
    {
        var rows = Parse(File(Item(T, "aldric", "Aldric", "a rat", "Rat Ear", "None")));
        Assert.True(rows[0].ByOwner);
        Assert.Equal("", rows[0].Class);
    }

    [Fact]
    public void Any_array_of_rows_is_found_and_other_objects_are_skipped()
    {
        var json = $"{{\"version\":3,\"c07\":[{Item(T, "a", "b", "m", "i")},{{\"x\":1}}]}}";
        Assert.Single(Parse(json));
    }

    [Fact]
    public void A_file_caught_half_written_is_an_error_the_caller_can_retry()
    {
        var json = File(Item(T, "a", "b", "m", "i"), Item(T, "a", "b", "m", "j"));
        Assert.ThrowsAny<JsonException>(() => Parse(json[..^30]));
    }
}

public class LedgerFolderTests
{
    private static readonly DateTime T = new(2026, 10, 24, 9, 0, 0);

    private static string Character(string name, string date) => $"srv\\{name}\\Ledger\\{name}_Character_{date}.json";
    private static string Social(string name, string date) => $"srv\\{name}\\Ledger\\{name}_Social_{date}.json";

    [Fact]
    public void Dated_files_and_the_archive_are_both_read()
    {
        using var s = new Scratch();
        s.Write(Social("Aldric", "2026-10-24"), File(Item(T.AddMinutes(10), "Brom", "Aldric", "a rat", "Rat Ear")));
        s.Write("srv\\Aldric\\Ledger\\Archive\\ledger_Aldric_Social_archive_20261024_100000.json", File(Item(T, "Doc", "Aldric", "a rat", "Rat Tail")));

        var folder = new LedgerFolder(Path.Combine(s.Path, "srv", "Aldric"));
        Assert.True(folder.Refresh());
        Assert.Equal(["Doc", "Brom"], folder.Rows.Select(r => r.Actor));    // oldest first, archive row included
        Assert.Equal(2, folder.FileCount);
    }

    [Fact]
    public void A_row_in_the_archive_and_a_dated_file_counts_once_but_repeats_inside_one_file_all_count()
    {
        using var s = new Scratch();
        var dup = Item(T, "Brom", "Aldric", "a rat", "Rat Ear");
        s.Write(Social("Aldric", "2026-10-24"), File(dup, Item(T.AddMinutes(1), "Brom", "Aldric", "a rat", "Rat Ear"), Item(T.AddMinutes(1), "Brom", "Aldric", "a rat", "Rat Ear")));
        s.Write("srv\\Aldric\\Ledger\\Archive\\ledger_Aldric_Social_archive_20261024_100000.json", File(dup));

        var folder = new LedgerFolder(Path.Combine(s.Path, "srv", "Aldric"));
        folder.Refresh();
        Assert.Equal(3, folder.Rows.Count);
    }

    [Fact]
    public void Only_a_changed_file_is_read_again_and_growth_is_picked_up()
    {
        using var s = new Scratch();
        var path = s.Write(Social("Aldric", "2026-10-24"), File(Item(T, "Brom", "Aldric", "a rat", "Rat Ear")));
        var folder = new LedgerFolder(Path.Combine(s.Path, "srv", "Aldric"));
        Assert.True(folder.Refresh());
        var v = folder.Version;
        Assert.False(folder.Refresh());
        Assert.Equal(v, folder.Version);

        System.IO.File.WriteAllText(path, File(Item(T, "Brom", "Aldric", "a rat", "Rat Ear"), Item(T.AddSeconds(30), "Brom", "Aldric", "a rat", "Rat Tail")));
        System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        Assert.True(folder.Refresh());
        Assert.Equal(2, folder.Rows.Count);
        Assert.True(folder.Version > v);
    }

    [Fact]
    public void A_file_caught_half_written_keeps_what_was_read_and_is_tried_again()
    {
        using var s = new Scratch();
        var good = File(Item(T, "Brom", "Aldric", "a rat", "Rat Ear"));
        var path = s.Write(Social("Aldric", "2026-10-24"), good);
        var folder = new LedgerFolder(Path.Combine(s.Path, "srv", "Aldric"));
        folder.Refresh();

        var more = File(Item(T, "Brom", "Aldric", "a rat", "Rat Ear"), Item(T.AddSeconds(30), "Brom", "Aldric", "a rat", "Rat Tail"));
        System.IO.File.WriteAllText(path, more[..^20]);
        System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        Assert.False(folder.Refresh());
        Assert.Single(folder.Rows);
        Assert.NotNull(folder.Error);

        System.IO.File.WriteAllText(path, more);
        System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
        Assert.True(folder.Refresh());
        Assert.Equal(2, folder.Rows.Count);
        Assert.Null(folder.Error);
    }

    [Fact]
    public void A_file_the_game_has_open_can_still_be_read()
    {
        using var s = new Scratch();
        var path = s.Write(Social("Aldric", "2026-10-24"), File(Item(T, "Brom", "Aldric", "a rat", "Rat Ear")));
        using var game = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Write);
        var folder = new LedgerFolder(Path.Combine(s.Path, "srv", "Aldric"));
        Assert.True(folder.Refresh());
        Assert.Single(folder.Rows);
    }

    [Fact]
    public void Characters_only_reads_just_the_character_files()
    {
        using var s = new Scratch();
        s.Write(Character("Aldric", "2026-10-24"), File(Sale(T, "Aldric", "Rat Ear", 1, "0 platinum 0 gold 0 silver 3 copper")));
        s.Write(Social("Aldric", "2026-10-24"), File(Item(T, "Brom", "Aldric", "a rat", "Rat Ear")));
        var folder = new LedgerFolder(Path.Combine(s.Path, "srv", "Aldric"), characterFilesOnly: true);
        folder.Refresh();
        Assert.Equal(["act_24"], folder.Rows.Select(r => r.Act));
    }

    [Fact]
    public void Characters_are_found_by_server_most_recent_first()
    {
        using var s = new Scratch();
        s.Write(Character("Old", "2026-10-01"), File(Sale(T, "Old", "Rat Ear", 1, "0 platinum 0 gold 0 silver 3 copper")));
        s.Write("other\\Newer\\Ledger\\Newer_Character_2026-10-24.json", File(Sale(T, "Newer", "Rat Ear", 1, "0 platinum 0 gold 0 silver 3 copper")));
        System.IO.File.SetLastWriteTime(Path.Combine(s.Path, "srv", "Old", "Ledger", "Old_Character_2026-10-01.json"), T.AddDays(-20));
        System.IO.File.SetLastWriteTime(Path.Combine(s.Path, "other", "Newer", "Ledger", "Newer_Character_2026-10-24.json"), T);
        Directory.CreateDirectory(Path.Combine(s.Path, "srv", "NoLedger"));

        var found = LedgerLocator.Find(s.Path);
        Assert.Equal(["Newer", "Old"], found.Select(f => f.Character));
        Assert.Equal(["other", "srv"], found.Select(f => f.Server));
    }
}
