using Avalonia;
using HammerOS.Core;

namespace HammerOS;

internal static class Program
{
    public static bool Windowed { get; private set; }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version") || args.Contains("-v")) { Console.WriteLine(Product.NameAndVersion); return 0; }
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine($"{Product.NameAndVersion} — a retro Windows workspace\n\n--windowed   Start in a resizable window\n--selftest   Start the platform headlessly, check the workspace, and exit\n--version    Print version\n--help       Show help\n\nF11 toggles fullscreen. Ctrl+Space opens applications.\nTerminal supports real PowerShell and CMD tabs; Ctrl+Shift+T adds a shell.\nFile Explorer / This PC works with real files; Department uses virtual records.\ntiled embeds supported Windows applications and restores them on exit.");
            return 0;
        }
        if (args.Contains("--selftest")) return SelfTest();
        Windowed = args.Contains("--windowed");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>
    /// What build.ps1 runs against the published single-file binary. A self-contained
    /// bundle resolves Avalonia's native libraries and avares:// assets differently
    /// from `dotnet run`, so this exercises exactly that without opening a window or
    /// touching the user's real %LOCALAPPDATA%/HammerOS/session.json.
    /// </summary>
    private static int SelfTest()
    {
        try
        {
            BuildAvaloniaApp().SetupWithoutStarting();

            var state = new SystemState(path: null, persistent: false);
            var required = new[] { "/department/welcome.txt", "/personal/notes.txt", "/system/workstation.conf" };
            foreach (var path in required)
            {
                if (state.Find(path) is null) { Console.WriteLine($"FAIL  seeded file missing: {path}"); return 1; }
            }
            var conf = state.Find("/system/workstation.conf")!.Content;
            if (!conf.Contains($"build={Product.Version}")) { Console.WriteLine($"FAIL  workstation.conf does not report build={Product.Version}"); return 1; }

            var apps = AppCatalog.All.Length;
            if (apps == 0) { Console.WriteLine("FAIL  application catalog is empty"); return 1; }

            var status = new TerminalSession(state).Execute("status");
            if (!status.Contains(Product.Version)) { Console.WriteLine("FAIL  Department shell status does not report the version"); return 1; }

            Console.WriteLine($"ok  {Product.NameAndVersion}  ·  {state.Files.Count} virtual entries  ·  {apps} applications  ·  platform initialised");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL  {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
