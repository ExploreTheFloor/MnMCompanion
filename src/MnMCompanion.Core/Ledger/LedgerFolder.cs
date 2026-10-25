using System.Text;
using System.Text.Json;

namespace MnMCompanion.Ledger;

/// <summary>A character whose ledger is on this PC: <c>…\Monsters and Memories\&lt;server&gt;\&lt;character&gt;</c>.</summary>
public sealed record LedgerSource(string Server, string Character, string Directory, DateTime LastActive)
{
    public string LedgerDirectory => Path.Combine(Directory, "Ledger");
    public string Label => $"{Character} · {Server}";
}

public static class LedgerLocator
{
    /// <summary>Where the game keeps its per-character data: <c>%USERPROFILE%\AppData\LocalLow\Niche Worlds Cult\Monsters and Memories</c>.</summary>
    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Niche Worlds Cult", "Monsters and Memories");

    /// <summary>Every <c>&lt;server&gt;\&lt;character&gt;</c> under <paramref name="root"/> that has a ledger, the one played most recently first.</summary>
    public static IReadOnlyList<LedgerSource> Find(string? root = null)
    {
        root ??= DefaultRoot();
        var found = new List<LedgerSource>();
        try
        {
            if (!Directory.Exists(root)) return found;
            foreach (var server in Directory.EnumerateDirectories(root))
                foreach (var character in Directory.EnumerateDirectories(server))
                {
                    var ledger = Path.Combine(character, "Ledger");
                    if (!Directory.Exists(ledger)) continue;
                    var files = new DirectoryInfo(ledger).EnumerateFiles("*.json").ToList();
                    if (files.Count == 0) continue;
                    found.Add(new LedgerSource(Path.GetFileName(server), Path.GetFileName(character), character, files.Max(f => f.LastWriteTime)));
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return found.OrderByDescending(s => s.LastActive).ThenBy(s => s.Character, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The servers among <paramref name="sources"/> (the game's server folders), the one played most recently first.</summary>
    public static IReadOnlyList<string> Servers(IEnumerable<LedgerSource> sources) =>
        sources.GroupBy(s => s.Server, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(s => s.LastActive)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First().Server).ToList();

    /// <summary>The characters on one server, the one played most recently first.</summary>
    public static IReadOnlyList<LedgerSource> On(IEnumerable<LedgerSource> sources, string server) =>
        sources.Where(s => string.Equals(s.Server, server, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.LastActive).ThenBy(s => s.Character, StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>
/// One character's ledger, read the way the game writes it: <c>Ledger\&lt;name&gt;_Character_&lt;date&gt;.json</c> and
/// <c>_Social_</c> files, one a day, which the game rewrites as it goes, and <c>Ledger\Archive\*.json</c>, where it moves the
/// oldest 500 rows of the Social file from time to time (those rows are in no dated file, so skipping the folder loses about
/// half of what other players looted). A file is parsed again only when its size or write time changed. A row that appears in
/// two files (the archive repeats a few of the dated file's) counts once; rows repeated inside one file all count.
/// Not thread-safe: use from one thread at a time.
/// </summary>
public sealed class LedgerFolder(string characterDirectory, bool characterFilesOnly = false)
{
    private sealed record Cached(DateTime Written, long Length, List<LedgerRow> Rows);

    private readonly Dictionary<string, Cached> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _problems = [];

    public string Directory { get; } = characterDirectory;
    public string LedgerDirectory => Path.Combine(Directory, "Ledger");
    public string Character => Path.GetFileName(Directory.TrimEnd('\\', '/'));

    /// <summary>Every row, oldest first.</summary>
    public IReadOnlyList<LedgerRow> Rows { get; private set; } = [];

    /// <summary>Changes by one whenever <see cref="Rows"/> does.</summary>
    public long Version { get; private set; }

    public int FileCount => _files.Count;

    /// <summary>The first thing that went wrong on the last <see cref="Refresh"/> (a file that can't be opened or isn't complete JSON), or null.</summary>
    public string? Error => _problems.Count == 0 ? null : _problems[0];

    /// <summary>Looks at the folder again. Returns true when the rows changed.</summary>
    public bool Refresh()
    {
        _problems.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (var f in Files())
        {
            seen.Add(f.FullName);
            // The signature is taken before reading, so a file that changes while it is read is read again next time
            if (_files.TryGetValue(f.FullName, out var had) && had.Written == f.LastWriteTimeUtc && had.Length == f.Length) continue;
            var kind = f.Name.Contains("_Character_", StringComparison.OrdinalIgnoreCase) ? LedgerFileKind.Character : LedgerFileKind.Social;
            try
            {
                string text;
                using (var stream = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    text = reader.ReadToEnd();
                _files[f.FullName] = new Cached(f.LastWriteTimeUtc, f.Length, LedgerParser.Parse(text, kind));
                changed = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Half written, or locked for a moment: keep what we had of it and look again next time
                _problems.Add($"{f.Name}: {(ex is JsonException ? "not complete yet" : ex.Message)}");
            }
        }
        foreach (var gone in _files.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _files.Remove(gone);
            changed = true;
        }
        if (changed) Merge();
        return changed;
    }

    private IEnumerable<FileInfo> Files()
    {
        if (!System.IO.Directory.Exists(LedgerDirectory)) yield break;
        var dated = new DirectoryInfo(LedgerDirectory).EnumerateFiles("*.json").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var f in dated)
            if (!characterFilesOnly || f.Name.Contains("_Character_", StringComparison.OrdinalIgnoreCase)) yield return f;
        var archive = Path.Combine(LedgerDirectory, "Archive");
        if (characterFilesOnly || !System.IO.Directory.Exists(archive)) yield break;
        foreach (var f in new DirectoryInfo(archive).EnumerateFiles("*.json").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)) yield return f;
    }

    private void Merge()
    {
        var all = new List<LedgerRow>();
        var before = new HashSet<string>(StringComparer.Ordinal);
        // In file order, dated files then the archive: a row is dropped only if an earlier FILE already had it
        foreach (var f in Files())
        {
            if (!_files.TryGetValue(f.FullName, out var file)) continue;
            foreach (var r in file.Rows)
                if (!before.Contains(r.Identity)) all.Add(r);
            foreach (var r in file.Rows) before.Add(r.Identity);
        }
        Rows = all.OrderBy(r => r.Time).ToList();   // stable, so rows of one instant stay in file order
        Version++;
    }
}
