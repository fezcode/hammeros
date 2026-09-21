using System.Text;
using Clockt.Terminal.Parser;

namespace Clockt.Terminal;

/// <summary>
/// The terminal state machine: owns the parser, both screens, the scrollback,
/// the reply queue and the event surface. All mutation happens under
/// <see cref="SyncRoot"/>; renderers read under the same lock.
/// </summary>
public sealed partial class TerminalEmulator : IVtActions {
    readonly VtParser _parser;
    readonly Screen _primary;
    readonly Screen _alternate;
    Screen _active;
    readonly Scrollback _scrollback;
    readonly List<byte> _responses = new();
    SavedCursor _savedPrimary, _savedAlternate;
    bool _g0Dec, _g1Dec, _useG1;
    bool _lastWasZwj;

    // DEC Special Graphics for 0x5F..0x7E when a charset is designated '0'.
    const string DecGraphics = " ◆▒␉␌␍␊°±␤␋┘┐┌└┼⎺⎻─⎼⎽├┤┴┬│≤≥π≠£·";

    public TerminalModes Modes = TerminalModes.Default;
    public CursorStyle CursorStyle { get; private set; }
    public string Title { get; private set; } = "";
    public string? Cwd { get; private set; }
    public ProgressState Progress { get; private set; }
    public HyperlinkPool Hyperlinks { get; } = new();
    public object SyncRoot { get; } = new();
    /// <summary>When true, replies (DA/DSR/CPR/OSC queries) are discarded — the mirroring host never answers the shell.</summary>
    public bool DropResponses { get; set; }
    /// <summary>Bumped whenever the whole surface changes identity (screen switch, reset). Renderers repaint fully when it changes.</summary>
    public ulong Generation { get; private set; }

    public event Action? Bell;
    public event Action? ModeChanged;
    public event Action<string>? TitleChanged;
    public event Action<string?>? CwdChanged;
    public event Action<string, string>? Notify;
    public event Action<ProgressState>? ProgressChanged;
    public event Action<string>? ClipboardWrite;
    public event Action<PromptMarkKind>? PromptMark;
    /// <summary>Decoded text/control observations, delivered under SyncRoot. Subscribers must remain lightweight.</summary>
    public event Action<Rune>? Printed;
    public event Action<byte>? ControlExecuted;
    public string? ShellCommandText { get; private set; }
    public int? LastCommandExitCode { get; private set; }
    public event Action<CursorStyle>? CursorStyleChanged;
    public event Action<int, byte, byte, byte>? PaletteSet;
    /// <summary>Answers OSC 4/10/11/12 queries. Index 0–255 palette, 256 foreground, 257 background, 258 cursor. Null result = no reply.</summary>
    public Func<int, (byte R, byte G, byte B)?>? ColorProvider { get; set; }
    /// <summary>Pixel size of one cell, supplied by the host renderer; used for XTWINOPS 14/16 replies.</summary>
    public int CellWidthPx { get; set; } = 8;
    public int CellHeightPx { get; set; } = 16;

    public TerminalEmulator(int cols, int rows, int scrollbackLines = 10000) {
        _primary = new Screen(cols, rows);
        _alternate = new Screen(cols, rows, isAlternate: true);
        _scrollback = new Scrollback(scrollbackLines);
        _primary.LineEvicted = _scrollback.Push;
        _active = _primary;
        _parser = new VtParser(this);
    }

    public Screen Screen => _active;
    public Screen Primary => _primary;
    public Screen Alternate => _alternate;
    public Scrollback Scrollback => _scrollback;
    public int Cols => _active.Cols;
    public int Rows => _active.Rows;
    public bool IsAlternate => _active.IsAlternate;
    public Cursor Cursor => _active.Cursor;
    public ulong Version => _active.Version;

    public void Feed(ReadOnlySpan<byte> data) {
        lock (SyncRoot) _parser.Feed(data);
    }

    public void Feed(string text) => Feed(Encoding.UTF8.GetBytes(text));

    public void Resize(int cols, int rows) {
        lock (SyncRoot) {
            cols = Math.Max(1, cols); rows = Math.Max(1, rows);
            Reflow.Resize(_primary, _scrollback, cols, rows);
            _alternate.Resize(cols, rows);
        }
    }

    public byte[] TakeResponses() {
        lock (SyncRoot) {
            var bytes = _responses.ToArray();
            _responses.Clear();
            return bytes;
        }
    }

    public int PendingResponseBytes => _responses.Count;

    void Respond(string reply) {
        if (DropResponses) return;
        _responses.AddRange(Encoding.ASCII.GetBytes(reply));
    }

    /// <summary>RIS: everything back to power-on except scrollback and title.</summary>
    public void Reset() {
        lock (SyncRoot) {
            _active = _primary;
            Modes = TerminalModes.Default;
            foreach (var s in new[] { _primary, _alternate }) {
                s.Cursor = default;
                s.AutoWrap = true; s.InsertMode = false; s.OriginMode = false;
                s.SetScrollRegion(0, s.Rows - 1);
                s.EraseInDisplay(2);
                s.ResetTabStops();
            }
            _savedPrimary = default; _savedAlternate = default;
            _g0Dec = _g1Dec = _useG1 = false;
            _lastWasZwj = false;
            SetCursorStyle(CursorStyle.Default);
            Generation++;
            ModeChanged?.Invoke();
        }
    }

