namespace Clockt.Terminal;

/// <summary>
/// Width-aware resize for the primary screen: soft-wrapped rows are joined into
/// logical lines, re-cut at the new width (never splitting a wide glyph), and
/// the visible window is chosen so the cursor keeps its screen row where
/// possible. Rows above the window go to scrollback, rows below are dropped.
/// </summary>
public static class Reflow {
    public static void Resize(Screen screen, Scrollback scrollback, int cols, int rows) {
        cols = Math.Max(1, cols); rows = Math.Max(1, rows);
        if (cols == screen.Cols && rows == screen.Rows) return;

        int lastUsed = Math.Max(screen.Cursor.Row, LastContentRow(screen));
        var all = new List<Line>(scrollback.Count + lastUsed + 1);
        for (int i = 0; i < scrollback.Count; i++) all.Add(scrollback[i]);
        int screenStart = all.Count;
        for (int r = 0; r <= lastUsed; r++) all.Add(screen[r]);
        int cursorAbs = screenStart + screen.Cursor.Row;
        int cursorCol = screen.Cursor.Col;
        int oldCursorScreenRow = screen.Cursor.Row;

        List<Line> lines; int newCursorAbs, newCursorCol;
        if (cols == screen.Cols) { lines = all; newCursorAbs = cursorAbs; newCursorCol = cursorCol; }
        else Rewrap(all, cols, cursorAbs, cursorCol, out lines, out newCursorAbs, out newCursorCol);

        int total = lines.Count;
        int start = Math.Clamp(newCursorAbs - oldCursorScreenRow, 0, Math.Max(0, total - rows));
        if (newCursorAbs >= start + rows) start = newCursorAbs - rows + 1;

        scrollback.Clear();
        for (int i = 0; i < start; i++) scrollback.Push(lines[i]);
        var grid = new Line[rows];
        for (int r = 0; r < rows; r++) {
            int idx = start + r;
            grid[r] = idx < total ? lines[idx] : new Line(cols);
            if (grid[r].Length != cols) grid[r].Resize(cols, CellStyle.Default);
        }
        screen.Load(grid);
        screen.Cursor.Row = Math.Clamp(newCursorAbs - start, 0, rows - 1);
        screen.Cursor.Col = Math.Clamp(newCursorCol, 0, cols - 1);
    }

    static int LastContentRow(Screen screen) {
        for (int r = screen.Rows - 1; r >= 0; r--)
            if (screen[r].LastNonBlank() >= 0 || screen[r].Wrapped) return r;
        return -1;
    }

    static void Rewrap(List<Line> src, int cols, int cursorAbs, int cursorCol,
                       out List<Line> dst, out int newCursorAbs, out int newCursorCol) {
        dst = new List<Line>(src.Count + 8);
        newCursorAbs = 0; newCursorCol = cursorCol;
        var cells = new List<Cell>();
        int i = 0;
        while (i < src.Count) {
            cells.Clear();
            int cursorOffset = -1;
            var marks = src[i].Marks;
            int j = i;
            while (true) {
                var line = src[j];
                int len = line.Wrapped ? line.Length : line.LastNonBlank() + 1;
                if (j == cursorAbs) {
                    len = Math.Max(len, Math.Min(cursorCol + 1, line.Length));
                    cursorOffset = cells.Count + cursorCol;
                }
                for (int c = 0; c < len; c++) cells.Add(line.Cells[c]);
                if (!line.Wrapped || j == src.Count - 1) break;
                j++;
            }
            i = j + 1;

            if (cells.Count == 0) {
                var empty = new Line(cols) { Marks = marks };
                if (cursorOffset >= 0) { newCursorAbs = dst.Count; newCursorCol = Math.Min(cursorOffset, cols - 1); }
                dst.Add(empty);
                continue;
            }

            int pos = 0; bool first = true;
            while (pos < cells.Count) {
                int take = Math.Min(cols, cells.Count - pos);
                if (take == cols && pos + take < cells.Count && cells[pos + take].IsWideContinuation) take--;
                take = Math.Max(1, take);
                var row = new Line(cols);
                for (int c = 0; c < take; c++) row.Cells[c] = cells[pos + c];
                if (first) { row.Marks = marks; first = false; }
                bool more = pos + take < cells.Count;
                row.Wrapped = more;
                if (cursorOffset >= 0 && cursorOffset >= pos && (cursorOffset < pos + take || !more)) {
                    newCursorAbs = dst.Count;
                    newCursorCol = Math.Min(cursorOffset - pos, cols - 1);
                    cursorOffset = -1;
                }
                dst.Add(row);
                pos += take;
            }
        }
    }
}
