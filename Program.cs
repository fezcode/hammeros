using Avalonia;

namespace HammerOS;

internal static class Program
{
    public static bool Windowed { get; private set; }
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version") || args.Contains("-v")) { Console.WriteLine("HammerOS 0.1.0"); return; }
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("HammerOS — a fictional corporate operating environment\n\n--windowed   Start in a resizable window\n--version    Print version\n--help       Show help\n\nF11 toggles fullscreen. Ctrl+Space opens applications. Ctrl+L locks.\nAlt+F4 exits. The Department shell uses virtual files.\nThe PowerShell tab opens a real Windows terminal; Ctrl+L clears it.");
            return;
        }
        Windowed = args.Contains("--windowed");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
