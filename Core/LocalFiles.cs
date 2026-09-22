using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace HammerOS.Core;

public record LocalEntry(string Path, string Name, bool IsDirectory, long Size, DateTime Modified, bool IsLink)
{
    public string Kind => IsDirectory ? "Folder" : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "File";
    public string SizeLabel => IsDirectory ? "—" : Size < 1024 ? $"{Size} B" : Size < 1048576 ? $"{Size / 1024d:0.#} KB" : $"{Size / 1048576d:0.#} MB";
}

public static class LocalFiles
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Resolve(string path) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    public static IReadOnlyList<LocalEntry> List(string path, CancellationToken token = default)
    {
        var result = new List<LocalEntry>();
        foreach (var entry in new DirectoryInfo(Resolve(path)).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            try { var directory = entry.Attributes.HasFlag(FileAttributes.Directory); result.Add(new(entry.FullName, entry.Name, directory, directory ? 0 : ((FileInfo)entry).Length, entry.LastWriteTime, entry.Attributes.HasFlag(FileAttributes.ReparsePoint))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string Child(string directory, string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' ')) throw new IOException("Enter a valid name without path separators or a trailing dot.");
        return Path.Combine(Resolve(directory), name);
    }
    public static void Create(string directory, string name, bool folder)
    {
        var path = Child(directory, name); if (File.Exists(path) || Directory.Exists(path)) throw new IOException("An item with this name already exists.");
        if (folder) Directory.CreateDirectory(path); else using (File.Open(path, FileMode.CreateNew, FileAccess.Write)) { }
    }
    public static void Rename(LocalEntry entry, string name)
    {
        var target = Child(Path.GetDirectoryName(entry.Path)!, name); if (target == entry.Path) return;
        if (entry.IsDirectory) Directory.Move(entry.Path, target); else File.Move(entry.Path, target, false);
    }
    public static void Transfer(LocalEntry entry, string destination, bool move)
    {
        var target = Child(destination, entry.Name);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("The destination already contains this name. Rename it first.");
        var source = Resolve(entry.Path).TrimEnd(Path.DirectorySeparatorChar);
        if (entry.IsDirectory && (target.Equals(source, StringComparison.OrdinalIgnoreCase) || target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new IOException("A folder cannot be placed inside itself.");
        if (entry.IsLink) throw new IOException("Use Windows Explorer to transfer symbolic links or junctions.");
        if (move) { if (entry.IsDirectory) Directory.Move(source, target); else File.Move(source, target); return; }
        if (!entry.IsDirectory) { File.Copy(source, target, false); return; }
        // Preflight links and enumeration before creating the destination.
        var all = EnumerateTree(source).ToArray(); Directory.CreateDirectory(target);
        foreach (var item in all) { var to = Path.Combine(target, Path.GetRelativePath(source, item.Path)); if (item.IsDirectory) Directory.CreateDirectory(to); else File.Copy(item.Path, to, false); }
    }
    private static IEnumerable<LocalEntry> EnumerateTree(string path)
    {
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("This folder contains a symbolic link or junction. Use Windows Explorer to copy it.");
            var directory = info.Attributes.HasFlag(FileAttributes.Directory);
            var item = new LocalEntry(info.FullName, info.Name, directory, directory ? 0 : ((FileInfo)info).Length, info.LastWriteTime, false);
            yield return item;
            if (directory) foreach (var child in EnumerateTree(item.Path)) yield return child;
        }
    }
    public static void Recycle(LocalEntry entry)
    {
        if (entry.IsDirectory) FileSystem.DeleteDirectory(entry.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        else FileSystem.DeleteFile(entry.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
    }
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
