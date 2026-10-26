using MnMCompanion.Ledger;

namespace MnMCompanion.Tests;

/// <summary>
/// Finding the game's data from inside Wine or Proton, where the usual place is empty because the game lives in another prefix. The Linux filesystem is a
/// scratch folder here, laid out the way Steam, UMU and Lutris lay theirs out.
/// </summary>
public class GameFolderFinderTests
{
    private static readonly DateTime Newer = new(2026, 10, 26, 18, 0, 0), Older = new(2026, 10, 20, 9, 0, 0);

    /// <summary>A prefix's game data with one character holding one ledger file written at <paramref name="when"/>; returns the game data folder.</summary>
    private static string Game(Fixture.Scratch s, string prefixUnderRoot, string character = "Aldric", DateTime? when = null, string user = "steamuser", string server = "alpha")
    {
        var root = Path.Combine([s.Path, .. prefixUnderRoot.Split('/'), "drive_c", "users", user, "AppData", "LocalLow", "Niche Worlds Cult", "Monsters and Memories"]);
        var file = s.Write(Path.GetRelativePath(s.Path, Path.Combine(root, server, character, "Ledger", $"{character}_Character_2026-10-26.json")), "{}");
        File.SetLastWriteTime(file, when ?? Newer);
        return root;
    }

    [Fact]
    public void A_Steam_non_Steam_games_prefix_is_found_through_the_Linux_filesystem()
    {
        using var s = new Fixture.Scratch();
        var expected = Game(s, "home/deck/.local/share/Steam/steamapps/compatdata/3123456789/pfx");

        var found = GameFolderFinder.Find(s.Path);

        var one = Assert.Single(found);
        Assert.Equal(expected, one.Root);
        Assert.Equal(1, one.Characters);
        Assert.Equal(Newer, one.LastActive);
    }

    [Fact]
    public void Every_usual_kind_of_prefix_is_looked_in()
    {
        using var s = new Fixture.Scratch();
        var places = new[]
        {
            "home/a/.local/share/Steam/steamapps/compatdata/1/pfx",                                 // Steam
            "home/b/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/2/pfx", // Steam as a Flatpak
            "home/c/Games/umu/mnm",                                                                  // the launcher's UMU prefix
            "home/d/Games/monsters-memories",                                                        // Lutris, one folder a game
            "home/e/Games/monsters-memories-2/prefix",
            "home/f/.wine",
            "var/home/g/Games/umu/mnm",                                                              // Fedora-style atomic desktops keep homes in /var/home
        };
        foreach (var place in places) Game(s, place, character: "Char" + place.Length);

        var found = GameFolderFinder.Find(s.Path);

        Assert.Equal(places.Length, found.Count);
    }

    [Fact]
    public void Another_Steam_library_such_as_an_SD_card_is_found_through_the_list_Steam_keeps()
    {
        using var s = new Fixture.Scratch();
        s.Write("home/deck/.local/share/Steam/steamapps/libraryfolders.vdf", """
            "libraryfolders"
            {
                "0" { "path" "/home/deck/.local/share/Steam" }
                "1" { "path" "/run/media/mmcblk0p1" }
            }
            """);
        var card = Game(s, "run/media/mmcblk0p1/steamapps/compatdata/987/pfx");

        var found = GameFolderFinder.Find(s.Path);

        Assert.Equal(card, Assert.Single(found).Root);
    }

    [Fact]
    public void The_one_played_most_recently_comes_first_and_the_same_folder_through_a_link_is_listed_once()
    {
        using var s = new Fixture.Scratch();
        var umu = Game(s, "home/deck/Games/umu/mnm", when: Older);
        var steam = Game(s, "home/deck/.local/share/Steam/steamapps/compatdata/555/pfx", when: Newer);
        Game(s, "home/deck/.steam/steam/steamapps/compatdata/555/pfx", when: Newer);   // .steam/steam is a link to the Steam folder: the same data again

        var found = GameFolderFinder.Find(s.Path);

        Assert.Equal([steam, umu], found.Select(f => f.Root));
        Assert.Equal(GameFolderFinder.Find(s.Path)[0], GameFolderFinder.Best(s.Path));
    }

    [Fact]
    public void Prefixes_without_a_character_the_public_user_and_nothing_at_all_give_nothing()
    {
        using var s = new Fixture.Scratch();
        Assert.Empty(GameFolderFinder.Find(s.Path));                                   // an empty filesystem
        Assert.Empty(GameFolderFinder.Find(Path.Combine(s.Path, "nowhere")));          // one that isn't there
        Assert.Null(GameFolderFinder.Best(s.Path));

        Game(s, "home/deck/.local/share/Steam/steamapps/compatdata/1/pfx", user: "Public");   // not a person's folder
        Directory.CreateDirectory(Path.Combine(s.Path, "home", "deck", "Games", "umu", "other", "drive_c", "users", "steamuser", "AppData", "LocalLow", "Other Studio", "Other Game"));
        Directory.CreateDirectory(Path.Combine(s.Path, "home", "deck", ".wine", "drive_c", "users", "deck"));   // a prefix that never ran the game

        Assert.Empty(GameFolderFinder.Find(s.Path));
    }

