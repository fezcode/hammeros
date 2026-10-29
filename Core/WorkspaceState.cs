namespace HammerOS.Core;

public sealed class WorkspaceState
{
    public bool RestoreSession { get; set; } = true;
    public int Desktop { get; set; }
    public List<string> Desktops { get; set; } = ["Work", "Terminal", "Media"];
    public Dictionary<string, string> Associations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RecentFiles { get; set; } = [];
    public List<string> Favorites { get; set; } = [];
    public List<string> CommandHistory { get; set; } = [];
    public List<WindowStateRecord> Windows { get; set; } = [];
    public List<WorkspaceProfile> Profiles { get; set; } = [];
    public List<WorkspaceNotification> Notifications { get; set; } = [];
    public Dictionary<string, string> Shortcuts { get; set; } = new() { ["Search"] = "Alt+Space", ["Applications"] = "Ctrl+Space", ["Next desktop"] = "Ctrl+Alt+Right", ["Previous desktop"] = "Ctrl+Alt+Left", ["Overview"] = "Ctrl+Alt+Up" };
    public bool ClipboardHistory { get; set; }
    public void Normalize()
    {
        Desktops = (Desktops ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Take(9).ToList(); if (Desktops.Count == 0) Desktops = ["Work", "Terminal", "Media"];
        Desktop = Math.Clamp(Desktop, 0, Desktops.Count - 1); Associations = new(Associations ?? [], StringComparer.OrdinalIgnoreCase);
        RecentFiles ??= []; Favorites ??= []; CommandHistory ??= []; Windows ??= []; Profiles ??= []; Notifications ??= []; Shortcuts ??= new();
    }
}
public sealed class WindowStateRecord
{
    public string App { get; set; } = "";
    public int ActiveTab { get; set; }
    public int Desktop { get; set; }
    public double X { get; set; } = 80; public double Y { get; set; } = 50; public double Width { get; set; } = 800; public double Height { get; set; } = 560;
    public bool Minimized { get; set; } public bool Maximized { get; set; }
    public string? Path { get; set; } public string? Draft { get; set; } public DateTime Stamp { get; set; } public bool Bom { get; set; }
    public List<string> Tabs { get; set; } = [];
    public List<TerminalStateRecord> Terminals { get; set; } = [];
}
public sealed class TerminalStateRecord { public string Kind { get; set; } = "powershell"; public string Title { get; set; } = "PowerShell"; public string? Directory { get; set; } }
public sealed class WorkspaceProfile { public string Name { get; set; } = ""; public List<WindowStateRecord> Windows { get; set; } = []; }
public sealed class WorkspaceNotification { public string Id { get; set; } = Guid.NewGuid().ToString("N"); public DateTime Time { get; set; } = DateTime.Now; public string Message { get; set; } = ""; public string? App { get; set; } public string? Path { get; set; } public bool Read { get; set; } }
