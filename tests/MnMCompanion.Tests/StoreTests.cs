using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using Xunit.Abstractions;
using static MnMCompanion.Tests.Fixture;

namespace MnMCompanion.Tests;

public class LootStoreTests
{
    private static readonly DateTime Today = DateTime.Today.AddHours(9);

    /// <summary>A server with Aldric (looting with Brom) and Other, who sold the rat ear for a price Aldric never saw.</summary>
    private static (Scratch Scratch, LootStore Store) Make()
    {
        var s = new Scratch();
        s.Write("ledger\\srv\\Aldric\\Ledger\\Aldric_Character_x.json", File(
            Item(Today, "Aldric", "Aldric", "a rat", "Rat Ear"),
            Purse(Today.AddSeconds(-1), "Aldric", "Aldric", "a rat", "0,0,0,30")));
        s.Write("ledger\\srv\\Aldric\\Ledger\\Aldric_Social_x.json", File(
            Item(Today.AddMinutes(1), "Brom", "Aldric", "a bat", "Bat Wing", "clr", 34),
            Item(Today.AddDays(-2), "Brom", "Aldric", "a bat", "Bat Wing", "clr", 34)));
        s.Write("ledger\\srv\\Other\\Ledger\\Other_Character_x.json", File(Sale(Today, "Other", "Rat Ear", 2, "0 platinum 0 gold 0 silver 10 copper")));
        var store = new LootStore(Path.Combine(s.Path, "state"), Path.Combine(s.Path, "ledger"));
        return (s, store);
    }

    [Fact]
    public void Characters_are_listed_and_a_selected_ones_loot_is_grouped_by_day()
    {
        var (s, store) = Make();
        using (s)
        {
            Assert.Equal(2, store.Sources.Count);
            store.Select(store.Sources.Single(x => x.Character == "Aldric"));
            Assert.True(store.Refresh());

            Assert.Equal([DateOnly.FromDateTime(Today), DateOnly.FromDateTime(Today.AddDays(-2))], store.Days());
            var today = store.Events(DateOnly.FromDateTime(Today));
            Assert.Equal(3, today.Count);                                         // purse, own item, Brom's item
            Assert.Single(store.Events(DateOnly.FromDateTime(Today.AddDays(-2))));
            Assert.Empty(store.Events(new DateOnly(2000, 1, 1)));
            Assert.Null(store.Error);
        }
    }

    [Fact]
    public void Prices_come_from_every_characters_sales()
    {
        var (s, store) = Make();
        using (s)
        {
            store.Select(store.Sources.Single(x => x.Character == "Aldric"));
            store.Refresh();
            Assert.Equal(new ItemValue(5, ValueSource.Sold), store.Values.Of("Rat Ear"));    // Other sold 2 for 10
            Assert.False(store.Values.Of("Bat Wing").Known);
        }
    }

    [Fact]
    public void Refresh_reports_a_change_only_when_the_ledger_changed_and_keeps_the_same_values_otherwise()
    {
        var (s, store) = Make();
        using (s)
        {
            var aldric = store.Sources.Single(x => x.Character == "Aldric");
            store.Select(aldric);
            Assert.True(store.Refresh());
            var values = store.Values;
            Assert.False(store.Refresh());
            Assert.Same(values, store.Values);

            var path = Path.Combine(aldric.LedgerDirectory, "Aldric_Social_x.json");
            System.IO.File.WriteAllText(path, File(Item(Today.AddMinutes(1), "Brom", "Aldric", "a bat", "Bat Wing"), Item(Today.AddMinutes(2), "Doc", "Aldric", "a bat", "Bat Wing")));
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
            Assert.True(store.Refresh());
            Assert.Same(values, store.Values);                                       // no sale changed
            Assert.Equal(4, store.Events(DateOnly.FromDateTime(Today)).Count);
        }
    }

    [Fact]
    public void A_session_is_the_loot_between_two_markers_and_nothing_is_deleted()
    {
        var (s, store) = Make();
        using (s)
        {
            store.Select(store.Sources.Single(x => x.Character == "Aldric"));
            store.Refresh();
            var day = DateOnly.FromDateTime(Today);
            Assert.Equal(3, store.Events(day, store.Sessions.Current).Count);

            var closed = store.NewSession();
            Assert.NotNull(closed);
            Assert.Empty(store.Events(day, store.Sessions.Current));                // the new session starts from nothing
            Assert.Equal(3, store.Events(day).Count);                               // the day still has it all
            Assert.Single(Directory.GetFiles(Path.Combine(store.StateFolder!, "sessions"), "*.txt"));
            Assert.Null(store.NewSession());                                         // an empty session makes no marker

            Assert.True(store.Sessions.RemoveLast());
            Assert.Equal(3, store.Events(day, store.Sessions.Current).Count);
        }
    }

