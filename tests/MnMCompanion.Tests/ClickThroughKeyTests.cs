using MnMCompanion.Timers;

namespace MnMCompanion.Tests;

/// <summary>The key held to use the timers window when clicks pass through it: only a modifier, and nothing saved can leave the window without one.</summary>
public class ClickThroughKeyTests
{
    [Theory]
    [InlineData("Ctrl", "Ctrl")]
    [InlineData("alt", "Alt")]
    [InlineData("  SHIFT ", "Shift")]
    [InlineData(null, "Ctrl")]
    [InlineData("", "Ctrl")]
    [InlineData("F5", "Ctrl")]
    [InlineData("W", "Ctrl")]
    [InlineData("Ctrl+Alt", "Ctrl")]
    public void A_saved_name_means_one_of_the_three_modifiers_or_the_default(string? saved, string used) => Assert.Equal(used, ClickThroughKey.Normalise(saved));

    [Theory]
    [InlineData("Ctrl", 0x11)]
    [InlineData("Alt", 0x12)]
    [InlineData("Shift", 0x10)]
    [InlineData("nonsense", 0x11)]
    public void Each_key_is_windows_own_modifier_code(string saved, int code) => Assert.Equal(code, ClickThroughKey.VirtualKey(saved));

    [Fact]
    public void Only_modifiers_are_offered_so_no_letter_or_digit_is_ever_looked_at()
    {
        Assert.Equal(["Ctrl", "Alt", "Shift"], ClickThroughKey.Names);
        Assert.All(ClickThroughKey.Names, n => Assert.Contains(ClickThroughKey.VirtualKey(n), new[] { 0x10, 0x11, 0x12 }));
        Assert.Contains(ClickThroughKey.Default, ClickThroughKey.Names);
    }
}
