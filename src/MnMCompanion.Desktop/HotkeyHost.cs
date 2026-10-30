using Avalonia.Threading;
using MnMCompanion.Timers;

namespace MnMCompanion;

/// <summary>
/// Starts timers from keys pressed anywhere, the game included. On Linux no program may take a key for itself (and on Wayland none can see a key
/// meant for another window), so the key is given to the timer in the desktop's own keyboard settings, as the command <c>MnMCompanion --timer "Name"</c>.
/// That command, started by the desktop, leaves a note naming the timer in Companion's folder and quits at once; the running Companion sees the note,
/// starts the timer and throws the note away. There is no keyboard hook: Companion never sees a key, only the notes, and it never sends a key press.
/// </summary>
internal sealed class HotkeyHost : IDisposable
{
    /// <summary>A note older than this when it is found (Companion wasn't running) is thrown away rather than starting a timer late.</summary>
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(10);

    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _sweep;
    private HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private bool _suspended;

    /// <summary>A timer's key was pressed: the name of the timer it starts.</summary>
    public event Action<string>? Pressed;

    /// <summary>Where the notes are left.</summary>
    public static string Folder => Path.Combine(Settings.Folder, "timer-keys");

    /// <summary>Whether the desktop is Hyprland, whose bind lines can be written out for the player.</summary>
    public static bool Hyprland => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));

    public HotkeyHost()
    {
        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.EnumerateFiles(Folder, "*.key")) TryDelete(old);   // left from when Companion wasn't running
        _watcher = new FileSystemWatcher(Folder, "*.key") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _watcher.Created += (_, _) => Dispatcher.UIThread.Post(Sweep);
        _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(Sweep);
        _watcher.EnableRaisingEvents = true;
        // In case the watcher misses one (a folder on a network share, a full event queue)
        _sweep = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Sweep());
        _sweep.Start();
    }

    /// <summary>The command that starts the timer called <paramref name="name"/>, for the desktop's key settings.</summary>
    public static string Command(string name)
    {
        var exe = Environment.ProcessPath ?? "MnMCompanion";
        // Run as "dotnet MnMCompanion.dll" (a build, not a published program), the command needs both. A published single-file program never gets here.
#pragma warning disable IL3000
        var program = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase) && typeof(HotkeyHost).Assembly.Location is { Length: > 0 } dll
            ? $"{Quote(exe)} {Quote(dll)}"
            : Quote(exe);
