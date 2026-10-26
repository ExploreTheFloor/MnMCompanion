using System.Globalization;
using MnMCompanion.Community;
using MnMCompanion.Journal;

namespace MnMCompanion.Data;

/// <summary>An NPC the journals name: how many lines of theirs are held, when, and whether the ledger has also seen their name looted.</summary>
public sealed record NpcSummary(string Name, int Lines, DateTime? First, DateTime? Last, bool LootedAsMonster);

/// <summary>One thing an NPC said to one character.</summary>
public sealed record NpcLine(DateTime? Time, string Character, string Speaker, string Verb, string Text);

public sealed partial class HistoryStore
{
    private static DateTime? Local(string? utc) =>
        utc is null ? null : DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();

    /// <summary>The NPCs the journals name, the one heard from most recently first. <paramref name="character"/> null = everyone on the server (or the PC).</summary>
    public IReadOnlyList<NpcSummary> Npcs(string? server = null, string? character = null) => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        var where = Conditions(cmd, server, character, "l");
        cmd.CommandText = $"""
            SELECT n.name, count(l.id), min(l.at_utc), max(l.at_utc), n.looted_as_monster
            FROM npc n JOIN npc_line l ON l.npc_id = n.id WHERE 1 = 1{where}
            GROUP BY n.id ORDER BY max(l.at_utc) DESC, n.name
            """;
        return Read(cmd, r => new NpcSummary(r.GetString(0), r.GetInt32(1), Local(r.IsDBNull(2) ? null : r.GetString(2)), Local(r.IsDBNull(3) ? null : r.GetString(3)), r.GetInt32(4) != 0));
    });

    /// <summary>What one NPC has said, oldest first (names match without regard to case).</summary>
    public IReadOnlyList<NpcLine> NpcLines(string npc, string? server = null, string? character = null) => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        var where = Conditions(cmd, server, character, "l");
        cmd.Parameters.AddWithValue("$npc", npc);
        cmd.CommandText = $"""
            SELECT l.at_utc, l.character, l.speaker, l.verb, l.text FROM npc_line l JOIN npc n ON n.id = l.npc_id
            WHERE n.name = $npc{where} ORDER BY l.at_utc, l.id
            """;
        return Read(cmd, r => new NpcLine(Local(r.IsDBNull(0) ? null : r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
    });

    /// <summary>
    /// What the NPCs said, for the community file: each NPC's distinct lines (what was said, no time, no character), with the name of
    /// the character they were said to replaced by <c>{player}</c>, and any line too long or empty left out. The lines are sorted, so
    /// nothing says when or to whom a line was said.
    /// </summary>
    public IReadOnlyList<ContributionNpc> OwnNpcDialogue() => db.With(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT n.name, l.character, l.text FROM npc_line l JOIN npc n ON n.id = l.npc_id";
        var perNpc = new Dictionary<string, (string Name, HashSet<string> Lines)>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var name = r.GetString(0).Trim();
            var text = JournalParser.WithoutPlayerName(r.GetString(2), r.GetString(1)).Trim();
            if (name.Length == 0 || name.Length > 80 || text.Length == 0 || text.Length > ContributionBuilder.MaxLine) continue;
            if (!perNpc.TryGetValue(name, out var entry)) perNpc[name] = entry = (name, new HashSet<string>(StringComparer.Ordinal));
            entry.Lines.Add(text);
        }
        return perNpc.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => new ContributionNpc(e.Name, e.Lines.Order(StringComparer.Ordinal).Take(ContributionBuilder.MaxLinesPerNpc).ToList()))
            .Where(n => n.Lines.Count > 0).Take(ContributionBuilder.MaxNpcs).ToList();
    });
}
