using System.Text;
using Clockt.Terminal.Parser;

namespace Clockt.Terminal;

public sealed partial class TerminalEmulator {
    const int MaxDcsBytes = 4096;
    readonly byte[] _dcs = new byte[MaxDcsBytes];
    int _dcsLen;
    bool _dcsOverflow;
    byte _dcsIntermediate, _dcsFinal;

    // ---- CSI reports --------------------------------------------------------------

    void DeviceAttributes(byte prefix, in CsiParams p) {
        if (p.Get(0) != 0) return;
        if (prefix == 0) Respond("\x1b[?62;22c");
        else if (prefix == (byte)'>') Respond("\x1b[>1;10;0c");
    }

    void DeviceStatusReport(in CsiParams p, bool isPrivate) {
        switch (p.Get(0)) {
            case 5 when !isPrivate: Respond("\x1b[0n"); break;
            case 6: {
                var c = _active.Cursor;
                int row = c.Row - (_active.OriginMode ? _active.ScrollTop : 0) + 1;
                int col = c.Col + 1;
                Respond(isPrivate ? $"\x1b[?{row};{col};1R" : $"\x1b[{row};{col}R");
                break;
            }
        }
    }

    void RequestMode(in CsiParams p, bool isPrivate) {
        int mode = p.Get(0);
        int value;
        if (isPrivate) {
            value = mode switch {
                1 => On(Modes.CursorKeysApp), 5 => On(Modes.ReverseVideo), 6 => On(Modes.Origin), 7 => On(Modes.AutoWrap),
                9 => On(Modes.MouseX10), 12 => On(Modes.CursorBlink), 25 => On(Modes.CursorVisible),
                47 or 1047 or 1049 => On(Modes.AltScreen), 1000 => On(Modes.MouseNormal), 1002 => On(Modes.MouseButton),
                1003 => On(Modes.MouseAny), 1004 => On(Modes.FocusEvents), 1005 => On(Modes.MouseUtf8), 1006 => On(Modes.MouseSgr),
                1015 => On(Modes.MouseUrxvt), 1036 => On(Modes.AltSendsEscape), 2004 => On(Modes.BracketedPaste), 2026 => On(Modes.SyncOutput),
                _ => 0
            };
            Respond($"\x1b[?{mode};{value}$y");
        } else {
            value = mode switch { 4 => On(Modes.Insert), 20 => On(Modes.NewLine), _ => 0 };
            Respond($"\x1b[{mode};{value}$y");
        }
        static int On(bool b) => b ? 1 : 2;
    }

    void WindowOps(in CsiParams p) {
        switch (p.Get(0)) {
            case 14: Respond($"\x1b[4;{_active.Rows * CellHeightPx};{_active.Cols * CellWidthPx}t"); break;
            case 16: Respond($"\x1b[6;{CellHeightPx};{CellWidthPx}t"); break;
            case 18: Respond($"\x1b[8;{_active.Rows};{_active.Cols}t"); break;
            default: break;   // move/resize/iconify requests are never honoured
        }
    }

    // ---- DCS ----------------------------------------------------------------------

    public void DcsHook(byte prefix, in CsiParams p, ReadOnlySpan<byte> intermediates, byte final) {
        _lastWasZwj = false;
        _dcsLen = 0; _dcsOverflow = false;
        _dcsIntermediate = intermediates.Length > 0 ? intermediates[0] : (byte)0;
        _dcsFinal = final;
    }

    public void DcsPut(byte b) {
        if (_dcsLen < MaxDcsBytes) _dcs[_dcsLen++] = b;
        else _dcsOverflow = true;
    }

    public void DcsUnhook() {
        var payload = _dcsOverflow ? ReadOnlySpan<byte>.Empty : _dcs.AsSpan(0, _dcsLen);
        switch ((_dcsIntermediate, _dcsFinal)) {
            case ((byte)'$', (byte)'q'): RequestStatusString(Encoding.ASCII.GetString(payload)); break;
            case ((byte)'+', (byte)'q'): GetTermcap(Encoding.ASCII.GetString(payload)); break;
        }
        _dcsLen = 0;
    }

    void RequestStatusString(string what) {
        switch (what) {
            case "r": Respond($"\x1bP1$r{_active.ScrollTop + 1};{_active.ScrollBottom + 1}r\x1b\\"); break;
            case " q": Respond($"\x1bP1$r{(int)CursorStyle} q\x1b\\"); break;
            case "m": Respond($"\x1bP1$r{SgrString(_active.Cursor.Style)}m\x1b\\"); break;
            default: Respond("\x1bP0$r\x1b\\"); break;
        }
    }

    void GetTermcap(string hexNames) {
        var replies = new List<string>();
        foreach (var hex in hexNames.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string name;
            try { name = Encoding.ASCII.GetString(Convert.FromHexString(hex)); }
            catch (FormatException) { continue; }
            string? value = name switch {
                "TN" => "xterm-256color",
                "Co" or "colors" => "256",
                "RGB" => "8",
                "Tc" => "",
                _ => null
            };
            if (value == null) continue;
            replies.Add($"{hex}={Convert.ToHexString(Encoding.ASCII.GetBytes(value))}");
        }
        Respond(replies.Count == 0 ? "\x1bP0+r\x1b\\" : $"\x1bP1+r{string.Join(';', replies)}\x1b\\");
    }

    /// <summary>SGR parameter string that reproduces <paramref name="s"/> from a reset state (always starts with 0).</summary>
    internal static string SgrString(CellStyle s) {
        var sb = new StringBuilder("0");
        if (s.Has(StyleFlags.Bold)) sb.Append(";1");
        if (s.Has(StyleFlags.Dim)) sb.Append(";2");
        if (s.Has(StyleFlags.Italic)) sb.Append(";3");
        if (s.UnderlineStyle != UnderlineStyle.None) sb.Append(";4:").Append((int)s.UnderlineStyle);
        if (s.Has(StyleFlags.Blink)) sb.Append(";5");
        if (s.Has(StyleFlags.Inverse)) sb.Append(";7");
        if (s.Has(StyleFlags.Hidden)) sb.Append(";8");
        if (s.Has(StyleFlags.Strike)) sb.Append(";9");
        AppendColor(sb, s.Fg, 30, 38);
        AppendColor(sb, s.Bg, 40, 48);
        if (!s.Underline.IsDefault) AppendColor(sb, s.Underline, 0, 58);
        return sb.ToString();

        static void AppendColor(StringBuilder sb, TermColor c, int basic, int extended) {
            switch (c.Kind) {
                case TermColorKind.Default: break;
                case TermColorKind.Indexed when basic != 0 && c.Index < 8: sb.Append(';').Append(basic + c.Index); break;
                case TermColorKind.Indexed when basic != 0 && c.Index < 16: sb.Append(';').Append(basic + 60 + c.Index - 8); break;
                case TermColorKind.Indexed: sb.Append(';').Append(extended).Append(":5:").Append(c.Index); break;
                case TermColorKind.Rgb: sb.Append(';').Append(extended).Append(":2::").Append(c.R).Append(':').Append(c.G).Append(':').Append(c.B); break;
            }
        }
    }
}
