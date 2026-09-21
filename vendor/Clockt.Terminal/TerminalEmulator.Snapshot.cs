namespace Clockt.Terminal;

public sealed partial class TerminalEmulator {
    internal void WriteState(BinaryWriter w, int scrollbackTail) {
        lock (SyncRoot) {
            w.Write(_active.Cols); w.Write(_active.Rows);
            w.Write(_active.IsAlternate);
            WriteModes(w, Modes);
            w.Write((byte)CursorStyle);
            w.Write(Title);
            w.Write(Cwd != null); if (Cwd != null) w.Write(Cwd);
            w.Write((byte)Progress.Kind); w.Write(Progress.Percent);
            w.Write(_g0Dec); w.Write(_g1Dec); w.Write(_useG1);
            var links = Hyperlinks.Snapshot();
            w.Write(links.Count);
            foreach (var (uri, id) in links) { w.Write(uri); w.Write(id); }
            WriteScreen(w, _primary, _savedPrimary);
            WriteScreen(w, _alternate, _savedAlternate);
            int tail = Math.Clamp(scrollbackTail, 0, _scrollback.Count);
            w.Write(tail);
            for (int i = _scrollback.Count - tail; i < _scrollback.Count; i++) WriteLine(w, _scrollback[i]);
        }
    }

    internal void ReadState(BinaryReader r) {
        lock (SyncRoot) {
            int cols = r.ReadInt32(), rows = r.ReadInt32();
            bool alt = r.ReadBoolean();
            Modes = ReadModes(r);
            CursorStyle = (CursorStyle)r.ReadByte();
            Title = r.ReadString();
            Cwd = r.ReadBoolean() ? r.ReadString() : null;
            Progress = new ProgressState((ProgressKind)r.ReadByte(), r.ReadInt32());
            _g0Dec = r.ReadBoolean(); _g1Dec = r.ReadBoolean(); _useG1 = r.ReadBoolean();
            int linkCount = r.ReadInt32();
            var links = new List<(string, string)>(linkCount);
            for (int i = 0; i < linkCount; i++) links.Add((r.ReadString(), r.ReadString()));
            Hyperlinks.Restore(links);
            _savedPrimary = ReadScreen(r, _primary, cols, rows);
            _savedAlternate = ReadScreen(r, _alternate, cols, rows);
            int tail = r.ReadInt32();
            var lines = new List<Line>(tail);
            for (int i = 0; i < tail; i++) lines.Add(ReadLine(r, cols));
            _scrollback.Load(lines);
            _active = alt ? _alternate : _primary;
            _lastWasZwj = false;
            Generation++;
        }
    }

    static void WriteModes(BinaryWriter w, in TerminalModes m) {
        w.Write(m.CursorKeysApp); w.Write(m.ReverseVideo); w.Write(m.Origin); w.Write(m.AutoWrap);
        w.Write(m.MouseX10); w.Write(m.CursorBlink); w.Write(m.CursorVisible); w.Write(m.AltScreen);
        w.Write(m.MouseNormal); w.Write(m.MouseButton); w.Write(m.MouseAny); w.Write(m.FocusEvents);
        w.Write(m.MouseUtf8); w.Write(m.MouseSgr); w.Write(m.MouseUrxvt); w.Write(m.AltSendsEscape);
        w.Write(m.BracketedPaste); w.Write(m.SyncOutput); w.Write(m.Insert); w.Write(m.NewLine); w.Write(m.KeypadApp);
    }

    static TerminalModes ReadModes(BinaryReader r) => new() {
        CursorKeysApp = r.ReadBoolean(), ReverseVideo = r.ReadBoolean(), Origin = r.ReadBoolean(), AutoWrap = r.ReadBoolean(),
        MouseX10 = r.ReadBoolean(), CursorBlink = r.ReadBoolean(), CursorVisible = r.ReadBoolean(), AltScreen = r.ReadBoolean(),
        MouseNormal = r.ReadBoolean(), MouseButton = r.ReadBoolean(), MouseAny = r.ReadBoolean(), FocusEvents = r.ReadBoolean(),
        MouseUtf8 = r.ReadBoolean(), MouseSgr = r.ReadBoolean(), MouseUrxvt = r.ReadBoolean(), AltSendsEscape = r.ReadBoolean(),
        BracketedPaste = r.ReadBoolean(), SyncOutput = r.ReadBoolean(), Insert = r.ReadBoolean(), NewLine = r.ReadBoolean(), KeypadApp = r.ReadBoolean()
    };

