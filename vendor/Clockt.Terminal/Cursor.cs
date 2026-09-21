namespace Clockt.Terminal;

public struct Cursor {
    public int Row;
    public int Col;
    /// <summary>Set after writing into the last column: the next printable wraps first (DECAWM).</summary>
    public bool PendingWrap;
    public CellStyle Style;
    public ushort HyperlinkId;
}

/// <summary>DECSC state — one per screen buffer.</summary>
public struct SavedCursor {
    public int Row, Col;
    public CellStyle Style;
    public ushort HyperlinkId;
    public bool OriginMode;
    public bool G0Dec, G1Dec, UseG1;
    public bool Valid;
}
