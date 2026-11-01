using System.Text.RegularExpressions;

namespace MnMCompanion.Tests;

/// <summary>
/// CHANGELOG.md is the text of every release (the release workflow and scripts\release-notes.ps1 copy a version's entry into it), so a version
/// can't be released, or even built by CI, without saying what changed in it.
/// </summary>
public class ChangelogTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MnMCompanion.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("MnMCompanion.sln not found above " + AppContext.BaseDirectory);
    }

    private static string[] Lines() => File.ReadAllLines(Path.Combine(Root(), "CHANGELOG.md"));

    private static string ProjectVersion()
    {
        var project = File.ReadAllText(Path.Combine(Root(), "src", "MnMCompanion", "MnMCompanion.csproj"));
        return Regex.Match(project, @"<Version>(\d+\.\d+\.\d+)</Version>").Groups[1].Value;
    }

    private static readonly Regex Heading = new(@"^## (\d+\.\d+\.\d+) \((\d{4}-\d{2}-\d{2})\)\s*$");

    [Fact]
    public void The_version_being_built_says_what_changed_in_it()
    {
        var version = ProjectVersion();
        Assert.NotEmpty(version);
        var lines = Lines();
        int at = Array.FindIndex(lines, l => Heading.Match(l) is { Success: true } m && m.Groups[1].Value == version);
        Assert.True(at >= 0, $"CHANGELOG.md has no entry headed '## {version} (yyyy-mm-dd)': write what changed in {version} before building it.");
        var body = lines.Skip(at + 1).TakeWhile(l => !l.StartsWith("## ")).Where(l => l.Trim().Length > 0).ToList();
        Assert.NotEmpty(body);
    }

    [Fact]
    public void Every_entry_is_headed_with_a_version_and_a_date_and_they_run_newest_first()
    {
        var headings = Lines().Where(l => l.StartsWith("## ")).ToList();
        Assert.NotEmpty(headings);
        Assert.All(headings, h => Assert.True(Heading.IsMatch(h), $"Heading not in the form '## 1.2.3 (2026-11-01)': {h}"));

        var versions = headings.Select(h => Version.Parse(Heading.Match(h).Groups[1].Value)).ToList();
        Assert.Equal(versions.OrderByDescending(v => v), versions);
        Assert.Equal(versions.Count, versions.Distinct().Count());

        var dates = headings.Select(h => DateOnly.Parse(Heading.Match(h).Groups[2].Value)).ToList();
        Assert.Equal(dates.OrderByDescending(d => d), dates);
    }

    [Fact]
    public void Entries_for_versions_that_share_something_say_what_it_means_for_the_readers_data()
    {
        // from 0.4.1 on every entry carries a Sharing or Network line, as the file's introduction promises
        var lines = Lines();
        var starts = lines.Select((l, i) => (l, i)).Where(x => Heading.IsMatch(x.l)).ToList();
        foreach (var (heading, index) in starts)
        {
            var version = Version.Parse(Heading.Match(heading).Groups[1].Value);
            if (version < new Version(0, 4, 1)) continue;
            var body = lines.Skip(index + 1).TakeWhile(l => !l.StartsWith("## ")).ToList();
            Assert.True(body.Any(l => l.Contains("**Sharing") || l.Contains("**Network")), $"The entry for {version} has no **Sharing** or **Network** line.");
        }
    }
}
