using System.Text.Json;

namespace HammerOS.Core;

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
    public bool Network { get; set; } = true;
    public string Wallpaper { get; set; } = "Petrol";
    public string Employee { get; set; } = "Mark S.";
    public int[] Bins { get; set; } = [32, 48, 25, 61, 39];
}

public sealed class SavedState
{
    public Preferences Preferences { get; set; } = new();
    public List<VirtualFile> Files { get; set; } = [];
}

public sealed class SystemState
{
    private readonly string? _statePath;
    public Preferences Preferences { get; private set; } = new();
    public List<VirtualFile> Files { get; private set; } = [];
    public List<string> Events { get; } = ["Session authenticated · MDR-04", "Virtual filesystem mounted", "Department link established"];
    public Dictionary<string, bool> Services { get; } = new() { ["Refinement engine"] = true, ["Archive indexer"] = true, ["Intranet relay"] = true, ["Wellness scheduler"] = true };
    public bool Online => Preferences.Network && Services["Intranet relay"];
    public event Action? Changed;
    public string? StorageWarning { get; private set; }

    public SystemState(string? path = null, bool persistent = true)
    {
        _statePath = persistent ? path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HammerOS", "session.json") : null;
        Seed();
        if (_statePath is not null && File.Exists(_statePath))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(_statePath));
                if (saved is not null && saved.Preferences is not null && saved.Files is not null)
                {
                    Preferences = saved.Preferences;
                    if (Preferences.Bins is not { Length: 5 }) Preferences.Bins = [32, 48, 25, 61, 39];
                    Preferences.Bins = Preferences.Bins.Select(x => Math.Clamp(x, 0, 100)).ToArray();
                    if (string.IsNullOrWhiteSpace(Preferences.Employee)) Preferences.Employee = "Mark S.";
                    Files = saved.Files.Where(x => x is not null && x.Path.StartsWith('/')).ToList();
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
        SeedFile("/department/welcome.txt", "WELCOME TO THE SEVERED FLOOR\n\nYour work is mysterious and important.\n\nYour workstation has been prepared for your arrival. Visit Macrodata Refinement to begin your assigned file, or consult the employee handbook through the Intranet.\n\nPlease enjoy each application equally.\n\n— Management");
        SeedFile("/department/refinement/siena.mdr", "FILE: SIENA\nCLASSIFICATION: MACRODATA\nBINS: 01 / 02 / 03 / 04 / 05\n\nSelect a group of numbers that feels significant. Assign your selection to a bin. Completion is its own reward.");
        SeedFile("/department/refinement/cold-harbor.mdr", "FILE: COLD HARBOR\nSTATUS: AWAITING ASSIGNMENT\n\nThis file has not yet been assigned to your workstation.");
        SeedFile("/department/handbook/principles.txt", "THE NINE PRINCIPLES\n\nVision. Verve. Wit. Cheer. Humility. Benevolence. Nimbleness. Probity. Wiles.\n\nLet these principles guide your work on the severed floor.");
        SeedFile("/department/handbook/orientation.txt", "WORKSTATION ORIENTATION\n\nCtrl+Space   Application directory\nCtrl+L       Lock workstation\nF11          Fullscreen / windowed\nAlt+F4       Exit HammerOS\n\nDrag a title bar to move a window. Use its corner to resize. Minimized windows remain in the taskbar.\n\nTerminal commands and files belong to the fictional HammerOS environment.");
        SeedFile("/personal/notes.txt", "Things to remember\n\nThe work is mysterious and important.\n");
        SeedFile("/system/workstation.conf", "hostname=MDR-04\ndepartment=Macrodata Refinement\nbuild=0.1.0\nnetwork=SEVERED-INTRANET\nclassification=INTERNAL");
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
                File.WriteAllText(_statePath + ".tmp", JsonSerializer.Serialize(new SavedState { Preferences = Preferences, Files = Files }, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(_statePath + ".tmp", _statePath, true); StorageWarning = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StorageWarning = "Changes are in memory only; session storage is unavailable."; }
        }
        Changed?.Invoke();
    }
}
