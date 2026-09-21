namespace Clockt.Terminal.Parser;

/// <summary>
/// Read-only view over the parser's parameter buffers. Parameter i occupies
/// slots [start[i], start[i+1]); slot start[i] is its value and the rest are
/// ':'-separated subparameters. Missing values read as 0.
/// </summary>
public readonly ref struct CsiParams {
    readonly ReadOnlySpan<int> _values;
    readonly ReadOnlySpan<int> _start;   // length Count + 1

    public CsiParams(ReadOnlySpan<int> values, ReadOnlySpan<int> start) {
        _values = values;
        _start = start;
    }

    public int Count => _start.Length == 0 ? 0 : _start.Length - 1;
    public bool Has(int i) => i >= 0 && i < Count;
    public int Get(int i) => Has(i) ? _values[_start[i]] : 0;
    /// <summary>CSI default rule: a missing or zero parameter means <paramref name="fallback"/>.</summary>
    public int GetOr(int i, int fallback) { var v = Get(i); return v == 0 ? fallback : v; }
    public int SubCount(int i) => Has(i) ? _start[i + 1] - _start[i] - 1 : 0;
    public int Sub(int i, int j) => j >= 0 && j < SubCount(i) ? _values[_start[i] + 1 + j] : 0;
}