#pragma warning restore IL3000
        return $"{program} --timer {Quote(name)}";
    }

    /// <summary>How Hyprland is set up here: in Lua (Hyprland 0.55 and later; with Omarchy's helpers when it is Omarchy), or in the older hyprland.conf.</summary>
    private enum HyprStyle { Conf, Lua, Omarchy }

    private static HyprStyle Style()
    {
        var config = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "hypr");
        if (!File.Exists(Path.Combine(config, "hyprland.lua"))) return HyprStyle.Conf;
        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OMARCHY_PATH")) && !Directory.Exists("/usr/share/omarchy") ? HyprStyle.Lua : HyprStyle.Omarchy;
    }

    /// <summary>
    /// What the player pastes into their desktop's settings: on Hyprland the lines for each timer with a key (and the window rules that float Companion's
    /// windows and keep the small ones on every workspace), elsewhere each timer's command with its key beside it. Empty when there are no timers.
    /// </summary>
    public static string Bindings(IReadOnlyList<(string Name, string? Key)> timers)
    {
        if (timers.Count == 0) return "";
        var lines = new List<string>();
        if (Hyprland)
        {
            var style = Style();
            string comment = style == HyprStyle.Conf ? "#" : "--";
            lines.Add(style switch
            {
                HyprStyle.Omarchy => "-- Companion: paste into ~/.config/hypr/bindings.lua (the window rules can go in hyprland.lua)",
                HyprStyle.Lua => "-- Companion: paste into your Hyprland Lua config",
                _ => "# Companion: paste into hyprland.conf (or a file it sources)",
            });
            foreach (var (name, key) in timers)
            {
                if (Hotkey.TryParse(key) is not { } k)
                {
                    lines.Add($"{comment} {name}: no key set; give it one in Companion's timer setup, or bind {Command(name)} yourself");
                    continue;
                }
                var keys = string.Join(" + ", HyprlandMods(k.Modifiers).Split(' ', StringSplitOptions.RemoveEmptyEntries).Append(HyprlandKey(k.Key)));   // "CTRL + ALT + 1"
                lines.Add(style switch
                {
                    HyprStyle.Omarchy => $"o.bind(\"{keys}\", \"Companion: {name.Replace("\"", "'")}\", [[{Command(name)}]])",
                    HyprStyle.Lua => $"hl.bind(\"{keys}\", hl.dsp.exec_cmd([[{Command(name)}]]))",
                    _ => $"bind = {HyprlandMods(k.Modifiers)}, {HyprlandKey(k.Key)}, exec, {Command(name)}",
                });
            }
            lines.Add("");
            lines.Add($"{comment} Companion's windows float, and the small window and the timers stay on every workspace");
            lines.Add(style switch
            {
                HyprStyle.Conf => "windowrule = float, class:^(MnMCompanion)$\nwindowrule = pin, class:^(MnMCompanion)$, title:^(Companion|Companion timers)$",
                _ => "o.window(\"^(MnMCompanion)$\", { float = true })\no.window({ class = \"^(MnMCompanion)$\", title = \"^(Companion|Companion timers)$\" }, { pin = true })",
            });
        }
        else
        {
            foreach (var (name, key) in timers)
                lines.Add($"{key ?? "(no key set)"}: {Command(name)}");
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string HyprlandMods(HotkeyModifiers m) => string.Join(" ", new[]
    {
        m.HasFlag(HotkeyModifiers.Ctrl) ? "CTRL" : "", m.HasFlag(HotkeyModifiers.Alt) ? "ALT" : "", m.HasFlag(HotkeyModifiers.Shift) ? "SHIFT" : "", m.HasFlag(HotkeyModifiers.Win) ? "SUPER" : "",
    }.Where(s => s.Length > 0));

    /// <summary>A key as Hyprland (xkb) names it.</summary>
    private static string HyprlandKey(string key) => key switch
    {
        "NumMultiply" => "KP_Multiply", "NumAdd" => "KP_Add", "NumSubtract" => "KP_Subtract", "NumDecimal" => "KP_Decimal", "NumDivide" => "KP_Divide",
        _ when key.StartsWith("Num", StringComparison.Ordinal) => "KP_" + key[3..],
        _ when key.Length == 1 => key.ToLowerInvariant(),
        _ => key,
    };

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// <c>--timer "Name"</c>: leaves the note for the running Companion and returns. The note is written under another name and then renamed, so it is
    /// never seen half written.
    /// </summary>
    public static void Request(string name)
    {
        Directory.CreateDirectory(Folder);
        var note = Path.Combine(Folder, $"{DateTime.UtcNow.Ticks}-{Guid.NewGuid():N}");
        File.WriteAllText(note + ".tmp", name.Trim());
        File.Move(note + ".tmp", note + ".key");
    }

    /// <summary>
    /// Which timers can be started by a note (the ones that have a name; a key only matters to the desktop). Returns what couldn't be set up, as timer name to
    /// a sentence saying so: nothing here, since the desktop is what holds the keys.
    /// </summary>
    public IReadOnlyDictionary<string, string> Apply(IReadOnlyList<(string Name, Hotkey Key)> keys)
    {
        _names = keys.Select(k => k.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, string>();
    }

    /// <summary>Notes are thrown away unread while the setup window is open (a key pressed there is being recorded, not used).</summary>
    public void Suspend() => _suspended = true;

    public IReadOnlyDictionary<string, string> Resume()
    {
        _suspended = false;
        return new Dictionary<string, string>();
    }

    private void Sweep()
    {
        string[] notes;
        try { notes = Directory.GetFiles(Folder, "*.key"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (var note in notes.Order(StringComparer.Ordinal))
        {
            string name;
            DateTime written;
            try
            {
                name = File.ReadAllText(note).Trim();
                written = File.GetLastWriteTimeUtc(note);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            TryDelete(note);
            // Any timer by its name works, keyed or not: the command names it, and the setup window is where a key is chosen to bind it to
            if (_suspended || DateTime.UtcNow - written > Stale || name.Length == 0) continue;
            Pressed?.Invoke(name);
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _sweep.Stop();
        _watcher.Dispose();
    }
}
