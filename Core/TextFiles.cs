using System.Text;

namespace HammerOS.Core;

public static class TextFiles
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".txt", ".txtt", ".text", ".md", ".markdown", ".log", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".conf", ".config", ".cs", ".css", ".html", ".htm", ".js", ".ts", ".tsx", ".jsx", ".py", ".go", ".rs", ".sql", ".ps1", ".sh" };
    private static readonly HashSet<string> WindowsLaunchers = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".com", ".bat", ".cmd", ".lnk", ".url", ".msi", ".appref-ms" };
    public static async Task<bool> IsDocumentAsync(string path)
    {
        var extension = Path.GetExtension(path);
        if (Extensions.Contains(extension)) return true;
        if (WindowsLaunchers.Contains(extension) || new FileInfo(path).Length > 8 * 1024 * 1024) return false;
        // Unfamiliar extensions can still be plain text. Inspect a bounded file,
        // preserving Windows launching for executable and shortcut formats.
        var bytes = await File.ReadAllBytesAsync(path);
        try { var text = new UTF8Encoding(false, true).GetString(bytes); return !text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n' or '\f')); }
        catch (DecoderFallbackException) { return false; }
    }
}
