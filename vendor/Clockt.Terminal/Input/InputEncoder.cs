using System.Text;

namespace Clockt.Terminal.Input;

/// <summary>Turns key events into the bytes an xterm-compatible application expects.</summary>
public sealed class InputEncoder {
    public bool CursorKeysApp { get; set; }
    public bool KeypadApp { get; set; }
    public bool BackspaceSendsDel { get; set; } = true;
    public bool AltSendsEscape { get; set; } = true;
    public bool NewLineMode { get; set; }

    public byte[] Encode(in KeyInput key) {
        Span<byte> buf = stackalloc byte[16];
        int n = Encode(in key, buf);
        return buf[..n].ToArray();
    }

    /// <summary>Writes the encoding into <paramref name="dst"/> (≥ 16 bytes) and returns its length; 0 means "send nothing".</summary>
    public int Encode(in KeyInput key, Span<byte> dst) {
        var mods = key.Mods;
        bool alt = (mods & TermModifiers.Alt) != 0;
        bool ctrl = (mods & TermModifiers.Ctrl) != 0;
        bool shift = (mods & TermModifiers.Shift) != 0;
        int n = 0;

        switch (key.Key) {
            case TermKey.None: return 0;

            case TermKey.Char: {
                if (key.Char.Value == 0) return 0;
                if (alt && AltSendsEscape) dst[n++] = 0x1B;
                if (ctrl && TryControl(key.Char, out var c)) { dst[n++] = c; return n; }
                n += key.Char.EncodeToUtf8(dst[n..]);
                return n;
            }

            case TermKey.Enter:
                if (alt) dst[n++] = 0x1B;
                dst[n++] = (byte)'\r';
                if (NewLineMode) dst[n++] = (byte)'\n';
                return n;

            case TermKey.Tab:
                if (shift) return Write(dst, "\x1b[Z");
                if (alt) dst[n++] = 0x1B;
                dst[n++] = (byte)'\t';
                return n;

            case TermKey.Backspace: {
                if (alt) dst[n++] = 0x1B;
                bool del = BackspaceSendsDel ^ ctrl;
                dst[n++] = del ? (byte)0x7F : (byte)0x08;
                return n;
            }

            case TermKey.Escape:
                if (alt) dst[n++] = 0x1B;
                dst[n++] = 0x1B;
                return n;

            case TermKey.Up:    return Cursor(dst, 'A', mods);
            case TermKey.Down:  return Cursor(dst, 'B', mods);
            case TermKey.Right: return Cursor(dst, 'C', mods);
            case TermKey.Left:  return Cursor(dst, 'D', mods);
            case TermKey.Home:  return Cursor(dst, 'H', mods);
            case TermKey.End:   return Cursor(dst, 'F', mods);

            case TermKey.Insert:   return Tilde(dst, 2, mods);
            case TermKey.Delete:   return Tilde(dst, 3, mods);
            case TermKey.PageUp:   return Tilde(dst, 5, mods);
            case TermKey.PageDown: return Tilde(dst, 6, mods);

            case TermKey.F1: return Ss3(dst, 'P', mods);
            case TermKey.F2: return Ss3(dst, 'Q', mods);
            case TermKey.F3: return Ss3(dst, 'R', mods);
            case TermKey.F4: return Ss3(dst, 'S', mods);
            case TermKey.F5:  return Tilde(dst, 15, mods);
            case TermKey.F6:  return Tilde(dst, 17, mods);
            case TermKey.F7:  return Tilde(dst, 18, mods);
            case TermKey.F8:  return Tilde(dst, 19, mods);
            case TermKey.F9:  return Tilde(dst, 20, mods);
            case TermKey.F10: return Tilde(dst, 21, mods);
            case TermKey.F11: return Tilde(dst, 23, mods);
            case TermKey.F12: return Tilde(dst, 24, mods);
        }
        return 0;
    }

    static int ModParam(TermModifiers m) =>
        1 + ((m & TermModifiers.Shift) != 0 ? 1 : 0) + ((m & TermModifiers.Alt) != 0 ? 2 : 0)
          + ((m & TermModifiers.Ctrl) != 0 ? 4 : 0) + ((m & TermModifiers.Meta) != 0 ? 8 : 0);

    int Cursor(Span<byte> dst, char final, TermModifiers mods) {
        if (mods == TermModifiers.None) return Write(dst, CursorKeysApp ? $"\x1bO{final}" : $"\x1b[{final}");
        return Write(dst, $"\x1b[1;{ModParam(mods)}{final}");
    }

    static int Ss3(Span<byte> dst, char final, TermModifiers mods) =>
        Write(dst, mods == TermModifiers.None ? $"\x1bO{final}" : $"\x1b[1;{ModParam(mods)}{final}");

    static int Tilde(Span<byte> dst, int code, TermModifiers mods) =>
        Write(dst, mods == TermModifiers.None ? $"\x1b[{code}~" : $"\x1b[{code};{ModParam(mods)}~");

    static int Write(Span<byte> dst, string ascii) {
        for (int i = 0; i < ascii.Length; i++) dst[i] = (byte)ascii[i];
        return ascii.Length;
    }

    static bool TryControl(Rune r, out byte control) {
        int v = r.Value;
        if (v >= 'a' && v <= 'z') { control = (byte)(v - 'a' + 1); return true; }
        if (v >= 'A' && v <= 'Z') { control = (byte)(v - 'A' + 1); return true; }
        switch (v) {
            case '@': case ' ': case '2': control = 0x00; return true;
            case '[': case '3': control = 0x1B; return true;
            case '\\': case '4': control = 0x1C; return true;
            case ']': case '5': control = 0x1D; return true;
            case '^': case '6': control = 0x1E; return true;
            case '_': case '7': control = 0x1F; return true;
            case '?': case '8': control = 0x7F; return true;
        }
        control = 0;
        return false;
    }

    /// <summary>Paste text as the application should see it: newlines become CR, other C0 controls (except TAB) are dropped, and an embedded bracketed-paste terminator is removed.</summary>
    public static byte[] EncodePaste(string text, bool bracketed) {
        var sb = new StringBuilder(text.Length + 16);
        if (bracketed) sb.Append("\x1b[200~");
        text = text.Replace("\x1b[201~", "");
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (c == '\r') { sb.Append('\r'); if (i + 1 < text.Length && text[i + 1] == '\n') i++; continue; }
            if (c == '\n') { sb.Append('\r'); continue; }
            if (c < 0x20 && c != '\t') continue;
            if (c == 0x7F) continue;
            sb.Append(c);
        }
        if (bracketed) sb.Append("\x1b[201~");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] FocusEvent(bool focused) => focused ? "\x1b[I"u8.ToArray() : "\x1b[O"u8.ToArray();
}
