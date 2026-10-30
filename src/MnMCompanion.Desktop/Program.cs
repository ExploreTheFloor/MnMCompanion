using System.Runtime.InteropServices;
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
        if (OperatingSystem.IsLinux()) PrepareWebView();
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
    }

    /// <summary>
    /// The Map page's web view (WebKitGTK) draws through GPU buffers (DMA-BUF) that some drivers can't make under Wayland ("Failed to create GBM buffer"),
    /// and the map is then blank. Drawing without them works everywhere. Set before anything starts, and only when the player hasn't set it themselves.
    /// It goes through the C library: WebKit reads the process's own environment, which .NET's Environment.SetEnvironmentVariable doesn't change on Linux.
    /// </summary>
    private static void PrepareWebView()
    {
        const string name = "WEBKIT_DISABLE_DMABUF_RENDERER";
        if (Environment.GetEnvironmentVariable(name) is not null) return;
        try { setenv(name, "1", 0); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);
}
