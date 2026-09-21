using System.Text;

namespace Clockt.Terminal;

/// <summary>
/// Grid, cursor, scroll region and tab stops for one buffer. Contains no escape
/// sequence knowledge — <c>TerminalEmulator</c> translates sequences into these
/// operations. Every mutation bumps <see cref="Version"/> and stamps it on the
/// touched rows so a renderer can redraw only what changed.
/// </summary>
public sealed class Screen {
    Line[] _lines;

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public bool IsAlternate { get; }
    public GraphemePool Graphemes { get; } = new();
    public Cursor Cursor;
    public int ScrollTop { get; private set; }
    public int ScrollBottom { get; private set; }
    public bool[] TabStops { get; private set; }
    public bool AutoWrap = true;
    public bool InsertMode;
    public bool OriginMode;
    public ulong Version { get; private set; }
    /// <summary>Receives lines scrolled off the top of a region that starts at row 0 (primary screen only).</summary>
    public Action<Line>? LineEvicted;

    public Screen(int cols, int rows, bool isAlternate = false) {
        Cols = Math.Max(1, cols);
        Rows = Math.Max(1, rows);
        IsAlternate = isAlternate;
        _lines = new Line[Rows];
        for (int r = 0; r < Rows; r++) _lines[r] = new Line(Cols);
        ScrollTop = 0; ScrollBottom = Rows - 1;
        TabStops = DefaultTabStops(Cols);
    }

    static bool[] DefaultTabStops(int cols) {
        var t = new bool[cols];
        for (int i = 8; i < cols; i += 8) t[i] = true;
        return t;
    }

    public Line this[int row] => _lines[row];
    public Line CurrentLine => _lines[Cursor.Row];

    /// <summary>Background-color-erase: erased cells keep only the current background.</summary>
    public CellStyle EraseStyle => new(TermColor.Default, Cursor.Style.Bg, TermColor.Default, StyleFlags.None, UnderlineStyle.None);

    void Touch(Line line) => line.Version = ++Version;
    void TouchRows(int from, int to) { for (int r = from; r <= to; r++) _lines[r].Version = ++Version; }

    // ---- writing ------------------------------------------------------------

    public void Put(uint content, int width, CellStyle style, ushort hyperlink) {
        if (width < 1) return;
        if (width == 2 && Cols < 2) width = 1;
        if (Cursor.PendingWrap) {
            Cursor.PendingWrap = false;
            if (AutoWrap) { CurrentLine.Wrapped = true; Touch(CurrentLine); Cursor.Col = 0; LineFeed(); }
        }
        if (width == 2 && Cursor.Col == Cols - 1) {
            var edge = CurrentLine;
            ClearWideRemnants(edge, Cursor.Col, 1);
            edge.Cells[Cursor.Col] = Cell.Blank(style);
            Touch(edge);
            if (!AutoWrap) return;   // no room and no wrapping: the glyph is dropped
            edge.Wrapped = true; Cursor.Col = 0; LineFeed();
        }
        var line = CurrentLine;
        if (InsertMode) ShiftRight(line, Cursor.Col, width);
        ClearWideRemnants(line, Cursor.Col, width);
        line.Cells[Cursor.Col] = new Cell(content, style, hyperlink, width == 2 ? CellFlags.Wide : CellFlags.None);
        if (width == 2) line.Cells[Cursor.Col + 1] = new Cell(0, style, hyperlink, CellFlags.WideContinuation);
        Touch(line);
        Cursor.Col += width;
        if (Cursor.Col >= Cols) { Cursor.Col = Cols - 1; Cursor.PendingWrap = true; }
    }

    /// <summary>Attach a zero-width scalar (combining mark, ZWJ, selector) to the most recently written cell.</summary>
    public bool TryAppendGrapheme(Rune r) {
        int col = Cursor.PendingWrap ? Cursor.Col : Cursor.Col - 1;
        if (col < 0) return false;
        var line = CurrentLine;
        var cell = line.Cells[col];
        if (cell.IsWideContinuation) { col--; if (col < 0) return false; cell = line.Cells[col]; }
        if (cell.Content == 0) return false;
        string existing = cell.IsGrapheme ? Graphemes.Get(cell.GraphemeIndex) : cell.Rune.ToString();
        var idx = Graphemes.Intern(existing + r.ToString());
        line.Cells[col] = cell with { Content = Cell.GraphemeBit | idx };
        Touch(line);
        return true;
    }

