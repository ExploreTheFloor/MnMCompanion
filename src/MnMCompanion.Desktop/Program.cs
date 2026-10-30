using Avalonia;
using Avalonia.Media;

namespace MnMCompanion;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // --timer "Name": a key bound in the desktop's settings; the running Companion starts the timer (see HotkeyHost). No window, done at once.
        if (Array.IndexOf(args, "--timer") is var at and >= 0)
        {
            if (at + 1 >= args.Length) return 2;
            HotkeyHost.Request(args[at + 1]);
            return 0;
        }
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
    }
}
