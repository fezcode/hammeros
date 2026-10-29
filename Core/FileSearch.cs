namespace HammerOS.Core;

public static class FileSearch
{
    public static IReadOnlyList<string> Build(IEnumerable<string> roots, CancellationToken cancellation = default)
    {
        var result = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var pending = new Queue<(string Path, int Depth)>(roots.Where(Directory.Exists).Select(x => (x, 0)));
        while (pending.TryDequeue(out var dir) && result.Count < 20000)
        {
            cancellation.ThrowIfCancellationRequested(); if (!seen.Add(dir.Path)) continue;
            try
            {
                if (File.GetAttributes(dir.Path).HasFlag(FileAttributes.ReparsePoint)) continue;
                foreach (var entry in new DirectoryInfo(dir.Path).EnumerateFileSystemInfos())
                { cancellation.ThrowIfCancellationRequested(); if ((entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0) continue; result.Add(entry.FullName); if (result.Count >= 20000) break; if (entry is DirectoryInfo && dir.Depth < 8) pending.Enqueue((entry.FullName, dir.Depth + 1)); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }
}
