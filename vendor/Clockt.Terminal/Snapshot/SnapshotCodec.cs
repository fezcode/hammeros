using System.Text;

namespace Clockt.Terminal.Snapshot;

/// <summary>
/// Compact binary image of a <see cref="TerminalEmulator"/> — what the daemon
/// sends a client on attach so its mirror starts from the same state.
/// </summary>
public static class SnapshotCodec {
    public const string Magic = "CKSN";
    public const byte Version = 1;

    public static void Write(TerminalEmulator t, Stream s, int scrollbackTail) {
        using var w = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes(Magic));
        w.Write(Version);
        t.WriteState(w, scrollbackTail);
        w.Flush();
    }

    public static byte[] Write(TerminalEmulator t, int scrollbackTail) {
        using var ms = new MemoryStream();
        Write(t, ms, scrollbackTail);
        return ms.ToArray();
    }

    public static TerminalEmulator Read(Stream s, int scrollbackCapacity = 10000) {
        using var r = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
        var magic = r.ReadBytes(4);
        if (magic.Length != 4 || Encoding.ASCII.GetString(magic) != Magic) throw new InvalidDataException("not a clockt snapshot");
        var version = r.ReadByte();
        if (version != Version) throw new InvalidDataException($"unsupported snapshot version {version}");
        int cols = r.ReadInt32(), rows = r.ReadInt32();
        if (cols < 1 || cols > 10000 || rows < 1 || rows > 10000) throw new InvalidDataException("implausible snapshot size");
        s.Seek(-8, SeekOrigin.Current);
        var t = new TerminalEmulator(cols, rows, scrollbackCapacity);
        t.ReadState(r);
        return t;
    }

    public static TerminalEmulator Read(byte[] bytes, int scrollbackCapacity = 10000) {
        using var ms = new MemoryStream(bytes);
        return Read(ms, scrollbackCapacity);
    }
}
