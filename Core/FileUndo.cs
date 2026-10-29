using System.Security.Cryptography;
using System.Text;

namespace HammerOS.Core;

// Receipts describe only paths created by this operation. Changed contents block undo.
public sealed class FileUndo
{
    private readonly string _source, _target, _fingerprint;
    private readonly bool _move, _directory;
    public FileUndo(string source, string target, bool move)
    { _source = Path.GetFullPath(source); _target = Path.GetFullPath(target); _move = move; _directory = Directory.Exists(target); _fingerprint = Fingerprint(_target); }
    public void Verify()
    {
        if (_move && (File.Exists(_source) || Directory.Exists(_source))) throw new IOException("Undo would overwrite the original path.");
        if (Fingerprint(_target) != _fingerprint) throw new IOException("The transferred item changed. Undo has been blocked to preserve your edits.");
    }
    public void Undo()
    {
        Verify();
        if (_move) { if (_directory) Directory.Move(_target, _source); else File.Move(_target, _source); }
        else LocalFiles.Recycle(new(_target, Path.GetFileName(_target), _directory, 0, DateTime.Now, false));
    }
    private static string Fingerprint(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Undo through a link is not supported.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = Directory.Exists(path) ? LocalFiles.Tree(path).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray() : [new LocalEntry(path, Path.GetFileName(path), false, 0, DateTime.Now, false)];
        foreach (var item in entries) { hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(path, item.Path) + (item.IsDirectory ? "/" : ":"))); if (!item.IsDirectory) { using var file = File.OpenRead(item.Path); hash.AppendData(SHA256.HashData(file)); } }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
