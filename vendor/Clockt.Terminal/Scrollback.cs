namespace Clockt.Terminal;

/// <summary>Fixed-capacity ring of lines that scrolled off the primary screen; index 0 is the oldest.</summary>
public sealed class Scrollback {
    Line[] _buf;
    int _head;    // index of the oldest line
    int _count;

    public Scrollback(int capacity = 10000) {
        _buf = new Line[Math.Max(1, capacity)];
    }

    public int Capacity => _buf.Length;
    public int Count => _count;

    public Line this[int index] {
        get {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _buf[(_head + index) % _buf.Length];
        }
    }

    public void Push(Line line) {
        if (_count == _buf.Length) {
            _buf[_head] = line;
            _head = (_head + 1) % _buf.Length;
        } else {
            _buf[(_head + _count) % _buf.Length] = line;
            _count++;
        }
    }

    public Line? PopNewest() {
        if (_count == 0) return null;
        int idx = (_head + _count - 1) % _buf.Length;
        var line = _buf[idx];
        _buf[idx] = null!;
        _count--;
        return line;
    }

    public void Clear() { Array.Clear(_buf); _head = 0; _count = 0; }

    public void SetCapacity(int capacity) {
        capacity = Math.Max(1, capacity);
        if (capacity == _buf.Length) return;
        var keep = Math.Min(_count, capacity);
        var next = new Line[capacity];
        for (int i = 0; i < keep; i++) next[i] = this[_count - keep + i];
        _buf = next; _head = 0; _count = keep;
    }

    public Line[] ToArray() {
        var arr = new Line[_count];
        for (int i = 0; i < _count; i++) arr[i] = this[i];
        return arr;
    }

    public void Load(IEnumerable<Line> lines) {
        Clear();
        foreach (var l in lines) Push(l);
    }
}
