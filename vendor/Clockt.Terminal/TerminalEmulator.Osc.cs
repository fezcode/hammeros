using System.Globalization;
using System.Text;

namespace Clockt.Terminal;

public sealed partial class TerminalEmulator {
    public void OscDispatch(ReadOnlySpan<byte> payload) {
        _lastWasZwj = false;
        int semi = payload.IndexOf((byte)';');
        var codeBytes = semi < 0 ? payload : payload[..semi];
        if (codeBytes.Length == 0 || codeBytes.Length > 5) return;
        int code = 0;
        foreach (var b in codeBytes) {
            if (b < '0' || b > '9') return;
            code = code * 10 + (b - '0');
        }
        var rest = semi < 0 ? ReadOnlySpan<byte>.Empty : payload[(semi + 1)..];
        string text = Encoding.UTF8.GetString(rest);

        switch (code) {
            case 0: case 2: SetTitle(text); break;
            case 4: Palette(text); break;
            case 7: SetCwd(ParseFileUrlPath(text)); break;
            case 8: Hyperlink(text); break;
            case 9: ConEmu(text); break;
            case 10: case 11: case 12: ColorQuery(code, text); break;
            case 52: Clipboard(text); break;
            case 133: ShellIntegration(text); break;
            case 99: KittyNotification(text); break;
            case 777: if (text.StartsWith("notify;", StringComparison.Ordinal)) Notification(text[7..]); break;
        }
    }

    void SetTitle(string title) {
        if (title == Title) return;
        Title = title;
        TitleChanged?.Invoke(title);
    }

    void SetCwd(string? cwd) {
        if (cwd == Cwd) return;
        Cwd = cwd;
        CwdChanged?.Invoke(cwd);
    }

