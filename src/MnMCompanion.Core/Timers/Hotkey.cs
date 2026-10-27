using System.Globalization;

namespace MnMCompanion.Timers;

/// <summary>The modifier keys of a hotkey. The values are Windows' own, so they go straight to RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>
/// A key (with its modifiers) that starts a timer from anywhere, the game included. It is kept as text ("Ctrl+Alt+1") so it can be typed, shown and saved.
/// Only letters, digits, F1 to F24 and the numeric keypad can be used: a plain letter or digit would be taken away from the game (and from typing in
/// its chat), so those need Ctrl or Alt with them.
/// </summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, int> Codes = BuildCodes();

    /// <summary>Every key name that can be used, as it is written: A to Z, 0 to 9, F1 to F24, Num0 to Num9, NumAdd, NumSubtract, NumMultiply, NumDivide, NumDecimal.</summary>
    public static IReadOnlyCollection<string> KeyNames => Codes.Keys;

    /// <summary>The Windows virtual-key code of <see cref="Key"/>; 0 if it is not a key that can be used.</summary>
    public int VirtualKey => Codes.GetValueOrDefault(Key);

    /// <summary>The key as it is written back: modifiers in a fixed order, then the key ("Ctrl+Alt+Shift+Win+1").</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>
    /// A hotkey from what was typed or saved: "ctrl+alt+1", "Alt + F5", "Num5". Case and spacing don't matter; "Control" and "Windows" are accepted.
    /// Null if it isn't one (an unknown key or modifier, nothing, or no key at the end). This reads the text only: <see cref="Problem"/> says if it may be used.
    /// </summary>
    public static Hotkey? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length is 0 or > 5 || parts.Any(p => p.Length == 0)) return null;
        var mods = HotkeyModifiers.None;
        foreach (var m in parts[..^1])
        {
            switch (m.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win" or "windows": mods |= HotkeyModifiers.Win; break;
                default: return null;
            }
        }
        var key = Codes.Keys.FirstOrDefault(k => string.Equals(k, parts[^1], StringComparison.OrdinalIgnoreCase));
        return key is null ? null : new Hotkey(mods, key);
    }

    /// <summary>Why this key can't be used (the sentence to show the player), or null if it can.</summary>
    public static string? Problem(Hotkey? key)
    {
        if (key is null || !Codes.ContainsKey(key.Key)) return "That key can't be used for a timer. Use a letter, a digit, F1 to F24 or a keypad key.";
        if (IsFunctionOrKeypad(key.Key)) return null;
        bool strong = key.Modifiers.HasFlag(HotkeyModifiers.Ctrl) || key.Modifiers.HasFlag(HotkeyModifiers.Alt) || key.Modifiers.HasFlag(HotkeyModifiers.Win);
        if (!strong) return key.Modifiers == HotkeyModifiers.None
            ? "A letter or digit needs Ctrl or Alt with it, or the game would stop getting that key."
            : "Shift alone is used for typing; add Ctrl or Alt.";
        return null;
    }

    private static bool IsFunctionOrKeypad(string key) => key.StartsWith("Num", StringComparison.Ordinal) || key[0] == 'F' && key.Length > 1;

    private static Dictionary<string, int> BuildCodes()
    {
        var codes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < 26; i++) codes[((char)('A' + i)).ToString()] = 0x41 + i;
        for (int i = 0; i < 10; i++) codes[i.ToString(CultureInfo.InvariantCulture)] = 0x30 + i;
        for (int i = 1; i <= 24; i++) codes["F" + i.ToString(CultureInfo.InvariantCulture)] = 0x70 + i - 1;
        for (int i = 0; i < 10; i++) codes["Num" + i.ToString(CultureInfo.InvariantCulture)] = 0x60 + i;
        codes["NumMultiply"] = 0x6A;
        codes["NumAdd"] = 0x6B;
        codes["NumSubtract"] = 0x6D;
        codes["NumDecimal"] = 0x6E;
        codes["NumDivide"] = 0x6F;
        return codes;
    }
}
