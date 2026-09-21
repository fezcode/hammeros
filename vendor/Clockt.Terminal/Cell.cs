using System.Text;

namespace Clockt.Terminal;

[Flags]
public enum CellFlags : byte { None = 0, Wide = 1, WideContinuation = 2 }

/// <summary>
/// One grid cell. <see cref="Content"/> is a Unicode scalar value, or — when
/// <see cref="GraphemeBit"/> is set — an index into the screen's <c>GraphemePool</c>
/// for clusters that need more than one codepoint (emoji ZWJ sequences, combining marks).
/// Content 0 means "never written" and renders as a blank in <see cref="Style"/>.
/// </summary>
public record struct Cell(uint Content, CellStyle Style, ushort HyperlinkId, CellFlags Flags) {
    public const uint GraphemeBit = 0x8000_0000u;

    public bool IsGrapheme => (Content & GraphemeBit) != 0;
    public uint GraphemeIndex => Content & ~GraphemeBit;
    public bool IsBlank => Content == 0 || Content == ' ';
    public bool IsWide => (Flags & CellFlags.Wide) != 0;
    public bool IsWideContinuation => (Flags & CellFlags.WideContinuation) != 0;
    public Rune Rune => IsGrapheme || Content == 0 ? new Rune(' ') : new Rune((int)Content);

    public static Cell Blank(CellStyle style) => new(0, style, 0, CellFlags.None);
}
