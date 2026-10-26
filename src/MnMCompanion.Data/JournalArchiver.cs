using System.Globalization;
using System.Text;
using MnMCompanion.Journal;
using MnMCompanion.Ledger;

namespace MnMCompanion.Data;

/// <summary>What one pass over the game's journal folders did.</summary>
public sealed record JournalResult(int Files, int FilesRead, int NpcsAdded, int LinesAdded, IReadOnlyList<string> Problems)
{
    public override string ToString() =>
        $"{Files} journal files, {FilesRead} read, {NpcsAdded:N0} new NPCs, {LinesAdded:N0} new lines" + (Problems.Count == 0 ? "" : $", {Problems.Count} to retry");
}

/// <summary>
/// Copies what NPCs have said to the player into the app's database: every <c>&lt;server&gt;\&lt;character&gt;\journal\&lt;NPC name&gt;</c> file on this PC.
/// A file is read only when its size or write time differs from what was imported, so a pass over unchanged files costs a directory
/// listing; a file caught half written is left for the next pass. Reads files, writes only the database. Afterwards the NPCs are lined up
/// with the monsters the ledger has seen looted (<see cref="LedgerDb.MarkLootedNpcs"/>).
/// </summary>
public static class JournalArchiver
{
    public static JournalResult Run(LedgerDb db, string? root = null)
    {
        root ??= LedgerLocator.DefaultRoot();
        int files = 0, read = 0, npcs = 0, lines = 0;
        var problems = new List<string>();
        try
        {
            if (!Directory.Exists(root)) return new JournalResult(0, 0, 0, 0, problems);
            foreach (var server in Directory.EnumerateDirectories(root))
                foreach (var character in Directory.EnumerateDirectories(server))
                {
                    var journal = Path.Combine(character, "journal");
                    if (!Directory.Exists(journal)) continue;
                    foreach (var file in new DirectoryInfo(journal).EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        files++;
                        var key = new LedgerFileKey(Path.GetFileName(server), Path.GetFileName(character), file.Name);
                        var written = file.LastWriteTimeUtc;
                        if (db.JournalState(key) is { } had && had.Size == file.Length && had.WrittenUtc == LedgerDb.Iso(written)) continue;
                        try
                        {
                            string text;
                            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                                text = reader.ReadToEnd();
                            var (newNpcs, newLines) = db.ImportJournal(key, file.Length, written, JournalParser.Parse(text));
                            npcs += newNpcs;
                            lines += newLines;
                            read++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            problems.Add($"{key.Server}\\{key.Character}\\journal\\{key.File}: {ex.Message}");
                        }
                    }
                }
            if (npcs > 0 || lines > 0) db.MarkLootedNpcs();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{root}: {ex.Message}");
        }
        return new JournalResult(files, read, npcs, lines, problems);
    }
}
