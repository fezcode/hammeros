using Clockt.Terminal.Parser;

namespace Clockt.Terminal;

public sealed partial class TerminalEmulator {
    public void EscDispatch(ReadOnlySpan<byte> intermediates, byte final) {
        _lastWasZwj = false;
        if (intermediates.Length == 0) {
            switch (final) {
                case (byte)'7': SaveCursor(); break;
                case (byte)'8': RestoreCursor(); break;
                case (byte)'D': _active.LineFeed(); break;
                case (byte)'E': _active.CarriageReturn(); _active.LineFeed(); break;
                case (byte)'H': _active.SetTabStop(); break;
                case (byte)'M': _active.ReverseIndex(); break;
                case (byte)'c': Reset(); break;
                case (byte)'=': Modes.KeypadApp = true; ModeChanged?.Invoke(); break;
                case (byte)'>': Modes.KeypadApp = false; ModeChanged?.Invoke(); break;
            }
            return;
        }
        switch (intermediates[0]) {
            case (byte)'(': _g0Dec = final == (byte)'0'; break;
            case (byte)')': _g1Dec = final == (byte)'0'; break;
            case (byte)'#': if (final == (byte)'8') ScreenAlignment(); break;
        }
    }

    // DECALN: fill with 'E', reset margins, home the cursor.
    void ScreenAlignment() {
        var s = _active;
        s.SetScrollRegion(0, s.Rows - 1);
        for (int r = 0; r < s.Rows; r++) {
            s.MoveTo(r, 0);
            for (int c = 0; c < s.Cols; c++) s.Put('E', 1, CellStyle.Default, 0);
        }
        s.MoveTo(0, 0);
    }
}
