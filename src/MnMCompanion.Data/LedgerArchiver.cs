using System.Text;
using System.Text.Json;
using MnMCompanion.Ledger;

namespace MnMCompanion.Data;

/// <summary>What one pass over the game's ledger folders did.</summary>
public sealed record ArchiveResult(int Files, int FilesRead, int RowsSeen, int RowsAdded, IReadOnlyList<string> Problems)
{
    public override string ToString() =>
        $"{Files} ledger files, {FilesRead} read, {RowsSeen:N0} rows seen, {RowsAdded:N0} new" + (Problems.Count == 0 ? "" : $", {Problems.Count} to retry");
}

/// <summary>
/// Copies every ledger row on this PC into the app's database: every server and character under the game's data folder, the dated
/// files and the archive folder alike (not only the character on screen). A file is read only when its size or write time differs
/// from what was imported, so a pass over an unchanged folder costs a directory listing. A file caught half written is left for the
/// next pass. Reads files, writes only the database.
/// </summary>
public static class LedgerArchiver
{
    public static ArchiveResult Run(LedgerDb db, string? root = null)
    {
        root ??= LedgerLocator.DefaultRoot();
        int files = 0, read = 0, seen = 0, added = 0;
        var problems = new List<string>();
        try
        {
            if (!Directory.Exists(root)) return new ArchiveResult(0, 0, 0, 0, problems);
            foreach (var server in Directory.EnumerateDirectories(root))
                foreach (var character in Directory.EnumerateDirectories(server))
                {
                    var ledger = Path.Combine(character, "Ledger");
                    if (!Directory.Exists(ledger)) continue;
                    foreach (var (file, relative) in Files(ledger))
                    {
                        files++;
                        var key = new LedgerFileKey(Path.GetFileName(server), Path.GetFileName(character), relative);
                        var written = file.LastWriteTimeUtc;
                        // Size and time are taken before reading: a file that changes while it is read is read again next pass
                        if (db.ImportedState(key) is { } had && had.Size == file.Length && had.WrittenUtc == Iso(written)) continue;
                        try
                        {
                            string text;
                            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                                text = reader.ReadToEnd();
                            var rows = LedgerParser.Parse(text, LedgerDb.KindOf(file.Name));
                            var envelope = LedgerParser.Envelope(text);
                            seen += rows.Count;
                            added += db.Import(key, file.Length, written, rows, envelope);
                            read++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                        {
                            problems.Add($"{key.Server}\\{key.Character}\\{relative}: {(ex is JsonException ? "not complete yet" : ex.Message)}");
                        }
                    }
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{root}: {ex.Message}");
        }
        return new ArchiveResult(files, read, seen, added, problems);
    }

    /// <summary>The ledger files of a character: the dated ones, then <c>Archive\*.json</c>, each with its path under the Ledger folder.</summary>
    private static IEnumerable<(FileInfo File, string Relative)> Files(string ledger)
    {
        foreach (var f in new DirectoryInfo(ledger).EnumerateFiles("*.json").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            yield return (f, f.Name);
        var archive = Path.Combine(ledger, "Archive");
        if (!Directory.Exists(archive)) yield break;
        foreach (var f in new DirectoryInfo(archive).EnumerateFiles("*.json").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            yield return (f, "Archive\\" + f.Name);
    }

    private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
