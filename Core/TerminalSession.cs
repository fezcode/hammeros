using System.Text.RegularExpressions;

namespace HammerOS.Core;

public sealed class TerminalSession(SystemState state)
{
    public string Cwd { get; private set; } = "/department";
    public List<string> History { get; } = [];
    public Action<string>? OpenApp { get; set; }
    public string Execute(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";
        History.Add(command);
        var args = Regex.Matches(command, "\"([^\"]*)\"|'([^']*)'|(\\S+)").Select(x => x.Groups[1].Success ? x.Groups[1].Value : x.Groups[2].Success ? x.Groups[2].Value : x.Groups[3].Value).ToArray();
        var path = SystemState.Normalize(args.Length > 1 ? args[1] : ".", Cwd);
        switch (args[0].ToLowerInvariant())
        {
            case "help": return "AVAILABLE COMMANDS\n\nhelp                 This directory\nls [path]            List virtual files\ncd <path>            Change directory\npwd                  Current directory\ncat <file>           Read a file\nwrite <file> <text>  Create or replace a file\nmkdir <folder>       Create a folder\nrm <path>            Remove a file or empty folder\nwhoami / date        Session information\nstatus / ps          Workstation and services\nping [host]          Simulated intranet check\nopen <app>           Launch an application\necho <text>          Print text\nclear                Clear this terminal\n\nApps: refinement, files, terminal, tiled, browser, settings, control, network, notes, wellness, handbook, sudoku, hacker, editor, web, media, calendar\nAll commands operate inside HammerOS.";
            case "pwd": return Cwd;
            case "ls": return state.Find(path)?.IsDirectory == true ? string.Join('\n', state.List(path).Select(x => $"{(x.IsDirectory ? "DIR " : "FILE")}  {x.Name}{(x.IsDirectory ? "/" : "")}")) : "Directory not found.";
            case "cd": if (state.Find(path)?.IsDirectory != true) return "Directory not found."; Cwd = path; return "";
            case "cat": var file = state.Find(path); return file is { IsDirectory: false } ? file.Content : "File not found.";
            case "mkdir": return args.Length < 2 ? "Usage: mkdir <folder>" : state.Write(path, "", true);
            case "write": return args.Length < 3 ? "Usage: write <file> <text>" : state.Write(path, string.Join(' ', args.Skip(2)));
            case "rm": return args.Length < 2 ? "Usage: rm <path>" : state.Delete(path);
            case "whoami": return $"{state.Preferences.Employee}\nRefinement · HMR-04\nAccess level: LOCAL";
            case "date": return DateTime.Now.ToString("dddd, dd MMMM yyyy  HH:mm:ss");
            case "status": return $"HAMMER OPERATING SYSTEM {Product.Version}\nWorkstation  HMR-04\nNetwork      {(state.Online ? "CONNECTED" : "DISCONNECTED")}\nFilesystem   {state.Files.Count} virtual entries\nQuota        {state.Preferences.Bins.Average():0}%\nDisposition  NOMINAL";
            case "ps": return string.Join('\n', state.Services.Select((x, i) => $"{100 + i}   {(x.Value ? "RUNNING" : "STOPPED")}   {x.Key}"));
            case "ping": var host = args.Length > 1 ? args[1] : "hammer.internal"; return !state.Online ? "Network unavailable." : host is "hammer.internal" or "10.4.0.1" or "hmr-04" ? $"Simulated reply from {host}: time=1ms\n3 transmitted · 3 received · 0% loss" : "Host is outside the approved intranet.";
            case "open": if (args.Length < 2) return "Usage: open <app>"; if (!AppCatalog.All.Any(x => x.Id == args[1])) return "Unknown application. Type help for the app directory."; OpenApp?.Invoke(args[1]); return "Application requested.";
            case "echo": return string.Join(' ', args.Skip(1));
            case "clear": return "\f";
            default: return $"Unknown command: {args[0]}. Type help for available commands.";
        }
    }
}

public record AppDefinition(string Id, string Name, string Symbol, string Description);
public static class AppCatalog
{
    public static readonly AppDefinition[] All = [
        new("refinement", "Refinement", "refinement", "Find meaning in the numbers."),
        new("files", "File Explorer", "folder", "Department archives and personal files."),
        new("terminal", "Terminal", "terminal", "A direct line to your workstation."),
        new("tiled", "tiled", "refinement", "Windows applications. One tiled workspace."),
        new("sudoku", "Sudoku", "refinement", "Nine by nine. A daily exercise in logic."),
        new("hacker", "Hacker Lab", "terminal", "A cinematic signal simulation."),
        new("editor", "Text Editor", "notes", "Open, edit, and save real text files."),
        new("web", "Browser", "globe", "The web, powered by Microsoft Edge WebView2."),
        new("media", "Media Player", "play", "Play your videos and music."),
        new("calendar", "Calendar", "calendar", "Plan your days with a local calendar and agenda."),
        new("browser", "Intranet", "globe", "Your window into the company."),
        new("notes", "Memoranda", "notes", "A place for approved thoughts."),
        new("network", "Network", "network", "Every connection has a purpose."),
        new("control", "Control Panel", "control", "Keep your department in harmony."),
        new("settings", "Settings", "settings", "Make yourself comfortable."),
        new("wellness", "Wellness", "wellness", "A moment of prescribed tranquility."),
        new("handbook", "Employee Handbook", "book", "Everything you need to know.")
    ];
}