    /// <summary>file://host/path → path; percent-decoded; a leading "/C:/" becomes "C:/". Non-URL input is returned as-is; empty → null.</summary>
    internal static string? ParseFileUrlPath(string url) {
        if (string.IsNullOrEmpty(url)) return null;
        if (!url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return url;
        var rest = url[7..];
        int slash = rest.IndexOf('/');
        if (slash < 0) return null;
        var path = Uri.UnescapeDataString(rest[slash..]);
        if (path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':') path = path[1..];
        return path.Length == 0 ? null : path;
    }

    void Hyperlink(string text) {
        int semi = text.IndexOf(';');
        if (semi < 0) return;
        var prms = text[..semi];
        var uri = text[(semi + 1)..];
        if (uri.Length == 0) { _active.Cursor.HyperlinkId = 0; return; }
        string? id = null;
        foreach (var kv in prms.Split(':', StringSplitOptions.RemoveEmptyEntries)) {
            if (kv.StartsWith("id=", StringComparison.Ordinal)) id = kv[3..];
        }
        _active.Cursor.HyperlinkId = Hyperlinks.Add(uri, id);
    }

    void ShellIntegration(string text) {
        if (text.Length == 0) return;
        var line = _active.CurrentLine;
        switch (text[0]) {
            case 'A': line.Marks |= LineMarks.PromptStart; PromptMark?.Invoke(PromptMarkKind.PromptStart); break;
            case 'B': ShellCommandText = null; line.Marks |= LineMarks.CommandStart; PromptMark?.Invoke(PromptMarkKind.CommandStart); break;
            case 'C': PromptMark?.Invoke(PromptMarkKind.CommandExecuted); break;
            case 'D':
                LastCommandExitCode = text.Length > 2 && int.TryParse(text.AsSpan(2), out var code) ? code : null;
                line.Marks |= LineMarks.CommandEnd; PromptMark?.Invoke(PromptMarkKind.CommandFinished); break;
            // clockt extension: command text as UTF-8/base64, never interpreted as terminal output.
            case 'E':
                if (text.Length > 2 && text.Length < 32768) {
                    try { ShellCommandText = Encoding.UTF8.GetString(Convert.FromBase64String(text[2..])); }
                    catch (FormatException) { ShellCommandText = null; }
                }
                break;
        }
    }

    // OSC 9: ConEmu family. "4;state;percent" is a progress report; anything else is a notification body.
    void ConEmu(string text) {
        var parts = text.Split(';');
        if (parts[0] == "4") {
            if (parts.Length < 2 || !int.TryParse(parts[1], out var state)) return;
            int percent = parts.Length >= 3 && int.TryParse(parts[2], out var pc) ? Math.Clamp(pc, 0, 100) : 0;
            var kind = (ProgressKind)Math.Clamp(state, 0, 4);
            Progress = new ProgressState(kind, kind == ProgressKind.Hidden ? 0 : percent);
            ProgressChanged?.Invoke(Progress);
            return;
        }
        if (text.Length > 0) Notify?.Invoke("", text);
    }

    void Notification(string text) {
        int semi = text.IndexOf(';');
        if (semi < 0) { Notify?.Invoke("", text); return; }
        Notify?.Invoke(text[..semi], text[(semi + 1)..]);
    }

    // OSC 99 (kitty desktop notifications): "metadata;body". Metadata is ':'-separated k=v; a "title=" token names
    // the toast, everything after the second ';' is the body. A lone "OSC 99 ; body" treats the payload as the body.
    void KittyNotification(string text) {
        int semi = text.IndexOf(';');
        if (semi < 0) { if (text.Length > 0) Notify?.Invoke("", text); return; }
        string title = "";
        foreach (var kv in text[..semi].Split(':', StringSplitOptions.RemoveEmptyEntries))
            if (kv.StartsWith("title=", StringComparison.Ordinal)) title = kv[6..];
        var body = text[(semi + 1)..];
        if (body.Length > 0 || title.Length > 0) Notify?.Invoke(title, body);
    }

    void Clipboard(string text) {
        int semi = text.IndexOf(';');
        if (semi < 0) return;
        var data = text[(semi + 1)..];
        if (data == "?") return;   // clipboard read: denied by default (host may add it later)
        try {
            var bytes = Convert.FromBase64String(data);
            ClipboardWrite?.Invoke(Encoding.UTF8.GetString(bytes));
        } catch (FormatException) { /* not base64: ignore */ }
    }

    void ColorQuery(int code, string text) {
        int index = code switch { 10 => 256, 11 => 257, _ => 258 };
        if (text == "?") {
            var c = ColorProvider?.Invoke(index);
            if (c is { } rgb) Respond($"\x1b]{code};{Rgb16(rgb)}\x1b\\");
            return;
        }
        if (TryParseColorSpec(text, out var r, out var g, out var b)) PaletteSet?.Invoke(index, r, g, b);
    }

    void Palette(string text) {
        var parts = text.Split(';');
        for (int i = 0; i + 1 < parts.Length; i += 2) {
            if (!int.TryParse(parts[i], out var index) || index < 0 || index > 255) continue;
            if (parts[i + 1] == "?") {
                var c = ColorProvider?.Invoke(index);
                if (c is { } rgb) Respond($"\x1b]4;{index};{Rgb16(rgb)}\x1b\\");
            } else if (TryParseColorSpec(parts[i + 1], out var r, out var g, out var b)) {
                PaletteSet?.Invoke(index, r, g, b);
            }
        }
    }

    static string Rgb16((byte R, byte G, byte B) c) => $"rgb:{c.R:x2}{c.R:x2}/{c.G:x2}{c.G:x2}/{c.B:x2}{c.B:x2}";

    /// <summary>Accepts "#rrggbb", "rgb:rr/gg/bb" and "rgb:rrrr/gggg/bbbb" (high byte used).</summary>
    static bool TryParseColorSpec(string spec, out byte r, out byte g, out byte b) {
        r = g = b = 0;
        if (spec.Length == 7 && spec[0] == '#') {
            return byte.TryParse(spec.AsSpan(1, 2), NumberStyles.HexNumber, null, out r)
                && byte.TryParse(spec.AsSpan(3, 2), NumberStyles.HexNumber, null, out g)
                && byte.TryParse(spec.AsSpan(5, 2), NumberStyles.HexNumber, null, out b);
        }
        if (!spec.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = spec[4..].Split('/');
        if (parts.Length != 3) return false;
        return TryHexChannel(parts[0], out r) && TryHexChannel(parts[1], out g) && TryHexChannel(parts[2], out b);
    }

    static bool TryHexChannel(string s, out byte value) {
        value = 0;
        if (s.Length is not (1 or 2 or 3 or 4)) return false;
        if (!int.TryParse(s, NumberStyles.HexNumber, null, out var v)) return false;
        value = s.Length switch { 1 => (byte)(v * 17), 2 => (byte)v, 3 => (byte)(v >> 4), _ => (byte)(v >> 8) };
        return true;
    }
}