    public string ClusterAt(int row, int col) {
        var c = _lines[row].Cells[col];
        if (c.IsWideContinuation && col > 0) c = _lines[row].Cells[col - 1];
        if (c.Content == 0) return " ";
        return c.IsGrapheme ? Graphemes.Get(c.GraphemeIndex) : c.Rune.ToString();
    }

    // Overwriting either half of a wide glyph blanks the other half.
    static void ClearWideRemnants(Line line, int col, int width) {
        var cells = line.Cells;
        if (cells[col].IsWideContinuation && col > 0) cells[col - 1] = Cell.Blank(cells[col - 1].Style);
        int last = col + width - 1;
        if (last < cells.Length && cells[last].IsWide && last + 1 < cells.Length)
            cells[last + 1] = Cell.Blank(cells[last + 1].Style);
    }

    void ShiftRight(Line line, int col, int n) {
        var cells = line.Cells;
        n = Math.Min(n, Cols - col);
        if (n <= 0) return;
        ClearWideRemnants(line, col, 1);
        Array.Copy(cells, col, cells, col + n, Cols - col - n);
        var blank = Cell.Blank(EraseStyle);
        for (int i = col; i < col + n; i++) cells[i] = blank;
        if (cells[Cols - 1].IsWide) cells[Cols - 1] = Cell.Blank(cells[Cols - 1].Style);
        Touch(line);
    }

    void ShiftLeft(Line line, int col, int n) {
        var cells = line.Cells;
        n = Math.Min(n, Cols - col);
        if (n <= 0) return;
        ClearWideRemnants(line, col, n);
        Array.Copy(cells, col + n, cells, col, Cols - col - n);
        var blank = Cell.Blank(EraseStyle);
        for (int i = Cols - n; i < Cols; i++) cells[i] = blank;
        if (cells[col].IsWideContinuation) cells[col] = Cell.Blank(cells[col].Style);
        Touch(line);
    }

    // ---- cursor -------------------------------------------------------------

    public void MoveTo(int row, int col) {
        int top = OriginMode ? ScrollTop : 0;
        int bottom = OriginMode ? ScrollBottom : Rows - 1;
        Cursor.Row = Math.Clamp(top + row, top, bottom);
        Cursor.Col = Math.Clamp(col, 0, Cols - 1);
        Cursor.PendingWrap = false;
    }

    public void SetRow(int row) => MoveTo(row, Cursor.Col);
    public void SetColumn(int col) { Cursor.Col = Math.Clamp(col, 0, Cols - 1); Cursor.PendingWrap = false; }

    public void MoveUp(int n) {
        int top = Cursor.Row >= ScrollTop ? ScrollTop : 0;
        Cursor.Row = Math.Max(top, Cursor.Row - Math.Max(1, n));
        Cursor.PendingWrap = false;
    }

    public void MoveDown(int n) {
        int bottom = Cursor.Row <= ScrollBottom ? ScrollBottom : Rows - 1;
        Cursor.Row = Math.Min(bottom, Cursor.Row + Math.Max(1, n));
        Cursor.PendingWrap = false;
    }

    public void MoveLeft(int n) { Cursor.Col = Math.Max(0, Cursor.Col - Math.Max(1, n)); Cursor.PendingWrap = false; }
    public void MoveRight(int n) { Cursor.Col = Math.Min(Cols - 1, Cursor.Col + Math.Max(1, n)); Cursor.PendingWrap = false; }
    public void CarriageReturn() { Cursor.Col = 0; Cursor.PendingWrap = false; }
    public void Backspace() { Cursor.PendingWrap = false; if (Cursor.Col > 0) Cursor.Col--; }

    public void LineFeed() {
        Cursor.PendingWrap = false;
        if (Cursor.Row == ScrollBottom) ScrollUp(1);
        else if (Cursor.Row < Rows - 1) Cursor.Row++;
    }

