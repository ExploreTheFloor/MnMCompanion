using System.Text.RegularExpressions;

namespace MnMCompanion.Update;

/// <summary>One version's entry in CHANGELOG.md: the version, the day it was released, and its lines (each bullet joined onto one line).</summary>
public sealed record ReleaseEntry(Version Version, DateOnly Date, IReadOnlyList<string> Lines);

/// <summary>A piece of one line of the notes, for showing it: plain words, bold words, a link, or a bit of code.</summary>
public abstract record NotesPiece(string Text);
public sealed record PlainPiece(string Text) : NotesPiece(Text);
public sealed record BoldPiece(string Text) : NotesPiece(Text);
public sealed record CodePiece(string Text) : NotesPiece(Text);
public sealed record LinkPiece(string Text, string Url) : NotesPiece(Text);

/// <summary>
/// Reads CHANGELOG.md, the text of every release, which the program carries inside itself so the "what's new" window needs no network. The
/// file is wrapped to be read as text, so an indented continuation line is joined onto its bullet (as <c>scripts\release-notes.ps1</c> does for
/// the release page), and the links to the project's own pages become full addresses. Only the little Markdown the change log uses is understood:
/// <c>**bold**</c>, <c>`code`</c> and <c>[text](url)</c>; anything else is shown as it is.
/// </summary>
public static class ReleaseNotes
{
    private static readonly Regex Heading = new(@"^## (\d+\.\d+\.\d+) \((\d{4}-\d{2}-\d{2})\)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Piece = new(@"\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|\[(?<text>[^\]]+)\]\((?<url>[^)\s]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Every entry, newest first (the file's order); an entry whose heading isn't "## 1.2.3 (yyyy-mm-dd)" is skipped.</summary>
    public static IReadOnlyList<ReleaseEntry> Parse(string changelog)
    {
        var lines = changelog.Replace("\r\n", "\n").Split('\n');
        var entries = new List<ReleaseEntry>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (Heading.Match(lines[i]) is not { Success: true } m || !Version.TryParse(m.Groups[1].Value, out var version) || !DateOnly.TryParse(m.Groups[2].Value, out var date)) continue;
            var body = new List<string>();
            for (int j = i + 1; j < lines.Length && !lines[j].StartsWith("## ", StringComparison.Ordinal); j++)
            {
                var line = lines[j];
                // a wrapped continuation (indented, after a line with words) belongs to the line before it
                if (line.Length > 1 && char.IsWhiteSpace(line[0]) && line.Trim().Length > 0 && body.Count > 0 && body[^1].Length > 0) body[^1] += " " + line.Trim();
                else body.Add(line.TrimEnd());
            }
            while (body.Count > 0 && body[^1].Length == 0) body.RemoveAt(body.Count - 1);
            while (body.Count > 0 && body[0].Length == 0) body.RemoveAt(0);
            entries.Add(new ReleaseEntry(AppVersion.Normalize(version), date, body));
        }
        return entries;
    }

    /// <summary>The entries newer than <paramref name="after"/> and no newer than <paramref name="upTo"/>, newest first. Null <paramref name="after"/>: only <paramref name="upTo"/>'s own entry.</summary>
    public static IReadOnlyList<ReleaseEntry> Between(IReadOnlyList<ReleaseEntry> entries, Version? after, Version upTo)
    {
        var top = AppVersion.Normalize(upTo);
        var floor = after is null ? null : AppVersion.Normalize(after);
        return entries.Where(e => e.Version <= top && (floor is null ? e.Version == top : e.Version > floor)).OrderByDescending(e => e.Version).ToList();
    }

    /// <summary>Whether a line is a bullet ("- …"), and the words after the dash.</summary>
    public static bool IsBullet(string line, out string rest)
    {
        var t = line.TrimStart();
        if (t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal))
        {
            rest = t[2..].Trim();
            return true;
        }
        rest = line;
        return false;
    }

    /// <summary>One line split into words, bold words, code and links. A relative link to a page of the project becomes a full address on <paramref name="site"/>.</summary>
    public static IReadOnlyList<NotesPiece> Pieces(string line, string site)
    {
        var pieces = new List<NotesPiece>();
        int at = 0;
        foreach (Match m in Piece.Matches(line))
        {
            if (m.Index > at) pieces.Add(new PlainPiece(line[at..m.Index]));
            if (m.Groups["bold"].Success) pieces.Add(new BoldPiece(m.Groups["bold"].Value));
            else if (m.Groups["code"].Success) pieces.Add(new CodePiece(m.Groups["code"].Value));
            else pieces.Add(new LinkPiece(m.Groups["text"].Value, Absolute(m.Groups["url"].Value, site)));
            at = m.Index + m.Length;
        }
        if (at < line.Length) pieces.Add(new PlainPiece(line[at..]));
        return pieces;
    }

    /// <summary>"README.md#timers" → "&lt;site&gt;#timers", "docs/FAQ.md#x" → "&lt;site&gt;/blob/main/docs/FAQ.md#x", "CHANGELOG.md" → on the site; a full address stays as it is.</summary>
    public static string Absolute(string url, string site)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return url;
        site = site.TrimEnd('/');
        if (url.StartsWith("README.md", StringComparison.OrdinalIgnoreCase)) return site + url["README.md".Length..];
        return site + "/blob/main/" + url.TrimStart('/');
    }
}

/// <summary>
/// When the "what's new" window opens by itself: once, the first time the program runs after it changed version. A brand-new install (no settings
/// yet) is not shown anything, the version is only noted; after that every version that is newer than the last one shown opens it, once.
/// </summary>
public static class WhatsNewPolicy
{
    /// <param name="shownVersion">The version whose notes were shown last (<c>Settings.NotesShownVersion</c>); null when never.</param>
    /// <param name="freshInstall">The settings file did not exist when the program started: a first run, not an update.</param>
    /// <param name="current">The running version.</param>
    public static bool ShouldShow(string? shownVersion, bool freshInstall, Version current)
    {
        if (shownVersion is null) return !freshInstall;
        return AppVersion.Parse(shownVersion) is { } shown && AppVersion.Normalize(current) > AppVersion.Normalize(shown);
    }
}
