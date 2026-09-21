using Clockt.Terminal.Parser;

namespace Clockt.Terminal;

public sealed partial class TerminalEmulator {
    void ApplySgr(in CsiParams p) {
        var st = _active.Cursor.Style;
        if (p.Count == 0) st = CellStyle.Default;

        for (int i = 0; i < p.Count; i++) {
            int code = p.Get(i);
            switch (code) {
                case 0: st = CellStyle.Default; break;
                case 1: st = st with { Flags = st.Flags | StyleFlags.Bold }; break;
                case 2: st = st with { Flags = st.Flags | StyleFlags.Dim }; break;
                case 3: st = st with { Flags = st.Flags | StyleFlags.Italic }; break;
                case 4:
                    st = st with {
                        UnderlineStyle = p.SubCount(i) > 0
                            ? (UnderlineStyle)Math.Clamp(p.Sub(i, 0), 0, 5)
                            : UnderlineStyle.Single
                    };
                    break;
                case 5: case 6: st = st with { Flags = st.Flags | StyleFlags.Blink }; break;
                case 7: st = st with { Flags = st.Flags | StyleFlags.Inverse }; break;
                case 8: st = st with { Flags = st.Flags | StyleFlags.Hidden }; break;
                case 9: st = st with { Flags = st.Flags | StyleFlags.Strike }; break;
                case 21: st = st with { UnderlineStyle = UnderlineStyle.Double }; break;
                case 22: st = st with { Flags = st.Flags & ~(StyleFlags.Bold | StyleFlags.Dim) }; break;
                case 23: st = st with { Flags = st.Flags & ~StyleFlags.Italic }; break;
                case 24: st = st with { UnderlineStyle = UnderlineStyle.None }; break;
                case 25: st = st with { Flags = st.Flags & ~StyleFlags.Blink }; break;
                case 27: st = st with { Flags = st.Flags & ~StyleFlags.Inverse }; break;
                case 28: st = st with { Flags = st.Flags & ~StyleFlags.Hidden }; break;
                case 29: st = st with { Flags = st.Flags & ~StyleFlags.Strike }; break;
                case >= 30 and <= 37: st = st with { Fg = TermColor.Indexed((byte)(code - 30)) }; break;
                case 38: if (TryParseExtendedColor(in p, ref i, out var fg)) st = st with { Fg = fg }; break;
                case 39: st = st with { Fg = TermColor.Default }; break;
                case >= 40 and <= 47: st = st with { Bg = TermColor.Indexed((byte)(code - 40)) }; break;
                case 48: if (TryParseExtendedColor(in p, ref i, out var bg)) st = st with { Bg = bg }; break;
                case 49: st = st with { Bg = TermColor.Default }; break;
                case 58: if (TryParseExtendedColor(in p, ref i, out var ul)) st = st with { Underline = ul }; break;
                case 59: st = st with { Underline = TermColor.Default }; break;
                case >= 90 and <= 97: st = st with { Fg = TermColor.Indexed((byte)(code - 90 + 8)) }; break;
                case >= 100 and <= 107: st = st with { Bg = TermColor.Indexed((byte)(code - 100 + 8)) }; break;
                default: break;   // unknown SGR: ignored
            }
        }
        _active.Cursor.Style = st;
    }

    /// <summary>
    /// Parses the colour after 38/48/58 in either form: <c>;5;n</c> / <c>;2;r;g;b</c>
    /// (consumes following parameters, advancing <paramref name="i"/>) or
    /// <c>:5:n</c> / <c>:2:r:g:b</c> / <c>:2::r:g:b</c> / <c>:2:cs:r:g:b</c> (subparameters).
    /// Returns false, consuming nothing, when the shape is not recognised.
    /// </summary>
    internal static bool TryParseExtendedColor(in CsiParams p, ref int i, out TermColor color) {
        color = TermColor.Default;
        int subs = p.SubCount(i);
        if (subs > 0) {
            int kind = p.Sub(i, 0);
            if (kind == 5 && subs >= 2) { color = TermColor.Indexed(Clamp(p.Sub(i, 1))); return true; }
            if (kind == 2) {
                if (subs >= 5) { color = TermColor.Rgb(Clamp(p.Sub(i, 2)), Clamp(p.Sub(i, 3)), Clamp(p.Sub(i, 4))); return true; }
                if (subs >= 4) { color = TermColor.Rgb(Clamp(p.Sub(i, 1)), Clamp(p.Sub(i, 2)), Clamp(p.Sub(i, 3))); return true; }
            }
            return false;
        }
        int next = p.Get(i + 1);
        if (next == 5 && p.Has(i + 2)) { color = TermColor.Indexed(Clamp(p.Get(i + 2))); i += 2; return true; }
        if (next == 2 && p.Has(i + 4)) {
            color = TermColor.Rgb(Clamp(p.Get(i + 2)), Clamp(p.Get(i + 3)), Clamp(p.Get(i + 4)));
            i += 4;
            return true;
        }
        return false;
    }

    static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
}