    public void ReverseIndex() {
        Cursor.PendingWrap = false;
        if (Cursor.Row == ScrollTop) ScrollDown(1);
        else if (Cursor.Row > 0) Cursor.Row--;
    }

    // ---- tabs ---------------------------------------------------------------

    public void Tab() {
        Cursor.PendingWrap = false;
        int c = Cursor.Col + 1;
        while (c < Cols && !TabStops[c]) c++;
        Cursor.Col = Math.Min(c, Cols - 1);
    }

    public void BackTab() {
        Cursor.PendingWrap = false;
        int c = Cursor.Col - 1;
        while (c > 0 && !TabStops[c]) c--;
        Cursor.Col = Math.Max(c, 0);
    }

    public void SetTabStop() => TabStops[Cursor.Col] = true;
    public void ClearTabStop() => TabStops[Cursor.Col] = false;
    public void ClearAllTabStops() => Array.Clear(TabStops);
    public void ResetTabStops() => TabStops = DefaultTabStops(Cols);

    internal void SetTabStops(bool[] stops) {
        var next = new bool[Cols];
        Array.Copy(stops, next, Math.Min(Cols, stops.Length));
        TabStops = next;
    }

    /// <summary>Restore margins without the DECSTBM cursor homing.</summary>
    internal void SetScrollRegionRaw(int top, int bottom) {
        if (top < 0 || bottom >= Rows || top >= bottom) { top = 0; bottom = Rows - 1; }
        ScrollTop = top; ScrollBottom = bottom;
    }

    // ---- scrolling ----------------------------------------------------------

    /// <summary>0-based inclusive margins; an invalid pair resets to the full screen. Homes the cursor (DECSTBM).</summary>
    public void SetScrollRegion(int top, int bottom) {
        if (top < 0 || bottom >= Rows || top >= bottom) { top = 0; bottom = Rows - 1; }
        ScrollTop = top; ScrollBottom = bottom;
        MoveTo(0, 0);
    }

    public void ScrollUp(int n) => ScrollRegionUp(ScrollTop, ScrollBottom, n, evictToScrollback: true);
    public void ScrollDown(int n) => ScrollRegionDown(ScrollTop, ScrollBottom, n);

    public void InsertLines(int n) {
        if (Cursor.Row < ScrollTop || Cursor.Row > ScrollBottom) return;
        ScrollRegionDown(Cursor.Row, ScrollBottom, n);
        Cursor.PendingWrap = false;
    }

    public void DeleteLines(int n) {
        if (Cursor.Row < ScrollTop || Cursor.Row > ScrollBottom) return;
        ScrollRegionUp(Cursor.Row, ScrollBottom, n, evictToScrollback: false);
        Cursor.PendingWrap = false;
    }

    void ScrollRegionUp(int top, int bottom, int n, bool evictToScrollback) {
        int height = bottom - top + 1;
        n = Math.Clamp(n, 1, height);
        bool evict = evictToScrollback && !IsAlternate && top == 0 && LineEvicted != null;
        var style = EraseStyle;
        for (int i = 0; i < n; i++) {
            var removed = _lines[top];
            Array.Copy(_lines, top + 1, _lines, top, height - 1);
            if (evict) { LineEvicted!(removed); _lines[bottom] = new Line(Cols, style); }
            else { removed.Clear(style); _lines[bottom] = removed; }
        }
        TouchRows(top, bottom);
    }

    void ScrollRegionDown(int top, int bottom, int n) {
        int height = bottom - top + 1;
        n = Math.Clamp(n, 1, height);
        var style = EraseStyle;
        for (int i = 0; i < n; i++) {
            var removed = _lines[bottom];
            Array.Copy(_lines, top, _lines, top + 1, height - 1);
            removed.Clear(style);
            _lines[top] = removed;
        }
        TouchRows(top, bottom);
    }

    // ---- erasing ------------------------------------------------------------

