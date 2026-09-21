using System.Text;

namespace Clockt.Terminal;

public sealed partial class TerminalEmulator {
    public const int SyncOutputTimeoutMs = 150;

    long _syncStartedMs = -1;

    /// <summary>Millisecond clock used for the synchronized-output timeout. Tests inject a fake.</summary>
    public Func<long> Clock { get; set; } = static () => Environment.TickCount64;

    /// <summary>
    /// True while an application has begun a synchronized update (DECSET 2026) less
    /// than <see cref="SyncOutputTimeoutMs"/> ago. Renderers skip painting while this
    /// holds so half-drawn frames never show; the timeout guards against apps that
    /// forget to end the update.
    /// </summary>
    public bool IsRenderDeferred {
        get {
            if (!Modes.SyncOutput || _syncStartedMs < 0) return false;
            return Clock() - _syncStartedMs < SyncOutputTimeoutMs;
        }
    }

    void SyncOutputChanged(bool on) => _syncStartedMs = on ? Clock() : -1;

    public bool HasChangedSince(ulong generation, ulong version) => Generation != generation || Version != version;

    /// <summary>Row indexes whose content changed after <paramref name="sinceVersion"/>. Returns the full count even when <paramref name="rows"/> is too small.</summary>
    public int DirtyRows(ulong sinceVersion, Span<int> rows) {
        int n = 0;
        for (int r = 0; r < _active.Rows; r++) {
            if (_active[r].Version <= sinceVersion) continue;
            if (n < rows.Length) rows[n] = r;
            n++;
        }
        return n;
    }

    public string DumpScreen() => DumpScreen(0);

    /// <summary>
    /// The visible grid as text, preceded by the last <paramref name="scrollbackLines"/> lines that scrolled off
    /// (oldest first). Automation reads this without attaching, so it never disturbs what runs in the session.
    /// </summary>
    public string DumpScreen(int scrollbackLines) {
        lock (SyncRoot) {
            var sb = new StringBuilder();
            int tail = Math.Clamp(scrollbackLines, 0, _scrollback.Count);
            // Scrollback is evicted from the primary screen, so its graphemes are interned in that pool.
            for (int i = _scrollback.Count - tail; i < _scrollback.Count; i++) sb.Append(_scrollback[i].ToText(_primary.Graphemes)).Append('\n');
            for (int r = 0; r < _active.Rows; r++) sb.Append(_active[r].ToText(_active.Graphemes)).Append('\n');
            return sb.ToString();
        }
    }
}
