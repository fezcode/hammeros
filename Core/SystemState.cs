using System.Text.Json;

namespace HammerOS.Core;

public sealed class CalendarEntry { public string Id { get; set; } = ""; public DateTime When { get; set; } public string Title { get; set; } = ""; }

public sealed class VirtualFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Modified { get; set; } = DateTime.Now;
    public bool IsDirectory { get; set; }
    public string Name => Path.TrimEnd('/').Split('/').Last();
}

public sealed class Preferences
{
    public bool Scanlines { get; set; } = true;
    public bool Motion { get; set; } = true;
    public bool EdgeResize { get; set; } = true;
    public bool Network { get; set; } = true;
    public string Wallpaper { get; set; } = "Petrol";
    public string WallpaperScene { get; set; } = "Classic";
    public bool WallpaperAnimation { get; set; } = true;
    public string Employee { get; set; } = "Operator";
    public int[] Bins { get; set; } = [32, 48, 25, 61, 39];
}

public sealed class SavedState
{
    public int BrandingVersion { get; set; } = 1;
    public List<CalendarEntry> Calendar { get; set; } = [];
    public SudokuGame? Sudoku { get; set; }
    public Preferences Preferences { get; set; } = new();
    public List<VirtualFile> Files { get; set; } = [];
}

public sealed class SystemState
{
    public List<CalendarEntry> Calendar { get; private set; } = [];
    public SudokuGame Sudoku { get; set; } = SudokuGame.Create();
    private readonly string? _statePath;
    public Preferences Preferences { get; private set; } = new();
    public List<VirtualFile> Files { get; private set; } = [];
    public List<string> Events { get; } = ["Session authenticated · HMR-04", "Virtual filesystem mounted", "Department link established"];
    public Dictionary<string, bool> Services { get; } = new() { ["Refinement engine"] = true, ["Archive indexer"] = true, ["Intranet relay"] = true, ["Wellness scheduler"] = true };
    public bool Online => Preferences.Network && Services["Intranet relay"];
    public event Action? Changed;
    public string? StorageWarning { get; private set; }

