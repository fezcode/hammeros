namespace Clockt.Terminal;

[Flags]
public enum StyleFlags : byte {
    None = 0, Bold = 1, Dim = 2, Italic = 4, Strike = 8, Inverse = 16, Hidden = 32, Blink = 64
}

public enum UnderlineStyle : byte { None = 0, Single = 1, Double = 2, Curly = 3, Dotted = 4, Dashed = 5 }

/// <summary>Visual attributes of one cell. Value type so rows can be compared and run-length encoded cheaply.</summary>
public readonly record struct CellStyle(
    TermColor Fg, TermColor Bg, TermColor Underline, StyleFlags Flags, UnderlineStyle UnderlineStyle) {
    public static readonly CellStyle Default = default;
    public bool Has(StyleFlags f) => (Flags & f) != 0;
}