    public void EraseInDisplay(int mode) {
        var s = EraseStyle;
        switch (mode) {
            case 0:
                EraseInLine(0);
                for (int r = Cursor.Row + 1; r < Rows; r++) _lines[r].Clear(s);
                TouchRows(Cursor.Row, Rows - 1);
                break;
            case 1:
                EraseInLine(1);
                for (int r = 0; r < Cursor.Row; r++) _lines[r].Clear(s);
                TouchRows(0, Cursor.Row);
                break;
            default:   // 2 and 3 — clearing scrollback for 3 is the emulator's job
                foreach (var l in _lines) l.Clear(s);
                TouchRows(0, Rows - 1);
                break;
        }
        Cursor.PendingWrap = false;
    }

    public void EraseInLine(int mode) {
        var line = CurrentLine;
        switch (mode) {
            case 0: ClearCells(line, Cursor.Col, Cols); break;
            case 1: ClearCells(line, 0, Cursor.Col + 1); break;
            default: ClearCells(line, 0, Cols); break;
        }
        Touch(line);
        Cursor.PendingWrap = false;
    }

    public void EraseChars(int n) {
        var line = CurrentLine;
        ClearCells(line, Cursor.Col, Math.Min(Cols, Cursor.Col + Math.Max(1, n)));
        Touch(line);
        Cursor.PendingWrap = false;
    }

    public void InsertChars(int n) { ShiftRight(CurrentLine, Cursor.Col, Math.Max(1, n)); Cursor.PendingWrap = false; }
    public void DeleteChars(int n) { ShiftLeft(CurrentLine, Cursor.Col, Math.Max(1, n)); Cursor.PendingWrap = false; }

    void ClearCells(Line line, int from, int toExclusive) {
        var cells = line.Cells;
        if (from > 0 && cells[from].IsWideContinuation) cells[from - 1] = Cell.Blank(cells[from - 1].Style);
        if (toExclusive < Cols && cells[toExclusive].IsWideContinuation) cells[toExclusive] = Cell.Blank(cells[toExclusive].Style);
        var blank = Cell.Blank(EraseStyle);
        for (int i = from; i < toExclusive; i++) cells[i] = blank;
    }

    // ---- geometry -----------------------------------------------------------

    /// <summary>Plain resize (no reflow): keeps the top-left content, drops rows from the top only as far as needed to keep the cursor visible.</summary>
    public void Resize(int cols, int rows) {
        cols = Math.Max(1, cols); rows = Math.Max(1, rows);
        int drop = Math.Max(0, Cursor.Row - (rows - 1));
        var next = new Line[rows];
        for (int r = 0; r < drop; r++) {
            if (!IsAlternate && LineEvicted != null) LineEvicted(_lines[r]);
        }
        for (int r = 0; r < rows; r++) {
            int src = r + drop;
            if (src < Rows) { var l = _lines[src]; l.Resize(cols, CellStyle.Default); next[r] = l; }
            else next[r] = new Line(cols);
        }
        var stops = new bool[cols];
        Array.Copy(TabStops, stops, Math.Min(cols, TabStops.Length));
        for (int i = (TabStops.Length / 8 + 1) * 8; i < cols; i += 8) stops[i] = true;
        TabStops = stops;
        _lines = next; Cols = cols; Rows = rows;
        ScrollTop = 0; ScrollBottom = rows - 1;
        Cursor.Row = Math.Clamp(Cursor.Row - drop, 0, rows - 1);
        Cursor.Col = Math.Clamp(Cursor.Col, 0, cols - 1);
        Cursor.PendingWrap = false;
        TouchRows(0, rows - 1);
    }

    /// <summary>Replace the grid wholesale (reflow and snapshot restore). All lines must share one width.</summary>
    public void Load(Line[] lines) {
        if (lines.Length == 0) return;
        _lines = lines; Rows = lines.Length; Cols = Math.Max(1, lines[0].Length);
        ScrollTop = 0; ScrollBottom = Rows - 1;
        if (TabStops.Length != Cols) { var stops = DefaultTabStops(Cols); Array.Copy(TabStops, stops, Math.Min(Cols, TabStops.Length)); TabStops = stops; }
        Cursor.Row = Math.Clamp(Cursor.Row, 0, Rows - 1);
        Cursor.Col = Math.Clamp(Cursor.Col, 0, Cols - 1);
        Cursor.PendingWrap = false;
        TouchRows(0, Rows - 1);
    }
}
