using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MnMCompanion.Timers;

namespace MnMCompanion;

/// <summary>
/// Listens for the keys that start timers. Each key is registered with Windows (RegisterHotKey), which tells this program when exactly that key
/// is pressed and nothing else: there is no keyboard hook, nothing else the player types is seen, and no key press is ever sent. Windows keeps the
/// key for this program while it is registered, so the game does not receive it (the reason a bare letter or digit is not allowed, see
/// <see cref="Hotkey.Problem"/>). The listener is a window with no size or place of its own, so it works whatever has the focus, the game included.
/// </summary>
internal sealed class HotkeyHost : IDisposable
{
    private const int WmHotkey = 0x0312, ErrorAlreadyRegistered = 1409;
    private const uint ModNoRepeat = 0x4000;   // a held key starts the timer once, not over and over

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _registered = [];
    private IReadOnlyList<(string Name, Hotkey Key)> _wanted = [];
    private bool _suspended;

    /// <summary>A registered key was pressed: the name of the timer it starts.</summary>
    public event Action<string>? Pressed;

    public HotkeyHost()
    {
        // HWND_MESSAGE (-3): a window that only receives messages
        _source = new HwndSource(new HwndSourceParameters("MnMCompanionHotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(Hook);
    }

    /// <summary>
    /// Makes <paramref name="keys"/> the keys that are listened for (the old ones are let go). Returns the ones that Windows would not give
    /// (another program has them), as timer name to a sentence saying so; empty when every key was taken or while listening is suspended.
    /// </summary>
    public IReadOnlyDictionary<string, string> Apply(IReadOnlyList<(string Name, Hotkey Key)> keys)
    {
        _wanted = keys;
        return Register();
    }

    /// <summary>Lets every key go (so a key can be pressed to be recorded rather than start a timer); <see cref="Resume"/> takes them again.</summary>
    public void Suspend()
    {
        _suspended = true;
        Release();
    }

    public IReadOnlyDictionary<string, string> Resume()
    {
        _suspended = false;
        return Register();
    }

    private Dictionary<string, string> Register()
    {
        Release();
        var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_suspended) return failed;
        int id = 1;
        foreach (var (name, key) in _wanted)
        {
            if (key.VirtualKey == 0) continue;
            if (RegisterHotKey(_source.Handle, id, (uint)key.Modifiers | ModNoRepeat, (uint)key.VirtualKey))
                _registered[id++] = name;
            else
            {
                int error = Marshal.GetLastWin32Error();
                failed[name] = $"“{name}” can't have {key}: " + (error == ErrorAlreadyRegistered ? "another program already uses it." : new Win32Exception(error).Message);
            }
        }
        return failed;
    }

    private void Release()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var name))
        {
            handled = true;
            Pressed?.Invoke(name);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Release();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
