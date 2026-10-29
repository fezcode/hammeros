using System.Globalization;

namespace HammerOS.Core;

public static class Calculator
{
    public static bool TryEvaluate(string expression, out string result)
    {
        result = ""; if (expression.Length is 0 or > 512) return false;
        try { var parser = new Parser(expression); var value = parser.Sum(); parser.Space(); if (parser.Position != expression.Length || !double.IsFinite(value)) return false; result = value.ToString("G12", CultureInfo.InvariantCulture); return true; } catch (Exception ex) when (ex is FormatException or OverflowException) { return false; }
    }
    private sealed class Parser(string text)
    {
        public int Position; private int _depth;
        public void Space() { while (Position < text.Length && char.IsWhiteSpace(text[Position])) Position++; }
        private bool Take(char c) { Space(); if (Position >= text.Length || text[Position] != c) return false; Position++; return true; }
        public double Sum() { var n = Product(); while (true) { if (Take('+')) n += Product(); else if (Take('-')) n -= Product(); else return n; } }
        private double Product() { var n = Unary(); while (true) { if (Take('*')) n *= Unary(); else if (Take('/')) n /= Unary(); else if (Take('%')) n %= Unary(); else return n; } }
        private double Unary() => Take('-') ? -Unary() : Take('+') ? Unary() : Power();
        private double Power() { var n = Atom(); return Take('^') ? Math.Pow(n, Unary()) : n; }
        private double Atom()
        {
            if (++_depth > 64) throw new FormatException();
            try { if (Take('(')) { var value = Sum(); if (!Take(')')) throw new FormatException(); return value; } Space(); var start = Position; while (Position < text.Length && (char.IsDigit(text[Position]) || text[Position] == '.')) Position++; if (start == Position) throw new FormatException(); return double.Parse(text[start..Position], CultureInfo.InvariantCulture); } finally { _depth--; }
        }
    }
}
