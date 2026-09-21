using System.Text;

namespace Clockt.Terminal;

[Flags]
public enum LineMarks : byte { None = 0, PromptStart = 1, CommandStart = 2, CommandEnd = 4 }

/// <summary>One row of cells. <see cref="Wrapped"/> means the logical line continues on the next row.</summary>
public sealed class Line {
    public Cell[] Cells;
    public bool Wrapped;
    public LineMarks Marks;
    public ulong Version;

    public Line(int cols, CellStyle fill = default) {
        Cells = new Cell[Math.Max(0, cols)];
        if (fill != CellStyle.Default) Array.Fill(Cells, Cell.Blank(fill));
    }

    public int Length => Cells.Length;

    public void Clear(CellStyle fill) {
        Array.Fill(Cells, Cell.Blank(fill));
        Wrapped = false;
        Marks = LineMarks.None;
    }

    public void Resize(int cols, CellStyle fill) {
        if (cols == Cells.Length) return;
        var next = new Cell[cols];
        Array.Copy(Cells, next, Math.Min(cols, Cells.Length));
        for (int i = Cells.Length; i < cols; i++) next[i] = Cell.Blank(fill);
        if (cols > 0 && next[cols - 1].IsWide) next[cols - 1] = Cell.Blank(next[cols - 1].Style);   // head lost its tail
        Cells = next;
    }

    /// <summary>Index of the last cell with content, or -1.</summary>
    public int LastNonBlank() {
        for (int i = Cells.Length - 1; i >= 0; i--)
            if (!Cells[i].IsBlank || Cells[i].IsWideContinuation) return i;
        return -1;
    }

    public string ToText(GraphemePool pool, bool trimEnd = true) {
        int end = trimEnd ? LastNonBlank() + 1 : Cells.Length;
        var sb = new StringBuilder(end);
        for (int i = 0; i < end; i++) {
            var c = Cells[i];
            if (c.IsWideContinuation) continue;
            if (c.Content == 0) sb.Append(' ');
            else if (c.IsGrapheme) sb.Append(pool.Get(c.GraphemeIndex));
            else sb.Append(c.Rune.ToString());
        }
        return sb.ToString();
    }

    public Line Clone() => new(0) { Cells = (Cell[])Cells.Clone(), Wrapped = Wrapped, Marks = Marks, Version = Version };
}
