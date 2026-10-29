using System.IO.Compression;

namespace HammerOS.Core;

public static class ArchiveFiles
{
    public static void Extract(string archive, string destination)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(root)) throw new IOException("Choose an existing destination folder.");
        using var zip = ZipFile.OpenRead(archive); long total = 0; var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(entry.FullName) || entry.FullName.Contains(':')) throw new IOException("Archive contains an unsafe path.");
            if (!targets.Add(target) || File.Exists(target)) throw new IOException("Extraction would overwrite an existing or duplicate file.");
            for (var directory = Path.GetDirectoryName(target); directory is not null && directory.Length >= root.Length - 1; directory = Path.GetDirectoryName(directory)) if (Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Extraction through links is not supported.");
            total += entry.Length; if (total > 2L * 1024 * 1024 * 1024 || zip.Entries.Count > 20000) throw new IOException("Archive exceeds the 2 GB / 20,000 entry extraction limit.");
        }
        foreach (var entry in zip.Entries) { var target = Path.GetFullPath(Path.Combine(root, entry.FullName)); if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) Directory.CreateDirectory(target); else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); using var output = new FileStream(target, FileMode.CreateNew); using var input = entry.Open(); input.CopyTo(output); } }
    }
    public static void Create(string output, IEnumerable<string> paths)
    {
        // Enumerate before creating the output, including when it is inside a selected folder.
        var entries = new List<(string Path, string Name, bool Directory)>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in paths)
        {
            var path = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Archiving links is not supported.");
            if (Directory.Exists(path)) { entries.Add((path, Path.GetFileName(path) + "/", true)); foreach (var item in LocalFiles.Tree(path)) entries.Add((item.Path, Path.GetFileName(path) + "/" + Path.GetRelativePath(path, item.Path).Replace('\\', '/') + (item.IsDirectory ? "/" : ""), item.IsDirectory)); }
            else entries.Add((path, Path.GetFileName(path), false));
        }
        if (entries.Any(x => !names.Add(x.Name))) throw new IOException("Selected items contain duplicate archive names.");
        var created = false;
        try { using var stream = new FileStream(output, FileMode.CreateNew); created = true; using var zip = new ZipArchive(stream, ZipArchiveMode.Create); foreach (var entry in entries) { if (entry.Directory) zip.CreateEntry(entry.Name); else zip.CreateEntryFromFile(entry.Path, entry.Name); } }
        catch { if (created) File.Delete(output); throw; }
    }
}
