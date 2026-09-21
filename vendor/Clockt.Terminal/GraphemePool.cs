namespace Clockt.Terminal;

/// <summary>
/// Interns multi-codepoint grapheme clusters so a <see cref="Cell"/> can refer to
/// them by index (see <see cref="Cell.GraphemeBit"/>). Owned by a screen; cleared
/// with it. Indices are stable for the pool's lifetime.
/// </summary>
public sealed class GraphemePool {
    readonly List<string> _items = new();
    readonly Dictionary<string, uint> _index = new(StringComparer.Ordinal);

    public int Count => _items.Count;

    public uint Intern(string cluster) {
        if (_index.TryGetValue(cluster, out var i)) return i;
        i = (uint)_items.Count;
        _items.Add(cluster);
        _index[cluster] = i;
        return i;
    }

    public string Get(uint index) => index < _items.Count ? _items[(int)index] : "�";

    public void Clear() { _items.Clear(); _index.Clear(); }

    public IReadOnlyList<string> Snapshot() => _items.ToArray();

    public void Restore(IEnumerable<string> items) {
        Clear();
        foreach (var s in items) Intern(s);
    }
}
