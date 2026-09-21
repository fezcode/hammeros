using System.Text;

namespace Clockt.Terminal.Input;

public enum TermKey : byte {
    None, Char, Enter, Tab, Backspace, Escape, Delete, Insert, Home, End, PageUp, PageDown,
    Up, Down, Left, Right, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
}

[Flags]
public enum TermModifiers : byte { None = 0, Shift = 1, Alt = 2, Ctrl = 4, Meta = 8 }

/// <summary>A key event already translated from the host toolkit: either a printable rune or a named key, plus modifiers.</summary>
public readonly record struct KeyInput(TermKey Key, Rune Char, TermModifiers Mods) {
    public static KeyInput Text(Rune r, TermModifiers mods = TermModifiers.None) => new(TermKey.Char, r, mods);
    public static KeyInput Special(TermKey key, TermModifiers mods = TermModifiers.None) => new(key, default, mods);
}
