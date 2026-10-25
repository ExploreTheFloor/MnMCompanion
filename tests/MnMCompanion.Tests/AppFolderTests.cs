using Scratch = MnMCompanion.Tests.Fixture.Scratch;

namespace MnMCompanion.Tests;

/// <summary>The data folder changed name with the rename from Loot Goblin; nothing the player already has may be lost or split in two.</summary>
public class AppFolderTests
{
    [Fact]
    public void A_fresh_machine_gets_the_new_folder_and_nothing_else_is_made()
    {
        using var root = new Scratch();
        Assert.Equal(Path.Combine(root.Path, "MnMCompanion"), AppFolder.Resolve(root.Path));
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [Fact]
    public void The_old_folder_moves_across_whole()
    {
        using var root = new Scratch();
        root.Write(@"LootWindow\settings.json", "{\"Character\":\"Aldric\"}");
        root.Write(@"LootWindow\ledger.db", "database");
        root.Write(@"LootWindow\alpha-Aldric\day.json", "rows");

        var folder = AppFolder.Resolve(root.Path);

        Assert.Equal(Path.Combine(root.Path, "MnMCompanion"), folder);
        Assert.Equal("{\"Character\":\"Aldric\"}", File.ReadAllText(Path.Combine(folder, "settings.json")));
        Assert.Equal("database", File.ReadAllText(Path.Combine(folder, "ledger.db")));
        Assert.Equal("rows", File.ReadAllText(Path.Combine(folder, "alpha-Aldric", "day.json")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "LootWindow")));
    }

    [Fact]
    public void A_second_ask_finds_the_same_folder_and_moves_nothing_more()
    {
        using var root = new Scratch();
        root.Write(@"LootWindow\ledger.db", "database");
        var first = AppFolder.Resolve(root.Path);
        var second = AppFolder.Resolve(root.Path);
        Assert.Equal(first, second);
        Assert.Equal("database", File.ReadAllText(Path.Combine(second, "ledger.db")));
    }

    [Fact]
    public void When_both_folders_exist_the_new_one_is_used_and_the_old_one_is_left_alone()
    {
        using var root = new Scratch();
        root.Write(@"LootWindow\ledger.db", "old");
        root.Write(@"MnMCompanion\ledger.db", "new");

        var folder = AppFolder.Resolve(root.Path);

        Assert.Equal(Path.Combine(root.Path, "MnMCompanion"), folder);
        Assert.Equal("new", File.ReadAllText(Path.Combine(folder, "ledger.db")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root.Path, "LootWindow", "ledger.db")));
    }

    [Fact]
    public void A_file_still_open_keeps_the_old_folder_in_use_instead_of_starting_an_empty_one()
    {
        using var root = new Scratch();
        var db = root.Write(@"LootWindow\ledger.db", "database");
        using var held = new FileStream(db, FileMode.Open, FileAccess.Read, FileShare.None);

        var folder = AppFolder.Resolve(root.Path);

        Assert.Equal(Path.Combine(root.Path, "LootWindow"), folder);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "MnMCompanion")));
    }
}
