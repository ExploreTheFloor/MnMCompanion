using System.Globalization;
using MnMCompanion.Ledger;
using MnMCompanion.Loot;
using static MnMCompanion.Tests.Fixture;

namespace MnMCompanion.Tests;

/// <summary>
/// The line a click on the coin at the top of the window copies: what the group looted, what reached you of it (your split) and your share, on one line.
/// </summary>
public class CoinLineTests
{
    private static readonly DateTime T = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Local);
    private static readonly DateOnly Day = DateOnly.FromDateTime(T);

    private static List<LootEvent> Rows(params string[] rows) => LootExtractor.Extract(Parse(File(rows), LedgerFileKind.Social));

    [Fact]
    public void The_line_gives_the_groups_coin_your_split_and_your_share_of_it()
    {
        // The day of OursTests: 100 in a purse split (15 reached you), 30 of your own looted alone, 40 of someone else's: 170 in all, 45 yours
        var day = LootDay.Of(Day, Rows(
            Purse(T, "Brom", "Aldric", "a rat", "0,0,1,0", split: "0,0,0,15"),
            Purse(T.AddHours(1), "Aldric", "Aldric", "a rat", "0,0,0,30"),
            Purse(T.AddSeconds(60), "Pip", "Aldric", "a rat", "0,0,0,40")), new ItemValues([]));

        Assert.Equal(170, day.Totals.Copper);
        Assert.Equal(45, day.Ours.Copper);
        Assert.Equal("Coin looted: 1sp 70cp | My split: 45cp | My share: 26.5%", LootSummary.CoinLine(day.Totals.Copper, day.Ours, mine: true));
    }

    [Fact]
    public void The_line_is_a_single_line_in_coin_denominations()
    {
        var line = LootSummary.CoinLine(8_95_01, new LootOurs(1_78_03, 0, 0, 0, 0), mine: true);
        Assert.Equal("Coin looted: 8gp 95sp 1cp | My split: 1gp 78sp 3cp | My share: 19.9%", line);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    [Fact]
    public void The_share_has_a_decimal_only_when_it_needs_one()
    {
        Assert.EndsWith("My share: 50%", LootSummary.CoinLine(200, new LootOurs(100, 0, 0, 0, 0), true));
        Assert.EndsWith("My share: 33.3%", LootSummary.CoinLine(300, new LootOurs(100, 0, 0, 0, 0), true));
        Assert.EndsWith("My share: 100%", LootSummary.CoinLine(300, new LootOurs(300, 0, 0, 0, 0), true));
        Assert.EndsWith("My share: 0%", LootSummary.CoinLine(300, new LootOurs(0, 0, 0, 0, 0), true));
    }

    [Fact]
    public void The_share_never_passes_a_hundred_even_when_estimated_shares_add_up_to_more_than_the_purses()
    {
        var line = LootSummary.CoinLine(100, new LootOurs(140, 0, 0, 0, 0, EstimatedShares: 3), true);
        Assert.EndsWith("My share: ~100%", line);
    }

    [Fact]
    public void An_estimated_split_is_marked_with_a_tilde_on_the_split_and_on_the_share()
    {
        var line = LootSummary.CoinLine(1000, new LootOurs(250, 0, 0, 0, 0, EstimatedShares: 2), true);
        Assert.Equal("Coin looted: 10sp | My split: ~2sp 50cp | My share: ~25%", line);
    }

    [Fact]
    public void Without_anything_of_yours_in_view_there_is_no_split_to_give()
    {
        Assert.Equal("Coin looted: 1sp 70cp | My split: none yet", LootSummary.CoinLine(170, LootOurs.None, mine: false));
    }

    [Fact]
    public void With_no_coin_at_all_the_line_has_no_share_to_work_out()
    {
        Assert.Equal("Coin looted: 0cp | My split: 0cp", LootSummary.CoinLine(0, LootOurs.None, mine: true));
    }

    [Fact]
    public void The_line_reads_the_same_whatever_the_computers_number_format_is()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.EndsWith("My share: 33.3%", LootSummary.CoinLine(300, new LootOurs(100, 0, 0, 0, 0), true));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
}
