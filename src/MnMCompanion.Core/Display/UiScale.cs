namespace MnMCompanion.Display;

/// <summary>
/// How big a window's contents are drawn, in percent (the player's choice, for each kind of window: 100 is the size the program is drawn at).
/// What is saved may say anything, so every number is clamped to <see cref="Min"/>–<see cref="Max"/> before it is used. A window grows or shrinks
/// with its contents (<see cref="Resized"/>), so a bigger scale shows the same page, only larger, and not less of it.
/// </summary>
public static class UiScale
{
    public const int Default = 100;
    public const int Min = 70;
    public const int Max = 250;

    /// <summary>How far one click of the wheel, or one press of Ctrl + or Ctrl −, moves the size.</summary>
    public const int Step = 10;

    /// <summary>The sizes the Size and font window offers as buttons.</summary>
    public static readonly IReadOnlyList<int> Presets = [80, 90, 100, 110, 125, 150, 175, 200];

    public static int Clamp(int percent) => Math.Clamp(percent, Min, Max);

    /// <summary>The factor to draw at: 1.0 at 100%.</summary>
    public static double Factor(int percent) => Clamp(percent) / 100.0;

    /// <summary>One step bigger (<paramref name="direction"/> above 0) or smaller (below 0), landing on a multiple of <see cref="Step"/> (125 goes to 130 or 120).</summary>
    public static int Next(int percent, int direction)
    {
        var now = Clamp(percent);
        if (direction == 0) return now;
        var next = direction > 0 ? (now / Step + 1) * Step : ((now + Step - 1) / Step - 1) * Step;
        return Clamp(next);
    }

    /// <summary>A window length after the size changed from <paramref name="from"/> to <paramref name="to"/> percent: in proportion, so the contents have the same room.</summary>
    public static double Resized(double length, int from, int to) =>
        double.IsNaN(length) || double.IsInfinity(length) ? length : Math.Round(length * Clamp(to) / Clamp(from));
}
