using System.Diagnostics;
using MnMCompanion.Timers;

namespace MnMCompanion;

/// <summary>
/// Plays the sound of a timer (see <see cref="TimerSounds"/>): one of the five system sounds, or a .wav file from where it is. On Linux a system sound is
/// the desktop's sound theme's nearest one (the freedesktop sounds: a bell, an information chime, a warning, an error), played by PipeWire's or
/// PulseAudio's player (pw-play, paplay; aplay for a .wav when neither is there). It never throws: a sound that can't be played (the file has gone, no player
/// is installed) is silent, and a timer is never held up by it.
/// </summary>
internal static class TimerSoundPlayer
{
    private static readonly string[] SoundThemes = ["/usr/share/sounds/freedesktop/stereo", "/usr/share/sounds/Yaru/stereo", "/usr/share/sounds/ubuntu/stereo"];

    private static readonly Dictionary<string, string[]> SystemFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Beep"] = ["bell.oga", "bell.ogg"],
        ["Asterisk"] = ["dialog-information.oga", "dialog-information.ogg", "message.oga"],
        ["Exclamation"] = ["dialog-warning.oga", "dialog-warning.ogg", "bell.oga"],
        ["Hand"] = ["dialog-error.oga", "dialog-error.ogg", "bell.oga"],
        ["Question"] = ["message.oga", "message-new-instant.oga", "dialog-question.oga"],
    };

    private static readonly Lazy<string?> Player = new(() => new[] { "pw-play", "paplay" }.FirstOrDefault(OnPath));

    private static DateTime _lastUtc = DateTime.MinValue;
    private static Process? _playing;

    /// <summary>
    /// Plays <paramref name="sound"/> (nothing for none). With <paramref name="throttled"/>, a sound is skipped when another was played in the last moment: for the
    /// corpse timers, where several end together and one sound will do.
    /// </summary>
    public static void Play(string? sound, bool throttled = false)
    {
        if (TimerSounds.Normalize(sound) is not { } kept) return;
        var now = DateTime.UtcNow;
        if (throttled && (now - _lastUtc).TotalSeconds < 1.5) return;
        _lastUtc = now;
        try
        {
            var file = TimerSounds.SystemName(kept) is { } name ? SystemFile(name) : TimerSounds.FilePath(kept);
            if (file is null || !File.Exists(file)) return;
            var player = Player.Value ?? (file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && OnPath("aplay") ? "aplay" : null);
            if (player is null) return;
            // The next sound replaces this one
            try { if (_playing is { HasExited: false } old) old.Kill(); }
            catch (InvalidOperationException) { }
            var start = new ProcessStartInfo(player) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            if (player == "aplay") start.ArgumentList.Add("-q");
            start.ArgumentList.Add(file);
            _playing = Process.Start(start);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    private static string? SystemFile(string name) =>
        SystemFiles.GetValueOrDefault(name, []).SelectMany(f => SoundThemes.Select(dir => Path.Combine(dir, f))).FirstOrDefault(File.Exists);

    private static bool OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(dir => File.Exists(Path.Combine(dir, program)));
}
