namespace MnMCompanion.Timers;

/// <summary>One sound a timer can play: <see cref="Value"/> is what is kept ("system:Asterisk", "file:C:\Windows\Media\Alarm01.wav"), <see cref="Name"/> what the player sees.</summary>
public sealed record TimerSound(string Group, string Name, string Value);

/// <summary>
/// The sounds a timer can end with. A sound is kept as text so it can go in the timers file: a Windows system sound ("system:Beep") or a .wav file
/// ("file:C:\Sounds\horn.wav"). Windows has sounds of its own in its Media folder (alarms, rings and the like), which are offered by name; the player can
/// also pick any .wav file they have. Nothing is copied or downloaded: a file is played from where it is, and a file that has gone is just silent.
/// </summary>
public static class TimerSounds
{
    public const string SystemPrefix = "system:", FilePrefix = "file:";

    /// <summary>The sounds Windows plays for its own events, which are on every PC.</summary>
    public static readonly IReadOnlyList<string> SystemNames = ["Beep", "Asterisk", "Exclamation", "Hand", "Question"];

    /// <summary>The longest path kept for a sound file.</summary>
    public const int MaxPath = 260;

    /// <summary>
    /// A sound as it is kept, or null for none: a system sound by name (any case), or a path to a .wav file. Anything else (text that is no sound, another kind of
    /// file, a path with control characters or one that is too long) is none, so a damaged or hand-edited file can't hold a sound that can't be played.
    /// </summary>
    public static string? Normalize(string? sound)
    {
        if (string.IsNullOrWhiteSpace(sound)) return null;
        sound = sound.Trim();
        if (sound.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = SystemNames.FirstOrDefault(n => string.Equals(n, sound[SystemPrefix.Length..].Trim(), StringComparison.OrdinalIgnoreCase));
            return name is null ? null : SystemPrefix + name;
        }
        if (sound.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var path = sound[FilePrefix.Length..].Trim();
            if (path.Length is 0 or > MaxPath || path.Any(char.IsControl)) return null;
            return path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? FilePrefix + path : null;
        }
        return null;
    }

    /// <summary>A sound for the player to read: "No sound", "Asterisk", "Alarm01" (a file by its name without the folder and ".wav").</summary>
    public static string Describe(string? sound)
    {
        if (Normalize(sound) is not { } kept) return "No sound";
        return kept.StartsWith(SystemPrefix, StringComparison.Ordinal) ? kept[SystemPrefix.Length..] : Path.GetFileNameWithoutExtension(kept[FilePrefix.Length..]);
    }

    /// <summary>The system sound's name when <paramref name="sound"/> is one; null otherwise.</summary>
    public static string? SystemName(string? sound) => Normalize(sound) is { } kept && kept.StartsWith(SystemPrefix, StringComparison.Ordinal) ? kept[SystemPrefix.Length..] : null;

    /// <summary>The file's path when <paramref name="sound"/> is a file; null otherwise.</summary>
    public static string? FilePath(string? sound) => Normalize(sound) is { } kept && kept.StartsWith(FilePrefix, StringComparison.Ordinal) ? kept[FilePrefix.Length..] : null;

    /// <summary>The sound for a .wav file at <paramref name="path"/>, or null if that isn't one that can be kept.</summary>
    public static string? ForFile(string? path) => path is null ? null : Normalize(FilePrefix + path);

    /// <summary>
    /// The sounds in Windows' Media folder, in groups (Alarms, Rings, Windows sounds), each by name. Speech sounds and the theme folders are left out. Empty when
    /// the folder isn't there (another system, such as Wine, may not have it). <paramref name="folder"/> is for tests; by default it is the PC's own.
    /// </summary>
    public static IReadOnlyList<TimerSound> WindowsFiles(string? folder = null)
    {
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        try
        {
            if (!Directory.Exists(folder)) return [];
            return Directory.EnumerateFiles(folder, "*.wav", SearchOption.TopDirectoryOnly)
                .Select(f => (Path: f, Name: Path.GetFileNameWithoutExtension(f)))
                .Where(f => !f.Name.StartsWith("Speech", StringComparison.OrdinalIgnoreCase))
                .Select(f => (f.Name, Value: ForFile(f.Path), Group: f.Name.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase) ? "Alarms" : f.Name.StartsWith("Ring", StringComparison.OrdinalIgnoreCase) ? "Rings" : "Windows sounds"))
                .Where(f => f.Value is not null)
                .OrderBy(f => Array.IndexOf(["Alarms", "Rings", "Windows sounds"], f.Group)).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new TimerSound(f.Group, f.Name, f.Value!))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}
