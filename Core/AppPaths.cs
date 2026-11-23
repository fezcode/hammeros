namespace HammerOS.Core;

/// <summary>
/// The one place HammerOS decides where its per-user data lives. Fezcode apps keep
/// small settings under %APPDATA%\fezcode\&lt;App&gt; and everything else under
/// %LOCALAPPDATA%\fezcode\&lt;App&gt;, so an uninstall purge removes it in one go.
/// HammerOS has no separate settings file: session.json mixes preferences with the
/// virtual files and game progress, and WebView2 keeps a large profile, so both
/// live under the Local root. forge.toml's settings_dirs must match <see cref="LocalData"/>.
/// User content (real files, folders the user picks) is never stored here.
/// </summary>
public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\fezcode\HammerOS — session, WebView2 profile, caches.</summary>
    public static string LocalData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fezcode", Product.Name);

    /// <summary>Virtual files, preferences, refinement/Sudoku progress and calendar.</summary>
    public static string SessionFile => Path.Combine(LocalData, "session.json");

    /// <summary>The WebView2 user data folder for Browser and Media Player.</summary>
    public static string WebView2 => Path.Combine(LocalData, "WebView2");

    /// <summary>Where releases up to 0.3.7 kept everything: %LOCALAPPDATA%\HammerOS.</summary>
    public static string LegacyLocalData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Name);

    /// <summary>
    /// Moves the pre-fezcode data folder into <see cref="LocalData"/>. Runs once at
    /// startup before anything reads the session; safe to call every launch. Never
    /// overwrites a file that already exists at the new location: the new copy wins
    /// and the stale legacy copy is deleted. The old folder is removed once empty;
    /// files locked by a running older instance stay and are retried next launch.
    /// Never throws.
    /// </summary>
    public static void MigrateLegacyData() => MigrateLegacyData(LegacyLocalData, LocalData);

    public static void MigrateLegacyData(string legacy, string target)
    {
        try
        {
            if (!Directory.Exists(legacy)) return;
            var legacyInfo = new DirectoryInfo(legacy);
            if (legacyInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            var legacyFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacy));
            var targetFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
            if (string.Equals(legacyFull, targetFull, StringComparison.OrdinalIgnoreCase)) return;
            // If someone installed the app itself into the legacy folder, that is an
            // install image, not data: leave it alone.
            var baseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            if (IsSameOrInside(baseDir, legacyFull)) return;
            if (File.Exists(Path.Combine(legacyFull, Product.Name + ".exe")) || File.Exists(Path.Combine(legacyFull, "uninstall.exe"))) return;

            Directory.CreateDirectory(Path.GetDirectoryName(targetFull)!);
            if (!Directory.Exists(targetFull))
            {
                // Same volume (the usual case): one atomic rename and we are done.
                try { Directory.Move(legacyFull, targetFull); return; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            Merge(legacyFull, targetFull);
            if (!Directory.EnumerateFileSystemEntries(legacyFull).Any()) Directory.Delete(legacyFull);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"HammerOS: legacy data migration skipped: {ex.Message}");
        }
    }

    private static void Merge(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var to = Path.Combine(destination, Path.GetFileName(file));
            if (Directory.Exists(to)) continue;
            // File.Move renames on one volume and copies-then-deletes across volumes.
            // When both exist the new location wins: the stale legacy copy is deleted.
            try { if (File.Exists(to)) File.Delete(file); else File.Move(file, to); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.WriteLine($"HammerOS: could not migrate {file}: {ex.Message}"); }
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var to = Path.Combine(destination, Path.GetFileName(dir));
            if (File.Exists(to)) continue;
            try
            {
                Merge(dir, to);
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.WriteLine($"HammerOS: could not migrate {dir}: {ex.Message}"); }
        }
    }

    private static bool IsSameOrInside(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
