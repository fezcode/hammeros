using System.Text;

namespace Clockt.Terminal;

/// <summary>
/// Byte-at-a-time UTF-8 decoder that never throws. Invalid input produces U+FFFD
/// following the "maximal subpart" practice: a broken sequence yields one
/// replacement and the offending byte is reprocessed as a potential lead byte.
/// </summary>
public struct Utf8Decoder {
    int _needed;      // continuation bytes still expected
    int _acc;         // accumulated scalar bits
    byte _lower;      // valid range for the next continuation byte (0 = default 0x80)
    byte _upper;      // (0 = default 0xBF)

    public bool InSequence => _needed > 0;

    public void Reset() { _needed = 0; _acc = 0; _lower = 0; _upper = 0; }

    /// <summary>Returns how many runes were completed (0–2).</summary>
    public int Push(byte b, out Rune first, out Rune second) {
        second = default;
        if (_needed == 0) return Start(b, out first);

        byte lo = _lower == 0 ? (byte)0x80 : _lower;
        byte hi = _upper == 0 ? (byte)0xBF : _upper;
        if (b < lo || b > hi) {
            // Broken sequence: emit U+FFFD and reprocess b as a new start byte.
            Reset();
            first = Rune.ReplacementChar;
            var n = Start(b, out var restarted);
            if (n == 1) { second = restarted; return 2; }
            return 1;
        }
        _acc = (_acc << 6) | (b & 0x3F);
        _lower = 0; _upper = 0;
        if (--_needed == 0) {
            first = Rune.IsValid(_acc) ? new Rune(_acc) : Rune.ReplacementChar;
            _acc = 0;
            return 1;
        }
        first = default;
        return 0;
    }

    int Start(byte b, out Rune rune) {
        _lower = 0; _upper = 0;
        if (b < 0x80) { rune = new Rune(b); return 1; }
        if (b >= 0xC2 && b <= 0xDF) { _needed = 1; _acc = b & 0x1F; rune = default; return 0; }
        if (b >= 0xE0 && b <= 0xEF) {
            _needed = 2; _acc = b & 0x0F;
            if (b == 0xE0) _lower = 0xA0;        // reject overlong
            else if (b == 0xED) _upper = 0x9F;   // reject surrogates
            rune = default; return 0;
        }
        if (b >= 0xF0 && b <= 0xF4) {
            _needed = 3; _acc = b & 0x07;
            if (b == 0xF0) _lower = 0x90;        // reject overlong
            else if (b == 0xF4) _upper = 0x8F;   // reject > U+10FFFF
            rune = default; return 0;
        }
        // 0x80–0xBF stray continuation, 0xC0/0xC1 overlong leads, 0xF5–0xFF invalid
        rune = Rune.ReplacementChar;
        return 1;
    }
}