    public SystemState(string? path = null, bool persistent = true)
    {
        _statePath = persistent ? path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HammerOS", "session.json") : null;
        Seed();
        var defaults = Files.ToDictionary(x => x.Path);
        if (_statePath is not null && File.Exists(_statePath))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(_statePath));
                if (saved is not null && saved.Preferences is not null && saved.Files is not null)
                {
                    Preferences = saved.Preferences;
                    if (saved.Sudoku is { Valid: true }) Sudoku = saved.Sudoku;
                    Calendar = saved.Calendar ?? [];
                    if (Preferences.Bins is not { Length: 5 }) Preferences.Bins = [32, 48, 25, 61, 39];
                    Preferences.Bins = Preferences.Bins.Select(x => Math.Clamp(x, 0, 100)).ToArray();
                    if (string.IsNullOrWhiteSpace(Preferences.Employee) || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Preferences.Employee))) == "830AF5E8C4BD06F1A9A86FB3A6E7AC7BF41C2A0C079B6781ECC4909D8ABBBD2B") Preferences.Employee = "Operator";
                    Files = saved.Files.Where(x => x is not null && x.Path.StartsWith('/')).ToList();
                    StockContentMigration.Apply(Files, defaults);
                    if (!Files.Any(x => x.Path == "/")) Files.Add(new() { Path = "/", IsDirectory = true });
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            { StorageWarning = "Saved session could not be read. Running with a fresh virtual desktop."; }
        }
    }

    private void Seed()
    {
        foreach (var dir in new[] { "/", "/department", "/department/refinement", "/department/handbook", "/personal", "/system" })
            Files.Add(new() { Path = dir, IsDirectory = true });
        SeedFile("/department/welcome.txt", "WELCOME TO THE WORKSPACE\n\nYour workspace is ready for new ideas.\n\nYour workstation has been prepared for your arrival. Visit Refinement to begin your assigned file, or consult the employee handbook through the Intranet.\n\nExplore the tools at your own pace.\n\n— Management");
        SeedFile("/department/refinement/aster.hmr", "FILE: ASTER\nCLASSIFICATION: DATA\nBINS: 01 / 02 / 03 / 04 / 05\n\nSelect a group of numbers that feels significant. Assign your selection to a bin. Completion is its own reward.");
        SeedFile("/department/refinement/signal-queue.hmr", "FILE: SIGNAL QUEUE\nSTATUS: AWAITING ASSIGNMENT\n\nThis file has not yet been assigned to your workstation.");
        SeedFile("/department/handbook/principles.txt", "WORKSPACE PRINCIPLES\n\nCuriosity. Clarity. Care. Craft. Openness. Patience. Play.\n\nLet these principles guide your work on the workspace.");
        SeedFile("/department/handbook/orientation.txt", "WORKSTATION ORIENTATION\n\nCtrl+Space   Application directory\nCtrl+L       Lock workstation\nF11          Fullscreen / windowed\nAlt+F4       Exit HammerOS\n\nDrag a title bar to move a window. Use its corner to resize. Minimized windows remain in the taskbar.\n\nThe Department shell and Department / H: use virtual records. This PC works with real files. PowerShell and CMD tabs run real commands. tiled hosts supported Windows applications.");
        SeedFile("/personal/notes.txt", "Things to remember\n\nOne workspace. Room to explore.\n");
        SeedFile("/system/workstation.conf", $"hostname=HMR-04\ndepartment=Refinement\nbuild={Product.Version}\nnetwork=HAMMER-INTRANET\nclassification=INTERNAL");
    }
    private void SeedFile(string path, string content) => Files.Add(new() { Path = path, Content = content });
    public static string Normalize(string path, string cwd = "/")
    {
        var segments = new List<string>();
        foreach (var part in (path.StartsWith('/') ? path : cwd.TrimEnd('/') + "/" + path).Replace('\\', '/').Split('/'))
        { if (part is "" or ".") continue; if (part == "..") { if (segments.Count > 0) segments.RemoveAt(segments.Count - 1); } else segments.Add(part); }
        return "/" + string.Join('/', segments);
    }
    public static string Parent(string path) { var i = path.LastIndexOf('/'); return i <= 0 ? "/" : path[..i]; }
    public VirtualFile? Find(string path) => Files.FirstOrDefault(x => x.Path == Normalize(path));
    public IEnumerable<VirtualFile> List(string path) => Files.Where(x => x.Path != "/" && Parent(x.Path) == Normalize(path)).OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name);
    public string Write(string path, string content, bool directory = false)
    {
        path = Normalize(path);
        if (Find(Parent(path))?.IsDirectory != true) return "Parent folder does not exist.";
        if (path.StartsWith("/system", StringComparison.Ordinal)) return "System records are read-only.";
        var existing = Find(path);
        if (existing?.IsDirectory == true || (existing is not null && directory)) return "An item already exists at that path.";
        if (existing is null) { existing = new() { Path = path, IsDirectory = directory }; Files.Add(existing); }
        existing.Content = content; existing.Modified = DateTime.Now; Save(); return "Saved.";
    }
    public string Delete(string path)
    {
        path = Normalize(path);
        if (path is "/" or "/personal" or "/department" || path.StartsWith("/system", StringComparison.Ordinal)) return "This location is protected.";
        var item = Find(path); if (item is null) return "Item not found.";
        if (List(path).Any()) return "Folder must be empty before removal.";
        Files.Remove(item); Save(); return "Removed.";
    }
    public void Record(string message) { Events.Insert(0, message); if (Events.Count > 100) Events.RemoveAt(Events.Count - 1); }
    public void Save()
    {
        if (_statePath is not null)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_statePath))!);
                File.WriteAllText(_statePath + ".tmp", JsonSerializer.Serialize(new SavedState { Preferences = Preferences, Files = Files, Sudoku = Sudoku, Calendar = Calendar }, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(_statePath + ".tmp", _statePath, true); StorageWarning = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StorageWarning = "Changes are in memory only; session storage is unavailable."; }
        }
        Changed?.Invoke();
    }
}
