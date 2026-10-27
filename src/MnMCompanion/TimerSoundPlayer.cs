using System.IO;
using System.Media;
using MnMCompanion.Timers;

namespace MnMCompanion;

/// <summary>
/// Plays the sound of a timer (see <see cref="TimerSounds"/>): one of the sounds Windows plays for its own events, or a .wav file from where it is. It never
/// throws: a sound that can't be played (the file has gone, or isn't really a .wav) is silent, and a timer is never held up by it.
/// </summary>
internal static class TimerSoundPlayer
{
    private static SoundPlayer? _file;
    private static DateTime _lastUtc = DateTime.MinValue;

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
            if (TimerSounds.SystemName(kept) is { } name)
            {
                (name switch
                {
                    "Beep" => SystemSounds.Beep,
                    "Asterisk" => SystemSounds.Asterisk,
                    "Exclamation" => SystemSounds.Exclamation,
                    "Hand" => SystemSounds.Hand,
                    _ => SystemSounds.Question,
                }).Play();
            }
            else if (TimerSounds.FilePath(kept) is { } path && File.Exists(path))
            {
                _file?.Dispose();
                _file = new SoundPlayer(path);
                _file.Play();   // on its own thread; the next sound replaces this one
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }
}
