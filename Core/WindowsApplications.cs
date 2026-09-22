namespace HammerOS.Core;

public sealed record WindowsApplication(string Name, string Shortcut);

public static class WindowsApplications
{
    public static Task<IReadOnlyList<WindowsApplication>> LoadAsync() => Task.Run<IReadOnlyList<WindowsApplication>>(() =>
    {
        var apps = new List<WindowsApplication>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    if (Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".appref-ms") apps.Add(new(Path.GetFileNameWithoutExtension(path), path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return apps.DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    });
}
