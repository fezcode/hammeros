namespace Clockt.Terminal;

public enum TermColorKind : byte { Default = 0, Indexed = 1, Rgb = 2 }

/// <summary>A terminal colour reference: the theme default, a 0–255 palette index, or a truecolor value.</summary>
public readonly record struct TermColor(TermColorKind Kind, byte Index, byte R, byte G, byte B) {
    public static readonly TermColor Default = default;
    public static TermColor Indexed(byte index) => new(TermColorKind.Indexed, index, 0, 0, 0);
    public static TermColor Rgb(byte r, byte g, byte b) => new(TermColorKind.Rgb, 0, r, g, b);
    public bool IsDefault => Kind == TermColorKind.Default;
}