    /// <summary>DECSTR: modes, margins, charsets and saved cursors — content stays.</summary>
    public void SoftReset() {
        var keepAlt = Modes.AltScreen;
        Modes = TerminalModes.Default;
        Modes.AltScreen = keepAlt;
        foreach (var s in new[] { _primary, _alternate }) {
            s.AutoWrap = true; s.InsertMode = false; s.OriginMode = false;
            var (row, col) = (s.Cursor.Row, s.Cursor.Col);
            s.SetScrollRegion(0, s.Rows - 1);
            s.Cursor.Row = row; s.Cursor.Col = col;
            s.Cursor.Style = CellStyle.Default; s.Cursor.HyperlinkId = 0;
        }
        _savedPrimary = default; _savedAlternate = default;
        _g0Dec = _g1Dec = _useG1 = false;
        SetCursorStyle(CursorStyle.Default);
        ModeChanged?.Invoke();
    }

    void SetCursorStyle(CursorStyle style) {
        if (CursorStyle == style) return;
        CursorStyle = style;
        CursorStyleChanged?.Invoke(style);
    }

    // ---- IVtActions: printing and C0/C1 ----------------------------------------

    public void Print(Rune r) {
        Printed?.Invoke(r);
        int width = Wcwidth.Width(r);
        if (width == 0) {
            if (r.Value >= 0x20 && _active.TryAppendGrapheme(r)) { _lastWasZwj = Wcwidth.IsZwj(r); return; }
            _lastWasZwj = false;
            return;   // unattached zero-width scalar: dropped
        }
        if (_lastWasZwj) {
            _lastWasZwj = false;
            if (_active.TryAppendGrapheme(r)) return;
        }
        uint content = (uint)r.Value;
        bool dec = _useG1 ? _g1Dec : _g0Dec;
        if (dec && r.Value >= 0x5F && r.Value <= 0x7E) content = DecGraphics[r.Value - 0x5F];
        _active.Put(content, width, _active.Cursor.Style, _active.Cursor.HyperlinkId);
    }

    public void Execute(byte control) {
        ControlExecuted?.Invoke(control);
        _lastWasZwj = false;
        switch (control) {
            case 0x07: Bell?.Invoke(); break;
            case 0x08: _active.Backspace(); break;
            case 0x09: _active.Tab(); break;
            case 0x0A: case 0x0B: case 0x0C: LineFeed(); break;
            case 0x0D: _active.CarriageReturn(); break;
            case 0x0E: _useG1 = true; break;
            case 0x0F: _useG1 = false; break;
            case 0x84: _active.LineFeed(); break;                               // IND
            case 0x85: _active.CarriageReturn(); _active.LineFeed(); break;     // NEL
            case 0x88: _active.SetTabStop(); break;                             // HTS
            case 0x8D: _active.ReverseIndex(); break;                           // RI
        }
    }

    void LineFeed() {
        if (Modes.NewLine) _active.CarriageReturn();
        _active.LineFeed();
    }

    // ---- cursor save/restore (DECSC/DECRC, CSI s/u, ?1048) ----------------------

    void SaveCursor() {
        var c = _active.Cursor;
        var saved = new SavedCursor {
            Row = c.Row, Col = c.Col, Style = c.Style, HyperlinkId = c.HyperlinkId,
            OriginMode = _active.OriginMode, G0Dec = _g0Dec, G1Dec = _g1Dec, UseG1 = _useG1, Valid = true
        };
        if (_active.IsAlternate) _savedAlternate = saved; else _savedPrimary = saved;
    }

    void RestoreCursor() {
        var saved = _active.IsAlternate ? _savedAlternate : _savedPrimary;
        if (!saved.Valid) {
            _active.OriginMode = false; Modes.Origin = false;
            _active.MoveTo(0, 0);
            _active.Cursor.Style = CellStyle.Default; _active.Cursor.HyperlinkId = 0;
            return;
        }
        _active.OriginMode = saved.OriginMode; Modes.Origin = saved.OriginMode;
        _active.Cursor.Row = Math.Clamp(saved.Row, 0, _active.Rows - 1);
        _active.Cursor.Col = Math.Clamp(saved.Col, 0, _active.Cols - 1);
        _active.Cursor.PendingWrap = false;
        _active.Cursor.Style = saved.Style;
        _active.Cursor.HyperlinkId = saved.HyperlinkId;
        _g0Dec = saved.G0Dec; _g1Dec = saved.G1Dec; _useG1 = saved.UseG1;
    }

    // ---- screen switching -------------------------------------------------------

    void SwitchScreen(bool toAlternate, bool saveCursor, bool clear) {
        if (toAlternate == _active.IsAlternate) return;
        if (toAlternate) {
            if (saveCursor) SaveCursor();
            _alternate.AutoWrap = _primary.AutoWrap;
            _alternate.InsertMode = _primary.InsertMode;
            _alternate.OriginMode = _primary.OriginMode;
            _alternate.Cursor = _primary.Cursor;
            _active = _alternate;
            Modes.AltScreen = true;
            if (clear) _alternate.EraseInDisplay(2);
        } else {
            if (clear) _alternate.EraseInDisplay(2);
            _primary.AutoWrap = _alternate.AutoWrap;
            _primary.InsertMode = _alternate.InsertMode;
            _active = _primary;
            Modes.AltScreen = false;
            if (saveCursor) RestoreCursor();
        }
        Generation++;
    }
}
