namespace Clockt.Terminal.Parser;

public enum ParserState : byte {
    Ground,
    Escape, EscapeIntermediate,
    CsiEntry, CsiParam, CsiIntermediate, CsiIgnore,
    DcsEntry, DcsParam, DcsIntermediate, DcsIgnore, DcsPassthrough,
    OscString, OscEscape,
    SosPmApcString, StringEscape
}
