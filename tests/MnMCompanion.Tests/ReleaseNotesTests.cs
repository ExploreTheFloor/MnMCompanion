using MnMCompanion.Update;

namespace MnMCompanion.Tests;

/// <summary>The "what's new" window: reading the change log the program carries, choosing which entries to show, and when it opens by itself.</summary>
public class ReleaseNotesTests
{
    private const string Site = "https://github.com/ExploreTheFloor/MnMCompanion";

    private const string Log = """
        # Changelog

        Some words about the file.

        ## 0.4.8 (2026-11-10)

        **In short:** A suggestion box.

        - **New: send a suggestion.** Under ⓘ. See [Sending a suggestion](README.md#sending-a-suggestion) and
          the [FAQ](docs/FAQ.md#how-do-i-suggest-something).
        - **Network:** only when you press `Send`.

        ## 0.4.7 (2026-11-01)

        - **New: a Market page.**

        ## 0.4.6 (2026-11-01)

        - Copy the coin line.
        """;

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MnMCompanion.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("MnMCompanion.sln not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Entries_are_read_newest_first_with_wrapped_lines_joined()
    {
        var entries = ReleaseNotes.Parse(Log);
        Assert.Equal(["0.4.8", "0.4.7", "0.4.6"], entries.Select(e => AppVersion.Show(e.Version)));
        Assert.Equal(new DateOnly(2026, 11, 10), entries[0].Date);
        var lines = entries[0].Lines;
        Assert.Equal("**In short:** A suggestion box.", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.StartsWith("- **New: send a suggestion.**", lines[2]);
        Assert.EndsWith("and the [FAQ](docs/FAQ.md#how-do-i-suggest-something).", lines[2]);
        Assert.Equal("- **Network:** only when you press `Send`.", lines[3]);
        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public void Between_picks_the_versions_since_the_last_shown_and_only_the_current_one_when_none_was()
    {
        var entries = ReleaseNotes.Parse(Log);
        Assert.Equal(["0.4.8", "0.4.7"], ReleaseNotes.Between(entries, Version.Parse("0.4.6"), Version.Parse("0.4.8")).Select(e => AppVersion.Show(e.Version)));
        Assert.Equal(["0.4.8"], ReleaseNotes.Between(entries, null, Version.Parse("0.4.8")).Select(e => AppVersion.Show(e.Version)));
        Assert.Equal(["0.4.7"], ReleaseNotes.Between(entries, Version.Parse("0.4.6"), Version.Parse("0.4.7.0")).Select(e => AppVersion.Show(e.Version)));
        Assert.Empty(ReleaseNotes.Between(entries, Version.Parse("0.4.8"), Version.Parse("0.4.8")));
        Assert.Empty(ReleaseNotes.Between(entries, null, Version.Parse("0.9.0")));
    }

    [Fact]
    public void A_line_is_split_into_words_bold_code_and_links_and_the_projects_own_links_become_full_addresses()
    {
        var pieces = ReleaseNotes.Pieces("**New: a box.** See [Sending](README.md#sending-a-suggestion), the [FAQ](docs/FAQ.md#how) and `Send`, or [GitHub](https://github.com/x).", Site);
        Assert.Equal(
        [
            new BoldPiece("New: a box."), new PlainPiece(" See "), new LinkPiece("Sending", Site + "#sending-a-suggestion"), new PlainPiece(", the "),
            new LinkPiece("FAQ", Site + "/blob/main/docs/FAQ.md#how"), new PlainPiece(" and "), new CodePiece("Send"), new PlainPiece(", or "), new LinkPiece("GitHub", "https://github.com/x"), new PlainPiece("."),
        ], pieces);
        Assert.Equal([new PlainPiece("plain words")], ReleaseNotes.Pieces("plain words", Site));
    }

    [Fact]
    public void Bullets_are_told_from_paragraphs()
    {
        Assert.True(ReleaseNotes.IsBullet("- **New:** thing", out var rest));
        Assert.Equal("**New:** thing", rest);
        Assert.False(ReleaseNotes.IsBullet("**In short:** words", out rest));
        Assert.Equal("**In short:** words", rest);
    }

    [Fact]
    public void The_real_change_log_reads_and_has_the_version_being_built()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "CHANGELOG.md"));
        var entries = ReleaseNotes.Parse(text);
        Assert.True(entries.Count >= 10);
        var project = File.ReadAllText(Path.Combine(Root(), "src", "MnMCompanion", "MnMCompanion.csproj"));
        var version = System.Text.RegularExpressions.Regex.Match(project, @"<Version>(\d+\.\d+\.\d+)</Version>").Groups[1].Value;
        var shown = Assert.Single(ReleaseNotes.Between(entries, null, Version.Parse(version)));
        Assert.NotEmpty(shown.Lines);
        // every link in the notes shown after an update must be one the window can open
        foreach (var line in entries.SelectMany(e => e.Lines))
            foreach (var piece in ReleaseNotes.Pieces(line, Site).OfType<LinkPiece>())
                Assert.StartsWith("https://", piece.Url);
    }

    [Fact]
    public void The_window_opens_by_itself_once_after_an_update_and_never_on_a_fresh_install()
    {
        var current = Version.Parse("0.4.8");
        Assert.False(WhatsNewPolicy.ShouldShow(shownVersion: null, freshInstall: true, current));
        Assert.True(WhatsNewPolicy.ShouldShow(null, freshInstall: false, current), "an install from before the window existed: shown once");
        Assert.True(WhatsNewPolicy.ShouldShow("0.4.7", false, current));
        Assert.False(WhatsNewPolicy.ShouldShow("0.4.8", false, current), "shown already");
        Assert.False(WhatsNewPolicy.ShouldShow("0.4.9", false, current), "a downgrade shows nothing");
        Assert.False(WhatsNewPolicy.ShouldShow("not a version", false, current), "a setting that isn't a version shows nothing");
    }
}
