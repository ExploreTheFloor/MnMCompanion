using System.Text;
using System.Text.RegularExpressions;

namespace MnMCompanion.Ledger;

/// <summary>A game data folder found in a Wine or Proton prefix: where it is, how many characters it holds, when one was last played, and which prefix it is in.</summary>
public sealed record FoundGameFolder(string Root, int Characters, DateTime LastActive, string Prefix);

/// <summary>
/// Finding the game's data when this program runs under Wine or Proton (a Linux player). The game keeps its data in <c>AppData\LocalLow\...</c> inside ITS
/// Wine prefix, and a program started as another Steam non-Steam game gets a prefix of its own, so the usual place is empty. Wine shows the Linux
/// filesystem as the <c>Z:</c> drive, so the other prefixes can be looked through from here: Steam's (<c>compatdata\&lt;number&gt;\pfx</c>, in the
/// Steam folder, its Flatpak and any other Steam library), UMU's (<c>Games\umu\*</c>), Heroic's (<c>Games\Heroic\Prefixes\*</c>), Bottles's
/// (<c>.local\share\bottles\bottles\*</c> and its Flatpak), Lutris's (<c>Games\*</c>) and the plain <c>.wine</c>. Nothing is
/// ever written; a folder counts only if characters with a ledger or the game's usual files are in it.
/// </summary>
public static partial class GameFolderFinder
{
    private static readonly string[] GamePath = ["AppData", "LocalLow", "Niche Worlds Cult", "Monsters and Memories"];

    private static readonly string[] SteamFolders =
    [
        ".local/share/Steam", ".steam/steam", ".steam/root", ".var/app/com.valvesoftware.Steam/.local/share/Steam", ".var/app/com.valvesoftware.Steam/data/Steam",
    ];

    // Heroic keeps a prefix per game in Prefixes/default (older versions straight in Prefixes); Bottles keeps one per bottle, also as a Flatpak
    private static readonly string[] PrefixFolders =
    [
        "Games/Heroic/Prefixes", "Games/Heroic/Prefixes/default", ".local/share/bottles/bottles", ".var/app/com.usebottles.bottles/data/bottles/bottles",
    ];

    /// <summary>
    /// Whether this program runs under Wine (Proton is Wine): they put these in the environment, and Wine's <c>Z:</c> drive is the Linux filesystem,
    /// <c>/proc</c> included. A Windows PC has none of them.
    /// </summary>
    public static bool RunningUnderWine(Func<string, string?>? env = null, Func<string, bool>? fileExists = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        fileExists ??= File.Exists;
        return new[] { "WINEPREFIX", "STEAM_COMPAT_DATA_PATH", "PROTON_VERSION", "WINELOADERNOEXEC" }.Any(name => !string.IsNullOrEmpty(env(name))) || fileExists(@"Z:\proc\version");
    }

    /// <summary>The Linux filesystem as Wine shows it (<c>Z:\</c>) when it has a home folder in it; null otherwise.</summary>
    public static string? LinuxRoot() => Directory.Exists(@"Z:\home") || Directory.Exists(@"Z:\var\home") ? @"Z:\" : null;

    /// <summary>
    /// Every game data folder under <paramref name="linuxRoot"/> that holds at least one character, the most recently played first. The same folder
    /// reached twice (Steam links <c>.steam/steam</c> to <c>.local/share/Steam</c>) is listed once.
    /// </summary>
    public static IReadOnlyList<FoundGameFolder> Find(string linuxRoot)
    {
        var found = new List<FoundGameFolder>();
        var seen = new HashSet<string>();
        foreach (var prefix in Prefixes(linuxRoot))
            foreach (var user in Dirs(Path.Combine(prefix, "drive_c", "users")))
            {
                if (Path.GetFileName(user).Equals("Public", StringComparison.OrdinalIgnoreCase)) continue;
                var root = GamePath.Aggregate(user, Path.Combine);
                var sources = LedgerLocator.Find(root);
                if (sources.Count == 0) continue;
                var last = sources.Max(s => s.LastActive);
                // the same folder through a link: same characters, same last play, same names
                var signature = $"{sources.Count}|{last.Ticks}|{string.Join(',', sources.Select(s => s.Server + "/" + s.Character).Order())}";
                if (seen.Add(signature)) found.Add(new FoundGameFolder(root, sources.Count, last, prefix));
            }
        return found.OrderByDescending(f => f.LastActive).ThenByDescending(f => f.Characters).ToList();
    }

    /// <summary>
    /// The folder to read instead of the one set up, or null to leave things as they are: only when the folder in use has no characters (the one chosen in settings, else the
    /// usual place), this runs under Wine or Proton, and <paramref name="bestFound"/> finds one. What already works is never replaced.
    /// </summary>
    public static string? Choose(string? chosenRoot, Func<string, int> charactersIn, int charactersAtDefault, bool underWine, Func<string?> bestFound)
    {
        if (chosenRoot is not null ? charactersIn(chosenRoot) > 0 : charactersAtDefault > 0) return null;
        return underWine ? bestFound() : null;
    }

