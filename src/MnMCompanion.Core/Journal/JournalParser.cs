using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MnMCompanion.Journal;

/// <summary>
/// One thing an NPC said to the player, as the game wrote it into the journal. <see cref="Time"/> is the local time the game stamped
/// (null when the entry had none). <see cref="Speaker"/> and <see cref="Verb"/> are "Abbot Harrow" and "says" from "Abbot Harrow says
/// …", empty when the entry has another shape, in which case <see cref="Text"/> is the whole entry. <see cref="Raw"/> is the entry as written.
/// </summary>
public sealed record JournalLine(DateTime? Time, string Speaker, string Verb, string Text, string Raw);

/// <summary>
/// The game's journal: <c>&lt;character&gt;\journal\&lt;NPC name&gt;</c>, one text file per NPC with everything that NPC has said to this
/// character, newest first, each entry stamped <c>2026-10-01 14:59:29: Abbot Harrow says …</c> and separated by a blank line. A line with
/// no stamp continues the entry above it.
/// </summary>
public static partial class JournalParser
{
    public static IReadOnlyList<JournalLine> Parse(string text)
    {
        var lines = new List<JournalLine>();
        DateTime? time = null;
        var entry = new StringBuilder();

        void Flush()
        {
            var raw = entry.ToString().Trim();
            if (raw.Length > 0) lines.Add(Shape(time, raw));
            entry.Clear();
            time = null;
        }

        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var stamp = Stamp().Match(line);
            if (stamp.Success)
            {
                Flush();
                time = DateTime.TryParseExact(stamp.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t) ? t : null;
                entry.Append(stamp.Groups[2].Value);
            }
            else if (line.Trim().Length == 0) Flush();
            else entry.Append(entry.Length > 0 ? " " : "").Append(line.Trim());
        }
        Flush();
        return lines;
    }

    private static JournalLine Shape(DateTime? time, string raw)
    {
        var m = Says().Match(raw);
        return m.Success
            ? new JournalLine(time, m.Groups[1].Value.Trim(), m.Groups[2].Value.ToLowerInvariant(), m.Groups[3].Value.Trim().Trim('"', '“', '”'), raw)
            : new JournalLine(time, "", "", raw, raw);
    }

    /// <summary>
    /// The text with the player's own character name replaced by <c>{player}</c> (any case, so a shouted name goes too), for anything that
    /// leaves the PC: NPCs address the player by name, and a name must never be shared.
    /// </summary>
    public static string WithoutPlayerName(string text, string player) =>
        player.Trim().Length < 2 ? text : Regex.Replace(text, $@"(?<!\w){Regex.Escape(player.Trim())}(?!\w)", "{player}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}):\s?(.*)$")]
    private static partial Regex Stamp();

    [GeneratedRegex(@"^(.{1,60}?)\s+(says|whispers|shouts|tells you|yells),?\s+(.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Says();
}