    [Fact]
    public void Sessions_are_kept_for_next_time_per_character()
    {
        var (s, store) = Make();
        using (s)
        {
            store.Select(store.Sources.Single(x => x.Character == "Aldric"));
            store.Refresh();
            store.NewSession();

            var again = new LootStore(Path.Combine(s.Path, "state"), Path.Combine(s.Path, "ledger"));
            again.Select(again.Sources.Single(x => x.Character == "Aldric"));
            Assert.Single(again.Sessions.Marks);
            again.Select(again.Sources.Single(x => x.Character == "Other"));
            Assert.Empty(again.Sessions.Marks);
        }
    }

    [Fact]
    public void Pointing_the_store_at_another_folder_forgets_the_first_characters_loot()
    {
        var (s, store) = Make();
        using (s)
        {
            store.Select(store.Sources.Single(x => x.Character == "Aldric"));
            store.Refresh();
            Assert.NotEmpty(store.Days());

            store.ChangeRoot(Path.Combine(s.Path, "nowhere"));
            Assert.Empty(store.Sources);
            Assert.Null(store.Selected);
            Assert.Empty(store.Days());
            Assert.False(store.Refresh());
        }
    }

    [Fact]
    public void A_missing_ledger_folder_is_just_empty()
    {
        using var s = new Scratch();
        var store = new LootStore(Path.Combine(s.Path, "state"), Path.Combine(s.Path, "nothing-here"));
        Assert.Empty(store.Sources);
        Assert.False(store.Refresh());
        Assert.Empty(store.Days());
    }
}

/// <summary>
/// Reads the real ledger on this PC and prints what it finds, to check the reader against counts made straight from the JSON.
/// Runs only when LOOTWINDOW_REAL_LEDGER is set (to a character name, or "*" for the most recent one).
/// </summary>
public class RealLedgerCheck(ITestOutputHelper output)
{
    [Fact]
    public void Counts()
    {
        var want = Environment.GetEnvironmentVariable("LOOTWINDOW_REAL_LEDGER");
        if (string.IsNullOrEmpty(want)) return;
        var sources = LedgerLocator.Find();
        var source = want == "*" ? sources.FirstOrDefault() : sources.FirstOrDefault(s => s.Character.Equals(want, StringComparison.OrdinalIgnoreCase));
        if (source is null) { output.WriteLine("no such character"); return; }

        var folder = new LedgerFolder(source.Directory);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        folder.Refresh();
        output.WriteLine($"{source.Label}: {folder.FileCount} files, {folder.Rows.Count} rows, read in {sw.ElapsedMilliseconds} ms, error: {folder.Error ?? "none"}");
        var events = LootExtractor.Extract(folder.Rows);
        output.WriteLine($"loot events {events.Count}: own {events.Count(e => e.You)}, others {events.Count(e => !e.You)}, items {events.Count(e => e.Kind == LootKind.Item)}, purses {events.Count(e => e.Kind == LootKind.Coin)}");
        output.WriteLine($"own loot rows in the Character files: {folder.Rows.Count(r => r.File == LedgerFileKind.Character && r.Act is "act_13" or "act_14")}");
        output.WriteLine($"others' loot rows (Social + Archive, unique): {folder.Rows.Count(r => r.File == LedgerFileKind.Social && r.Act is "act_13" or "act_14" && !r.ByOwner)}");
        output.WriteLine($"looters {events.Select(e => e.Who).Distinct(StringComparer.OrdinalIgnoreCase).Count()}, days {events.Select(e => e.Day).Distinct().Count()}, {events.Min(e => e.Time):yyyy-MM-dd} to {events.Max(e => e.Time):yyyy-MM-dd}");
        var classes = events.Select(e => e.Class).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        output.WriteLine("class codes: " + string.Join(" ", classes.Select(c => $"{c}={GameClasses.NameOf(c)}")));
        Assert.NotEmpty(events);
    }
}
