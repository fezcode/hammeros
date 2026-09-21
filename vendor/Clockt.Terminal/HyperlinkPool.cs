namespace Clockt.Terminal;

/// <summary>OSC 8 hyperlinks referenced by cells through a 1-based id (0 = none).</summary>
public sealed class HyperlinkPool {
    readonly List<(string Uri, string Id)> _links = new();
    readonly Dictionary<string, ushort> _byKey = new(StringComparer.Ordinal);

    public int Count => _links.Count;

    public ushort Add(string uri, string? id) {
        id ??= "";
        var key = id.Length > 0 ? "id:" + id : "uri:" + uri;
        if (_byKey.TryGetValue(key, out var existing)) return existing;
        if (_links.Count >= ushort.MaxValue - 1) return 0;
        _links.Add((uri, id));
        var handle = (ushort)_links.Count;
        _byKey[key] = handle;
        return handle;
    }

    public string? Uri(ushort id) => id >= 1 && id <= _links.Count ? _links[id - 1].Uri : null;
    public string? Id(ushort id) => id >= 1 && id <= _links.Count ? _links[id - 1].Id : null;

    public IReadOnlyList<(string Uri, string Id)> Snapshot() => _links.ToArray();

    public void Restore(IEnumerable<(string Uri, string Id)> links) {
        _links.Clear(); _byKey.Clear();
        foreach (var (uri, id) in links) Add(uri, id);
    }
}
