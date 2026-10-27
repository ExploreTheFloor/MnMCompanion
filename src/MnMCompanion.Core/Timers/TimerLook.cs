namespace MnMCompanion.Timers;

/// <summary>
/// How see-through the timers window is. Only the window's background fades (the bars and the words stay solid, so they can be read over a bright game);
/// the bar with the title and buttons never goes below <see cref="TitleFloor"/>, so there is always something to grab and the buttons stay in sight.
/// </summary>
public static class TimerLook
{
    /// <summary>The levels offered, as the percent of the background that is left (100 = solid).</summary>
    public static readonly IReadOnlyList<int> OpacityLevels = [100, 85, 70, 55, 40, 25, 10];

    /// <summary>The least that can be kept: at nothing a layered window can't be clicked on at all.</summary>
    public const int MinOpacity = 5;

    /// <summary>The title bar stays at least this opaque (of 255).</summary>
    public const byte TitleFloor = 0xB0;

    /// <summary>A percent held to what can be used (a saved file can hold anything).</summary>
    public static int ClampOpacity(int percent) => Math.Clamp(percent, MinOpacity, 100);

    /// <summary>The colour channel (0 to 255) for a percent.</summary>
    public static byte Alpha(int percent) => (byte)Math.Round(ClampOpacity(percent) * 255 / 100.0);

    /// <summary>The title bar's channel for a percent: the same, but never below <see cref="TitleFloor"/>.</summary>
    public static byte TitleAlpha(int percent) => Math.Max(Alpha(percent), TitleFloor);

    /// <summary>The offered level nearest to <paramref name="percent"/> (what the menu shows as chosen).</summary>
    public static int Nearest(int percent) => OpacityLevels.OrderBy(l => Math.Abs(l - ClampOpacity(percent))).First();
}