    /// <summary>The folder most recently played in, or null if none was found.</summary>
    public static FoundGameFolder? Best(string linuxRoot) => Find(linuxRoot).FirstOrDefault();

    /// <summary>The Wine prefixes there might be under the Linux root, whether or not they exist (the callers look inside).</summary>
    public static IEnumerable<string> Prefixes(string linuxRoot)
    {
        foreach (var home in Dirs(Path.Combine(linuxRoot, "home")).Concat(Dirs(Path.Combine(linuxRoot, "var", "home"))))
        {
            foreach (var steam in SteamFolders.Select(f => InHome(home, f)).Where(Directory.Exists))
                foreach (var library in Libraries(linuxRoot, steam))
                    foreach (var app in Dirs(Path.Combine(library, "steamapps", "compatdata")))
                        yield return Path.Combine(app, "pfx");
            foreach (var umu in Dirs(InHome(home, "Games/umu"))) yield return umu;
            foreach (var prefix in PrefixFolders.SelectMany(f => Dirs(InHome(home, f)))) yield return prefix;
            foreach (var game in Dirs(InHome(home, "Games")))
            {
                yield return game;                              // Lutris keeps a prefix per game
                yield return Path.Combine(game, "prefix");
            }
            yield return InHome(home, ".wine");
        }
    }

    /// <summary>A Steam folder and the other Steam libraries it lists (an SD card, another disk), as paths under the Linux root.</summary>
    private static IEnumerable<string> Libraries(string linuxRoot, string steam)
    {
        yield return steam;
        string text;
        try { text = File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (Match m in LibraryPath().Matches(text))
        {
            var path = m.Groups[1].Value.Replace(@"\\", "/").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (path.Length > 0) yield return Path.Combine(linuxRoot, path);
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPath();

    private static string InHome(string home, string relative) => Path.Combine([home, .. relative.Split('/')]);

    private static IEnumerable<string> Dirs(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path).ToList() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    // ---------------------------------------------------------------- a report that is safe to paste

    /// <summary>
    /// Hides the user name in a path (<c>/home/deck</c>, <c>C:\users\steamuser</c>, <c>/run/media/name</c>) so a report can be posted. Nothing else in a path is personal.
    /// </summary>
    public static string Mask(string text) => UserSegment().Replace(text, "<user>");

    [GeneratedRegex(@"(?<=[/\\](?:home|users|media)[/\\])[^/\\]+", RegexOptions.IgnoreCase)]
    private static partial Regex UserSegment();

    /// <summary>What a tester posts: what this program sees of its surroundings, in plain lines, with counts but no names.</summary>
    public static string Report(DiagnoseFacts f)
    {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.AppendLine(Mask(text));
        Line($"Companion {f.Version}");
        Line($"system: {f.System}; runtime: {f.Runtime}");
        Line($"looks like Wine or Proton: {(f.UnderWine ? "yes" : "no")} ({f.WineSigns})");
        foreach (var (name, value) in f.Environment) Line($"  {name} = {value ?? "(not set)"}");
        Line($"Linux filesystem (Z:): {(f.LinuxRoot is null ? "not visible" : "visible")}");
        Line();
        Line($"game data folder in use: {f.RootInUse}");
        Line($"  default place ({f.DefaultRoot}): {Characters(f.DefaultCharacters)}");
        Line($"  chosen in settings: {(f.ChosenRoot is null ? "nothing" : f.ChosenRoot + ": " + Characters(f.ChosenCharacters))}");
        Line($"  found by looking through other prefixes: {f.Found.Count}");
        foreach (var found in f.Found) Line($"    {found.Root}  ({Characters(found.Characters)}, last played {found.LastActive:yyyy-MM-dd HH:mm}; prefix {found.Prefix})");
        Line();
        Line($"drawing a test window: {f.WindowTest}");
        Line($"the map's web view: {f.WebView}");
        Line($"Companion's own files: {f.OwnFolder}");
        return sb.ToString();
    }

    private static string Characters(int n) => n switch { 0 => "no characters", 1 => "1 character", _ => $"{n} characters" };
}

/// <summary>What <see cref="GameFolderFinder.Report"/> says, gathered by the program.</summary>
public sealed record DiagnoseFacts(
    string Version, string System, string Runtime, bool UnderWine, string WineSigns, IReadOnlyList<(string Name, string? Value)> Environment, string? LinuxRoot,
    string RootInUse, string DefaultRoot, int DefaultCharacters, string? ChosenRoot, int ChosenCharacters, IReadOnlyList<FoundGameFolder> Found,
    string WindowTest, string WebView, string OwnFolder);
