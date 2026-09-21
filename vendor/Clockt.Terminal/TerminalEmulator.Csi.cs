using Clockt.Terminal.Parser;

namespace Clockt.Terminal;

public sealed partial class TerminalEmulator {
    public void CsiDispatch(byte prefix, in CsiParams p, ReadOnlySpan<byte> intermediates, byte final) {
        _lastWasZwj = false;
        if (intermediates.Length > 0) { CsiWithIntermediate(prefix, in p, intermediates[0], final); return; }
        if (prefix == (byte)'?') { CsiPrivate(in p, final); return; }
        if (prefix != 0) { CsiOther(prefix, in p, final); return; }

        var s = _active;
        switch (final) {
            case (byte)'@': s.InsertChars(p.GetOr(0, 1)); break;
            case (byte)'A': s.MoveUp(p.GetOr(0, 1)); break;
            case (byte)'B': case (byte)'e': s.MoveDown(p.GetOr(0, 1)); break;
            case (byte)'C': case (byte)'a': s.MoveRight(p.GetOr(0, 1)); break;
            case (byte)'D': s.MoveLeft(p.GetOr(0, 1)); break;
            case (byte)'E': s.MoveDown(p.GetOr(0, 1)); s.CarriageReturn(); break;
            case (byte)'F': s.MoveUp(p.GetOr(0, 1)); s.CarriageReturn(); break;
            case (byte)'G': case (byte)'`': s.SetColumn(p.GetOr(0, 1) - 1); break;
            case (byte)'H': case (byte)'f': s.MoveTo(p.GetOr(0, 1) - 1, p.GetOr(1, 1) - 1); break;
            case (byte)'I': for (int i = p.GetOr(0, 1); i > 0; i--) s.Tab(); break;
            case (byte)'J': EraseInDisplay(p.Get(0)); break;
            case (byte)'K': s.EraseInLine(p.Get(0)); break;
            case (byte)'L': s.InsertLines(p.GetOr(0, 1)); break;
            case (byte)'M': s.DeleteLines(p.GetOr(0, 1)); break;
            case (byte)'P': s.DeleteChars(p.GetOr(0, 1)); break;
            case (byte)'S': s.ScrollUp(p.GetOr(0, 1)); break;
            case (byte)'T': s.ScrollDown(p.GetOr(0, 1)); break;
            case (byte)'X': s.EraseChars(p.GetOr(0, 1)); break;
            case (byte)'Z': for (int i = p.GetOr(0, 1); i > 0; i--) s.BackTab(); break;
            case (byte)'d': s.SetRow(p.GetOr(0, 1) - 1); break;
            case (byte)'g':
                if (p.Get(0) == 3) s.ClearAllTabStops();
                else if (p.Get(0) == 0) s.ClearTabStop();
                break;
            case (byte)'h': SetAnsiModes(in p, true); break;
            case (byte)'l': SetAnsiModes(in p, false); break;
            case (byte)'r': s.SetScrollRegion(p.GetOr(0, 1) - 1, p.GetOr(1, s.Rows) - 1); break;
            case (byte)'s': SaveCursor(); break;
            case (byte)'u': RestoreCursor(); break;
            default: CsiOther(prefix, in p, final); break;
        }
    }

    void EraseInDisplay(int mode) {
        if (mode == 3) { _scrollback.Clear(); return; }
        _active.EraseInDisplay(mode);
    }

    void SetAnsiModes(in CsiParams p, bool on) {
        for (int i = 0; i < p.Count; i++) {
            switch (p.Get(i)) {
                case 4: Modes.Insert = on; _primary.InsertMode = on; _alternate.InsertMode = on; break;
                case 20: Modes.NewLine = on; break;
            }
        }
        ModeChanged?.Invoke();
    }

    void CsiPrivate(in CsiParams p, byte final) {
        switch (final) {
            case (byte)'h': SetDecModes(in p, true); break;
            case (byte)'l': SetDecModes(in p, false); break;
            default: CsiPrivateOther(in p, final); break;
        }
    }

    void SetDecModes(in CsiParams p, bool on) {
        for (int i = 0; i < p.Count; i++) {
            switch (p.Get(i)) {
                case 1: Modes.CursorKeysApp = on; break;
                case 5: Modes.ReverseVideo = on; break;
                case 6: Modes.Origin = on; _primary.OriginMode = on; _alternate.OriginMode = on; _active.MoveTo(0, 0); break;
                case 7: Modes.AutoWrap = on; _primary.AutoWrap = on; _alternate.AutoWrap = on; break;
                case 9: Modes.MouseX10 = on; break;
                case 12: Modes.CursorBlink = on; break;
                case 25: Modes.CursorVisible = on; break;
                case 47: SwitchScreen(on, saveCursor: false, clear: false); break;
                case 1047: SwitchScreen(on, saveCursor: false, clear: true); break;
                case 1048: if (on) SaveCursor(); else RestoreCursor(); break;
                case 1049: SwitchScreen(on, saveCursor: true, clear: true); break;
                case 1000: Modes.MouseNormal = on; break;
                case 1002: Modes.MouseButton = on; break;
                case 1003: Modes.MouseAny = on; break;
                case 1004: Modes.FocusEvents = on; break;
                case 1005: Modes.MouseUtf8 = on; break;
                case 1006: Modes.MouseSgr = on; break;
                case 1015: Modes.MouseUrxvt = on; break;
                case 1036: Modes.AltSendsEscape = on; break;
                case 2004: Modes.BracketedPaste = on; break;
                case 2026: Modes.SyncOutput = on; SyncOutputChanged(on); break;
            }
        }
        ModeChanged?.Invoke();
    }

    void CsiWithIntermediate(byte prefix, in CsiParams p, byte intermediate, byte final) {
        switch ((intermediate, final)) {
            case ((byte)' ', (byte)'q'): SetCursorStyle((CursorStyle)Math.Clamp(p.Get(0), 0, 6)); break;
            case ((byte)'!', (byte)'p'): SoftReset(); break;
            case ((byte)'$', (byte)'p'): RequestMode(in p, isPrivate: prefix == (byte)'?'); break;
            default: break;
        }
    }

    // SGR ('m'), reports ('c', 'n', 't') and private queries. Anything unhandled is ignored on purpose.
    void CsiOther(byte prefix, in CsiParams p, byte final) {
        switch (final) {
            case (byte)'m': if (prefix == 0) ApplySgr(in p); break;
            case (byte)'c': DeviceAttributes(prefix, in p); break;
            case (byte)'n': if (prefix == 0) DeviceStatusReport(in p, isPrivate: false); break;
            case (byte)'t': if (prefix == 0) WindowOps(in p); break;
            default: break;
        }
    }

    void CsiPrivateOther(in CsiParams p, byte final) {
        switch (final) {
            case (byte)'n': DeviceStatusReport(in p, isPrivate: true); break;
            default: break;
        }
    }
}