    [Fact]
    public void Running_under_Wine_is_told_by_what_Wine_and_Proton_leave_in_the_environment_or_by_the_Linux_proc_folder()
    {
        Func<string, string?> none = _ => null;
        Assert.False(GameFolderFinder.RunningUnderWine(none, _ => false));
        Assert.True(GameFolderFinder.RunningUnderWine(n => n == "WINEPREFIX" ? "/home/deck/pfx" : null, _ => false));
        Assert.True(GameFolderFinder.RunningUnderWine(n => n == "STEAM_COMPAT_DATA_PATH" ? "/x" : null, _ => false));
        Assert.True(GameFolderFinder.RunningUnderWine(n => n == "PROTON_VERSION" ? "GE-Proton9-5" : null, _ => false));
        Assert.True(GameFolderFinder.RunningUnderWine(none, path => path == @"Z:\proc\version"));
        Assert.False(GameFolderFinder.RunningUnderWine(n => n == "WINEPREFIX" ? "" : null, _ => false), "an empty variable is not a sign");
    }

    [Fact]
    public void A_folder_that_works_is_never_replaced_and_the_search_is_only_for_Wine_when_nothing_is_there()
    {
        string? Never() => throw new InvalidOperationException("nothing should have been looked for");
        string? Found() => "found-folder";

        // the usual place has the game: leave it
        Assert.Null(GameFolderFinder.Choose(null, _ => 0, charactersAtDefault: 2, underWine: true, Never));
        // a folder chosen in settings that works: leave it, even if the usual place is empty
        Assert.Null(GameFolderFinder.Choose("chosen", r => r == "chosen" ? 3 : 0, charactersAtDefault: 0, underWine: true, Never));
        // nothing works, and this is Wine: use what the search finds
        Assert.Equal("found-folder", GameFolderFinder.Choose(null, _ => 0, 0, underWine: true, Found));
        Assert.Equal("found-folder", GameFolderFinder.Choose("gone", _ => 0, 0, underWine: true, Found));   // a chosen folder that no longer has the game
        // nothing works and this is a Windows PC: nothing to do
        Assert.Null(GameFolderFinder.Choose(null, _ => 0, 0, underWine: false, Never));
        // Wine, and the search finds nothing
        Assert.Null(GameFolderFinder.Choose(null, _ => 0, 0, underWine: true, () => null));
    }

    [Theory]
    [InlineData(@"Z:\home\deck\.local\share\Steam", @"Z:\home\<user>\.local\share\Steam")]
    [InlineData("/home/deck/Games/umu/mnm", "/home/<user>/Games/umu/mnm")]
    [InlineData("/var/home/someone/Games", "/var/home/<user>/Games")]
    [InlineData("/run/media/mmcblk0p1/steamapps", "/run/media/<user>/steamapps")]
    [InlineData(@"C:\users\steamuser\AppData\LocalLow", @"C:\users\<user>\AppData\LocalLow")]
    [InlineData("nothing personal here", "nothing personal here")]
    public void A_user_name_in_a_path_is_hidden_and_nothing_else(string path, string masked) => Assert.Equal(masked, GameFolderFinder.Mask(path));

    [Fact]
    public void The_report_says_what_was_found_by_counts_and_hides_the_home_folder_and_leaves_names_out()
    {
        var found = new FoundGameFolder(@"Z:\home\deck\.local\share\Steam\steamapps\compatdata\123\pfx\drive_c\users\steamuser\AppData\LocalLow\Niche Worlds Cult\Monsters and Memories", 3, Newer,
            @"Z:\home\deck\.local\share\Steam\steamapps\compatdata\123\pfx");
        var facts = new DiagnoseFacts("0.3.2", "Microsoft Windows 10.0.19045", ".NET 10.0.0", true, "WINEPREFIX set", [("WINEPREFIX", "/home/deck/pfx"), ("PROTON_VERSION", null)], @"Z:\",
            found.Root, @"C:\users\steamuser\AppData\LocalLow\Niche Worlds Cult\Monsters and Memories", 0, null, 0, [found], "a window could be shown and drawn", "not installed", @"C:\users\steamuser\AppData\Local\MnMCompanion");

        var report = GameFolderFinder.Report(facts);

        Assert.DoesNotContain("deck", report.Replace("<user>", ""));
        Assert.DoesNotContain("steamuser", report.Replace("<user>", ""));
        Assert.Contains("3 characters", report);
        Assert.Contains("looks like Wine or Proton: yes", report);
        Assert.Contains("PROTON_VERSION = (not set)", report);
        Assert.Contains("a window could be shown and drawn", report);
        Assert.DoesNotContain("Aldric", report);
    }
}
