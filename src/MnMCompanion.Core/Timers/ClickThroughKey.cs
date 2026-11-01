namespace MnMCompanion.Timers;

/// <summary>
/// The key held to use the timers window while clicks otherwise pass through it to the game (the player's choice, off until they turn click-through on).
/// Only a modifier can be chosen: it is a key the game rarely reads alone, and it is the one key the app looks at (whether it is down, nothing else).
/// What is saved may say anything, so a name that is not one of these means <see cref="Default"/>.
/// </summary>
public static class ClickThroughKey
{
    public const string Default = "Ctrl";

    /// <summary>The keys offered, in the order the menu shows them.</summary>
    public static readonly IReadOnlyList<string> Names = ["Ctrl", "Alt", "Shift"];

    /// <summary>The offered name that <paramref name="saved"/> means (any letter case, spaces around it ignored), or <see cref="Default"/>.</summary>
    public static string Normalise(string? saved) =>
        Names.FirstOrDefault(n => string.Equals(n, saved?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>Windows' virtual-key code of the key: VK_CONTROL, VK_MENU (Alt) or VK_SHIFT. These read either side of the keyboard.</summary>
    public static int VirtualKey(string? saved) => Normalise(saved) switch
    {
        "Alt" => 0x12,
        "Shift" => 0x10,
        _ => 0x11,
    };
}
