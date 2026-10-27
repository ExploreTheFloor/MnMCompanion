using MnMCompanion.Timers;

namespace MnMCompanion.Tests;

/// <summary>How see-through the timers window can be: what the levels are, that nothing saved can break it, and that the title bar can always be grabbed.</summary>
public class TimerLookTests
{
    [Theory]
    [InlineData(100, 255)]
    [InlineData(50, 128)]
    [InlineData(25, 64)]
    [InlineData(10, 26)]
    [InlineData(5, 13)]
    public void A_percent_is_the_alpha_of_the_background(int percent, int alpha) => Assert.Equal(alpha, TimerLook.Alpha(percent));

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-40, 5)]
    [InlineData(3, 5)]
    [InlineData(70, 70)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void What_is_saved_is_held_to_what_can_be_used_so_the_window_can_never_be_made_unclickable(int saved, int used) => Assert.Equal(used, TimerLook.ClampOpacity(saved));

    [Fact]
    public void The_title_bar_keeps_enough_of_itself_to_be_found_and_grabbed()
    {
        Assert.Equal(255, TimerLook.TitleAlpha(100));
        Assert.True(TimerLook.TitleAlpha(5) >= TimerLook.TitleFloor);
        Assert.True(TimerLook.TitleAlpha(10) >= TimerLook.TitleFloor);
        foreach (var level in TimerLook.OpacityLevels) Assert.True(TimerLook.TitleAlpha(level) >= TimerLook.Alpha(level));
        Assert.True(TimerLook.Alpha(TimerLook.MinOpacity) > 0, "a layered window can't be clicked where it is wholly clear");
    }

    [Fact]
    public void The_levels_go_from_solid_down_and_each_is_usable()
    {
        Assert.Equal(100, TimerLook.OpacityLevels[0]);
        Assert.Equal(TimerLook.OpacityLevels.OrderByDescending(l => l), TimerLook.OpacityLevels);
        Assert.All(TimerLook.OpacityLevels, l => Assert.Equal(l, TimerLook.ClampOpacity(l)));
        Assert.Equal(TimerLook.OpacityLevels.Count, TimerLook.OpacityLevels.Distinct().Count());
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(80, 85)]
    [InlineData(30, 25)]
    [InlineData(1, 10)]
    [InlineData(60, 55)]
    [InlineData(500, 100)]
    public void A_saved_value_between_levels_shows_as_the_nearest_one_in_the_menu(int saved, int shown) => Assert.Equal(shown, TimerLook.Nearest(saved));
}