    static void WriteScreen(BinaryWriter w, Screen s, in SavedCursor saved) {
        var graphemes = s.Graphemes.Snapshot();
        w.Write(graphemes.Count);
        foreach (var g in graphemes) w.Write(g);
        w.Write(s.Cursor.Row); w.Write(s.Cursor.Col); w.Write(s.Cursor.PendingWrap);
        WriteStyle(w, s.Cursor.Style); w.Write(s.Cursor.HyperlinkId);
        w.Write(s.ScrollTop); w.Write(s.ScrollBottom);
        w.Write(s.AutoWrap); w.Write(s.InsertMode); w.Write(s.OriginMode);
        foreach (var stop in s.TabStops) w.Write(stop);
        w.Write(saved.Valid); w.Write(saved.Row); w.Write(saved.Col); WriteStyle(w, saved.Style); w.Write(saved.HyperlinkId);
        w.Write(saved.OriginMode); w.Write(saved.G0Dec); w.Write(saved.G1Dec); w.Write(saved.UseG1);
        for (int r = 0; r < s.Rows; r++) WriteLine(w, s[r]);
    }

    static SavedCursor ReadScreen(BinaryReader r, Screen s, int cols, int rows) {
        int gCount = r.ReadInt32();
        var graphemes = new List<string>(gCount);
        for (int i = 0; i < gCount; i++) graphemes.Add(r.ReadString());
        s.Graphemes.Restore(graphemes);
        var cursor = new Cursor { Row = r.ReadInt32(), Col = r.ReadInt32(), PendingWrap = r.ReadBoolean() };
        cursor.Style = ReadStyle(r); cursor.HyperlinkId = r.ReadUInt16();
        int top = r.ReadInt32(), bottom = r.ReadInt32();
        bool autoWrap = r.ReadBoolean(), insert = r.ReadBoolean(), origin = r.ReadBoolean();
        var stops = new bool[cols];
        for (int i = 0; i < cols; i++) stops[i] = r.ReadBoolean();
        var saved = new SavedCursor { Valid = r.ReadBoolean(), Row = r.ReadInt32(), Col = r.ReadInt32() };
        saved.Style = ReadStyle(r); saved.HyperlinkId = r.ReadUInt16();
        saved.OriginMode = r.ReadBoolean(); saved.G0Dec = r.ReadBoolean(); saved.G1Dec = r.ReadBoolean(); saved.UseG1 = r.ReadBoolean();
        var lines = new Line[rows];
        for (int i = 0; i < rows; i++) lines[i] = ReadLine(r, cols);
        s.Load(lines);
        s.SetTabStops(stops);
        s.SetScrollRegionRaw(top, bottom);
        s.AutoWrap = autoWrap; s.InsertMode = insert; s.OriginMode = origin;
        s.Cursor = cursor;
        s.Cursor.Row = Math.Clamp(s.Cursor.Row, 0, rows - 1);
        s.Cursor.Col = Math.Clamp(s.Cursor.Col, 0, cols - 1);
        return saved;
    }

    static void WriteLine(BinaryWriter w, Line line) {
        w.Write(line.Wrapped);
        w.Write((byte)line.Marks);
        var cells = line.Cells;
        int runs = 0;
        for (int i = 0; i < cells.Length;) { int j = i + 1; while (j < cells.Length && cells[j] == cells[i] && j - i < ushort.MaxValue) j++; runs++; i = j; }
        w.Write(runs);
        for (int i = 0; i < cells.Length;) {
            int j = i + 1;
            while (j < cells.Length && cells[j] == cells[i] && j - i < ushort.MaxValue) j++;
            w.Write((ushort)(j - i));
            WriteCell(w, cells[i]);
            i = j;
        }
    }

    static Line ReadLine(BinaryReader r, int cols) {
        var line = new Line(cols) { Wrapped = r.ReadBoolean(), Marks = (LineMarks)r.ReadByte() };
        int runs = r.ReadInt32();
        if (runs < 0 || runs > cols) throw new InvalidDataException("corrupt run count");
        int pos = 0;
        for (int k = 0; k < runs; k++) {
            int len = r.ReadUInt16();
            var cell = ReadCell(r);
            for (int i = 0; i < len && pos < cols; i++) line.Cells[pos++] = cell;
        }
        return line;
    }

    static void WriteCell(BinaryWriter w, in Cell c) {
        w.Write(c.Content); WriteStyle(w, c.Style); w.Write(c.HyperlinkId); w.Write((byte)c.Flags);
    }

    static Cell ReadCell(BinaryReader r) =>
        new(r.ReadUInt32(), ReadStyle(r), r.ReadUInt16(), (CellFlags)r.ReadByte());

    static void WriteStyle(BinaryWriter w, in CellStyle s) {
        WriteColor(w, s.Fg); WriteColor(w, s.Bg); WriteColor(w, s.Underline);
        w.Write((byte)s.Flags); w.Write((byte)s.UnderlineStyle);
    }

    static CellStyle ReadStyle(BinaryReader r) {
        var fg = ReadColor(r); var bg = ReadColor(r); var ul = ReadColor(r);
        return new CellStyle(fg, bg, ul, (StyleFlags)r.ReadByte(), (UnderlineStyle)r.ReadByte());
    }

    static void WriteColor(BinaryWriter w, in TermColor c) {
        w.Write((byte)c.Kind); w.Write(c.Index); w.Write(c.R); w.Write(c.G); w.Write(c.B);
    }

    static TermColor ReadColor(BinaryReader r) =>
        new((TermColorKind)r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
}
