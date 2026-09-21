using System.Text;

namespace Clockt.Terminal.Input;

public enum MouseButton { None = -1, Left = 0, Middle = 1, Right = 2, WheelUp = 64, WheelDown = 65, WheelLeft = 66, WheelRight = 67 }

public enum MouseEventKind { Press, Release, Motion }

/// <summary>Encodes mouse events for the reporting mode the application asked for.</summary>
public sealed class MouseEncoder {
    bool _x10, _normal, _button, _any, _utf8, _sgr, _urxvt;
    int _lastMotionCol = -1, _lastMotionRow = -1;

    public void Sync(in TerminalModes m) {
        _x10 = m.MouseX10; _normal = m.MouseNormal; _button = m.MouseButton; _any = m.MouseAny;
        _utf8 = m.MouseUtf8; _sgr = m.MouseSgr; _urxvt = m.MouseUrxvt;
    }

    public bool IsEnabled => _x10 || _normal || _button || _any;
    public bool ReportsMotion => _button || _any;

    public byte[] Encode(MouseEventKind kind, MouseButton button, TermModifiers mods, int col, int row) {
        if (!IsEnabled) return [];
        bool wheel = (int)button >= 64;

        if (kind == MouseEventKind.Motion) {
            if (!_button && !_any) return [];
            if (_button && !_any && button == MouseButton.None) return [];
            if (col == _lastMotionCol && row == _lastMotionRow) return [];
            _lastMotionCol = col; _lastMotionRow = row;
        } else {
            _lastMotionCol = -1; _lastMotionRow = -1;
            if (kind == MouseEventKind.Release && wheel) return [];
            if (_x10 && !_normal && !_button && !_any) {
                if (kind != MouseEventKind.Press || wheel || button == MouseButton.None) return [];
                return X10(EncodeButtonCode(button, TermModifiers.None, motion: false), col, row);
            }
        }

        int code = EncodeButtonCode(button, mods, kind == MouseEventKind.Motion);
        bool release = kind == MouseEventKind.Release;

        if (_sgr) {
            return Encoding.ASCII.GetBytes($"\x1b[<{code};{col + 1};{row + 1}{(release ? 'm' : 'M')}");
        }
        if (release) code = (code & ~3) | 3;   // release = "button 3" in legacy encodings
        if (_urxvt) {
            return Encoding.ASCII.GetBytes($"\x1b[{code + 32};{col + 1};{row + 1}M");
        }
        if (_utf8) {
            var sb = new StringBuilder("\x1b[M");
            sb.Append((char)(code + 32));
            sb.Append(Utf8Coord(col + 1 + 32));
            sb.Append(Utf8Coord(row + 1 + 32));
            return Encoding.UTF8.GetBytes(sb.ToString());
        }
        return X10(code, col, row);
    }

    // Button bits: 0-2 button (3 = none/release), +4 shift, +8 alt(meta), +16 ctrl, +32 motion, 64+ wheel.
    static int EncodeButtonCode(MouseButton button, TermModifiers mods, bool motion) {
        int code = button == MouseButton.None ? 3 : (int)button;
        if ((mods & TermModifiers.Shift) != 0) code += 4;
        if ((mods & TermModifiers.Alt) != 0) code += 8;
        if ((mods & TermModifiers.Ctrl) != 0) code += 16;
        if (motion) code += 32;
        return code;
    }

    static byte[] X10(int code, int col, int row) => [
        0x1B, (byte)'[', (byte)'M',
        (byte)Math.Min(255, code + 32),
        (byte)Math.Min(255, col + 1 + 32),
        (byte)Math.Min(255, row + 1 + 32)
    ];

    static string Utf8Coord(int value) => char.ConvertFromUtf32(Math.Min(value, 2047));
}
