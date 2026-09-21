using System.Text;

namespace Clockt.Terminal.Input;

/// <summary>
/// Parses a key name a script types — "enter", "ctrl+c", "shift+tab", "C-c", "f5" — into a <see cref="KeyInput"/>.
/// Modifiers may come in any order and are case-insensitive; the last segment is the key itself. Used by
/// `clockt send --key`, so the names here are the automation contract (see docs/automation.md).
/// </summary>
public static class KeySpec {
    public static bool TryParse(string? spec, out KeyInput key) {
        key = default;
        if (string.IsNullOrWhiteSpace(spec)) return false;
        var text = spec.Trim();

        // Emacs-style prefixes: C-c, M-x, A-x, S-tab. A bare "-" stays the minus key.
        var mods = TermModifiers.None;
        while (text.Length > 2 && text[1] == '-' && TryEmacsPrefix(text[0], out var prefix)) {
            if ((mods & prefix) != 0) return false;
            mods |= prefix;
            text = text[2..];
        }

        // "+" on its own and a trailing "…++" both mean the plus key; "ctrl+" is incomplete.
        var parts = text.Split('+');
        if (parts.Length >= 2 && parts[^1].Length == 0 && parts[^2].Length == 0) parts = [.. parts[..^2], "+"];
        if (parts.Any(p => p.Length == 0)) return false;
        for (int i = 0; i < parts.Length - 1; i++) {
            if (!TryModifier(parts[i], out var m) || (mods & m) != 0) return false;
            mods |= m;
        }
        if (!TryKey(parts[^1], out var named, out var rune)) return false;
        key = named == TermKey.Char ? KeyInput.Text(rune, mods) : KeyInput.Special(named, mods);
        return true;
    }

    static bool TryEmacsPrefix(char c, out TermModifiers mods) {
        mods = char.ToLowerInvariant(c) switch {
            'c' => TermModifiers.Ctrl,
            'm' or 'a' => TermModifiers.Alt,
            's' => TermModifiers.Shift,
            _ => TermModifiers.None,
        };
        return mods != TermModifiers.None;
    }

    static bool TryModifier(string part, out TermModifiers mods) {
        mods = part.ToLowerInvariant() switch {
            "ctrl" or "control" => TermModifiers.Ctrl,
            "alt" or "opt" or "option" => TermModifiers.Alt,
            "shift" => TermModifiers.Shift,
            "meta" or "win" or "cmd" or "super" => TermModifiers.Meta,
            _ => TermModifiers.None,
        };
        return mods != TermModifiers.None;
    }

    static bool TryKey(string part, out TermKey named, out Rune rune) {
        named = TermKey.None;
        rune = default;
        switch (part.ToLowerInvariant()) {
            case "enter" or "return" or "cr": named = TermKey.Enter; return true;
            case "tab": named = TermKey.Tab; return true;
            case "backspace" or "bs": named = TermKey.Backspace; return true;
            case "escape" or "esc": named = TermKey.Escape; return true;
            case "delete" or "del": named = TermKey.Delete; return true;
            case "insert" or "ins": named = TermKey.Insert; return true;
            case "home": named = TermKey.Home; return true;
            case "end": named = TermKey.End; return true;
            case "pageup" or "pgup": named = TermKey.PageUp; return true;
            case "pagedown" or "pgdn" or "pgdown": named = TermKey.PageDown; return true;
            case "up": named = TermKey.Up; return true;
            case "down": named = TermKey.Down; return true;
            case "left": named = TermKey.Left; return true;
            case "right": named = TermKey.Right; return true;
            case "space": named = TermKey.Char; rune = new Rune(' '); return true;
            case "plus": named = TermKey.Char; rune = new Rune('+'); return true;
            case "minus" or "dash": named = TermKey.Char; rune = new Rune('-'); return true;
            case ['f', ..] when int.TryParse(part.AsSpan(1), out var n) && n is >= 1 and <= 12:
                named = (TermKey)((int)TermKey.F1 + n - 1);
                return true;
        }
        // Anything else must be a single character: "a", "7", "/".
        var runes = part.EnumerateRunes();
        if (!runes.MoveNext()) return false;
        rune = runes.Current;
        if (runes.MoveNext()) return false;
        named = TermKey.Char;
        return true;
    }
}
