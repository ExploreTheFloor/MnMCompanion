namespace MnMCompanion.Tests;

/// <summary>
/// The rule this app is built on: its only inputs are the game's ledger files and each character's journal folder (what NPCs said to them). These tests read the project's own sources and
/// fail if anything reaches for another source: someone else's database, the network, the game's process or memory, or the
/// Memento code. Two exceptions are made on purpose, each confined to its own project: <c>src\MnMCompanion.Data</c> keeps this app's
/// OWN database of the rows it has read (the only project that may use SQLite, and the only package, Microsoft.Data.Sqlite), and
/// <c>src\MnMCompanion.Update</c> asks GitHub whether a newer version of this app exists (the only project that may use the network,
/// and only github.com and api.github.com). A third is the map: the app itself carries one package, the system's web view, to show the
/// community atlas's map of the zone the ledger says the character is in, only once the player has chosen to (Settings.MapsOnline); the atlas's
/// address lives in the update project like the others. A fourth is the Recipes page: the community wiki's public tradeskills data is fetched (one
/// GET, by the update project's RecipeWikiClient) when the page is first shown and about weekly, unless the player switched it off (Settings.RecipesOnline),
/// and kept on this PC. Everything else,
/// the Core included, stays free of all of them. (The community data is to live in an open repository on GitHub, and reading or sharing it is where
/// this list will make its next exception.)
/// </summary>
public class LedgerOnlyTests
{
    private static readonly string[] Forbidden =
    [
        "Memento", "Sqlite", "world.db", "observations.db", "System.Data",
        "HttpClient", "System.Net", "WebRequest", "WebClient", "Socket",
        "OpenProcess", "ReadProcessMemory", "GetProcessesByName", "Process.GetProcess", "MemoryMappedFile", "NamedPipe",
        "SendInput", "SetCursorPos", "keybd_event", "mouse_event", "Interception",
    ];

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MnMCompanion.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("MnMCompanion.sln not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml") || f.EndsWith(".csproj"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>The project that keeps the app's own database.</summary>
    private static bool InData(string file) => file.Contains($"{Path.DirectorySeparatorChar}MnMCompanion.Data{Path.DirectorySeparatorChar}");

    /// <summary>The project that looks for a newer version on GitHub.</summary>
    private static bool InUpdate(string file) => file.Contains($"{Path.DirectorySeparatorChar}MnMCompanion.Update{Path.DirectorySeparatorChar}");

    /// <summary>The app itself, which shows the community atlas's map of the zone in a web view (the only package it has).</summary>
    private static bool InApp(string file) => file.EndsWith($"{Path.DirectorySeparatorChar}MnMCompanion{Path.DirectorySeparatorChar}MnMCompanion.csproj");

    /// <summary>What the data project may mention that the rest may not.</summary>
    private static readonly string[] DatabaseWords = ["Sqlite"];

    /// <summary>What the update project may mention that the rest may not.</summary>
    private static readonly string[] NetworkWords = ["HttpClient", "System.Net"];

    [Fact]
    public void No_source_file_reaches_for_anything_but_the_ledger()
    {
        var found = new List<string>();
        foreach (var file in Sources())
        {
            var text = File.ReadAllText(file);
            foreach (var word in Forbidden)
            {
                if (InData(file) && DatabaseWords.Contains(word)) continue;
                if (InUpdate(file) && NetworkWords.Contains(word)) continue;
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase)) found.Add($"{Path.GetRelativePath(Root(), file)}: {word}");
            }
        }
        Assert.Empty(found);
    }

    [Fact]
    public void Only_the_data_project_and_the_app_have_packages_sqlite_and_the_web_view_alone()
    {
        foreach (var project in Sources().Where(f => f.EndsWith(".csproj")))
        {
            var text = File.ReadAllText(project);
            var packages = text.Split('\n').Where(l => l.Contains("PackageReference")).ToList();
            if (InData(project)) Assert.All(packages, l => Assert.Contains("Microsoft.Data.Sqlite", l));
            else if (InApp(project)) Assert.All(packages, l => Assert.Contains("Microsoft.Web.WebView2", l));
            else Assert.Empty(packages);
            foreach (var line in text.Split('\n').Where(l => l.Contains("ProjectReference")))
                Assert.True(line.Contains("MnMCompanion.Core") || line.Contains("MnMCompanion.Data") || line.Contains("MnMCompanion.Update"), line);
        }
    }

    [Fact]
    public void The_core_reaches_neither_the_data_nor_the_update_project_and_they_do_not_reach_each_other()
    {
        string Project(string name) => File.ReadAllText(Sources().Single(f => f.EndsWith(name)));
        Assert.DoesNotContain("MnMCompanion.Data", Project("MnMCompanion.Core.csproj"));
        Assert.DoesNotContain("MnMCompanion.Update", Project("MnMCompanion.Core.csproj"));
        Assert.DoesNotContain("MnMCompanion.Update", Project("MnMCompanion.Data.csproj"));
        Assert.DoesNotContain("MnMCompanion.Data", Project("MnMCompanion.Update.csproj"));
    }

    [Fact]
    public void The_update_project_uses_the_base_class_library_alone()
    {
        var references = typeof(MnMCompanion.Update.UpdateService).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");
        Assert.All(references, name => Assert.True(name.StartsWith("System") || name is "netstandard" or "mscorlib", $"unexpected reference: {name}"));
    }

    [Fact]
    public void The_only_addresses_in_the_source_are_githubs_the_atlas_and_the_community_service_and_only_the_update_project_has_them()
    {
        var allowed = new[] { "api.github.com", "github.com", "www.mnmatlas.com", "mnm-intake.mnm-companion.workers.dev", "monstersandmemories.wiki" };
        var urls = new System.Text.RegularExpressions.Regex("https?://[^\\s\"'<>{}/\\\\]+");
        foreach (var file in Sources().Where(f => f.EndsWith(".cs")))
            foreach (System.Text.RegularExpressions.Match m in urls.Matches(File.ReadAllText(file)))
            {
                var where = Path.GetRelativePath(Root(), file);
                Assert.True(InUpdate(file), $"{where}: an address outside the update project: {m.Value}");
                Assert.StartsWith("https://", m.Value);
                Assert.Contains(m.Value["https://".Length..], allowed);
            }
    }

    [Fact]
    public void The_data_project_uses_the_base_class_library_and_sqlite_only()
    {
        var references = typeof(MnMCompanion.Data.LedgerDb).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");
        Assert.All(references, name => Assert.True(
            name.StartsWith("System") || name.StartsWith("Microsoft.Data.Sqlite") || name is "netstandard" or "mscorlib" or "MnMCompanion.Core",
            $"unexpected reference: {name}"));
    }

    [Fact]
    public void The_core_only_uses_the_base_class_library()
    {
        var references = typeof(MnMCompanion.Loot.LootStore).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");
        Assert.All(references, name => Assert.True(name.StartsWith("System") || name is "netstandard" or "mscorlib", $"unexpected reference: {name}"));
    }
}
