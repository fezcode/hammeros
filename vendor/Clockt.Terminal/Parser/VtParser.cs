using System.Text;

namespace Clockt.Terminal.Parser;

/// <summary>
/// DEC ANSI-compatible parser (after Paul Williams' state diagram) with UTF-8
/// decoding in the ground state. Bytes are consumed one at a time; nothing here
/// allocates per byte and nothing throws. Sequences the emulator does not
/// understand are still delivered so it can ignore them deliberately.
/// </summary>
public sealed class VtParser {
    public const int MaxOscBytes = 65536;
    public const int MaxParams = 32;
    public const int MaxSlots = 64;
    public const int MaxIntermediates = 4;

    readonly IVtActions _actions;
    ParserState _state;
    Utf8Decoder _utf8;

    readonly int[] _values = new int[MaxSlots];
    readonly int[] _start = new int[MaxParams + 1];
    int _paramCount, _slotCount;
    bool _needParam, _trailingSep;
    byte _prefix;
    readonly byte[] _intermediates = new byte[MaxIntermediates];
    int _intermediateCount;

    readonly byte[] _osc = new byte[MaxOscBytes];
    int _oscLen;
    bool _inDcs;   // StringEscape came from DcsPassthrough (needs Unhook)
    bool _dropping; // current parameter exceeded capacity; its digits/subparams are discarded

    public VtParser(IVtActions actions) { _actions = actions; Reset(); }

    public ParserState State => _state;

    public void Reset() {
        _state = ParserState.Ground;
        _utf8.Reset();
        ClearSequence();
    }

    public void Feed(ReadOnlySpan<byte> data) {
        foreach (var b in data) Advance(b);
    }

    public void Advance(byte b) {
        switch (_state) {
            case ParserState.Ground:             Ground(b); break;
            case ParserState.Escape:             Escape(b); break;
            case ParserState.EscapeIntermediate: EscapeIntermediate(b); break;
            case ParserState.CsiEntry:           CsiEntry(b); break;
            case ParserState.CsiParam:           CsiParam(b); break;
            case ParserState.CsiIntermediate:    CsiIntermediate(b); break;
            case ParserState.CsiIgnore:          CsiIgnore(b); break;
            case ParserState.DcsEntry:           DcsEntry(b); break;
            case ParserState.DcsParam:           DcsParam(b); break;
            case ParserState.DcsIntermediate:    DcsIntermediate(b); break;
            case ParserState.DcsIgnore:          DcsIgnore(b); break;
            case ParserState.DcsPassthrough:     DcsPassthrough(b); break;
            case ParserState.OscString:          OscString(b); break;
            case ParserState.OscEscape:          OscEscape(b); break;
            case ParserState.SosPmApcString:     SosPmApcString(b); break;
            case ParserState.StringEscape:       StringEscape(b); break;
        }
    }

    // ---- ground -------------------------------------------------------------

    void Ground(byte b) {
        if (b >= 0x80 || _utf8.InSequence) {
            var n = _utf8.Push(b, out var r1, out var r2);
            if (n >= 1) GroundRune(r1);
            if (n == 2) GroundRune(r2);
            return;
        }
        GroundAscii(b);
    }

    void GroundRune(Rune r) {
        int v = r.Value;
        if (v < 0x80) { GroundAscii((byte)v); return; }
        if (v <= 0x9F) { C1((byte)v); return; }
        _actions.Print(r);
    }

    void GroundAscii(byte b) {
        if (b == 0x1B) { Enter(ParserState.Escape); return; }
        if (b == 0x7F) return;
        if (b < 0x20) { _actions.Execute(b); return; }
        _actions.Print(new Rune(b));
    }

    void C1(byte c) {
        switch (c) {
            case 0x84: case 0x85: case 0x88: case 0x8D: _actions.Execute(c); break;   // IND NEL HTS RI
            case 0x90: Enter(ParserState.DcsEntry); break;
            case 0x9B: Enter(ParserState.CsiEntry); break;
            case 0x9D: Enter(ParserState.OscString); break;
            case 0x98: case 0x9E: case 0x9F: Enter(ParserState.SosPmApcString); break;
            default: break;   // ST and everything else: ignore
        }
    }

    // ---- escape -------------------------------------------------------------

    void Escape(byte b) {
        if (b >= 0x80) { Enter(ParserState.Ground); Ground(b); return; }
        if (Anywhere(b)) return;
        if (b < 0x20) { _actions.Execute(b); return; }
        if (b == 0x7F) return;
        if (b <= 0x2F) { Collect(b); _state = ParserState.EscapeIntermediate; return; }
        switch (b) {
            case (byte)'P': Enter(ParserState.DcsEntry); return;
            case (byte)'[': Enter(ParserState.CsiEntry); return;
            case (byte)']': Enter(ParserState.OscString); return;
            case (byte)'X': case (byte)'^': case (byte)'_': Enter(ParserState.SosPmApcString); return;
        }
        _actions.EscDispatch(Intermediates, b);
        Enter(ParserState.Ground);
    }

    void EscapeIntermediate(byte b) {
        if (b >= 0x80) { Enter(ParserState.Ground); Ground(b); return; }
        if (Anywhere(b)) return;
        if (b < 0x20) { _actions.Execute(b); return; }
        if (b == 0x7F) return;
        if (b <= 0x2F) { Collect(b); return; }
        _actions.EscDispatch(Intermediates, b);
        Enter(ParserState.Ground);
    }

    // ---- csi ----------------------------------------------------------------

    void CsiEntry(byte b) {
        if (b >= 0x80) { Enter(ParserState.Ground); Ground(b); return; }
        if (Anywhere(b)) return;
        if (b < 0x20) { _actions.Execute(b); return; }
        if (b == 0x7F) return;
        if (b <= 0x2F) { Collect(b); _state = ParserState.CsiIntermediate; return; }
        if (b <= 0x3B || b == (byte)':') { ParamByte(b); _state = ParserState.CsiParam; return; }
        if (b <= 0x3F) { _prefix = b; _state = ParserState.CsiParam; return; }
        DispatchCsi(b);
    }

    void CsiParam(byte b) {
        if (b >= 0x80) { Enter(ParserState.Ground); Ground(b); return; }
        if (Anywhere(b)) return;
        if (b < 0x20) { _actions.Execute(b); return; }
        if (b == 0x7F) return;
        if (b <= 0x2F) { Collect(b); _state = ParserState.CsiIntermediate; return; }
        if (b <= 0x3B) { ParamByte(b); return; }
        if (b <= 0x3F) { _state = ParserState.CsiIgnore; return; }
        DispatchCsi(b);
    }

    void CsiIntermediate(byte b) {
        if (b >= 0x80) { Enter(ParserState.Ground); Ground(b); return; }
        if (Anywhere(b)) return;
        if (b < 0x20) { _actions.Execute(b); return; }
        if (b == 0x7F) return;
        if (b <= 0x2F) { Collect(b); return; }
        if (b <= 0x3F) { _state = ParserState.CsiIgnore; return; }
        DispatchCsi(b);
    }

    void CsiIgnore(byte b) {
        if (b >= 0x80) { Enter(ParserState.Ground); Ground(b); return; }
        if (Anywhere(b)) return;
        if (b < 0x20) { _actions.Execute(b); return; }
        if (b >= 0x40 && b <= 0x7E) Enter(ParserState.Ground);
    }

    void DispatchCsi(byte final) {
        if (_trailingSep) StartParam();
        var p = new CsiParams(_values.AsSpan(0, _slotCount), _start.AsSpan(0, _paramCount + 1));
        _actions.CsiDispatch(_prefix, in p, Intermediates, final);
        Enter(ParserState.Ground);
    }

    // ---- dcs ----------------------------------------------------------------

    void DcsEntry(byte b) {
        if (b >= 0x80) { Enter(ParserState.Ground); Ground(b); return; }
        if (Anywhere(b)) return;
        if (b < 0x20 || b == 0x7F) return;
        if (b <= 0x2F) { Collect(b); _state = ParserState.DcsIntermediate; return; }
        if (b <= 0x3B || b == (byte)':') { ParamByte(b); _state = ParserState.DcsParam; return; }
        if (b <= 0x3F) { _prefix = b; _state = ParserState.DcsParam; return; }
        HookDcs(b);
    }

    void DcsParam(byte b) {
        if (b >= 0x80) { Enter(ParserState.Ground); Ground(b); return; }
        if (Anywhere(b)) return;
        if (b < 0x20 || b == 0x7F) return;
        if (b <= 0x2F) { Collect(b); _state = ParserState.DcsIntermediate; return; }
        if (b <= 0x3B) { ParamByte(b); return; }
        if (b <= 0x3F) { _state = ParserState.DcsIgnore; return; }
        HookDcs(b);
    }

    void DcsIntermediate(byte b) {
        if (b >= 0x80) { Enter(ParserState.Ground); Ground(b); return; }
        if (Anywhere(b)) return;
        if (b < 0x20 || b == 0x7F) return;
        if (b <= 0x2F) { Collect(b); return; }
        if (b <= 0x3F) { _state = ParserState.DcsIgnore; return; }
        HookDcs(b);
    }

    void DcsIgnore(byte b) {
        if (b == 0x1B) { _inDcs = false; _state = ParserState.StringEscape; return; }
        if (b == 0x18 || b == 0x1A) { _actions.Execute(b); Enter(ParserState.Ground); }
    }

    void HookDcs(byte final) {
        if (_trailingSep) StartParam();
        var p = new CsiParams(_values.AsSpan(0, _slotCount), _start.AsSpan(0, _paramCount + 1));
        _actions.DcsHook(_prefix, in p, Intermediates, final);
        _state = ParserState.DcsPassthrough;
    }

    void DcsPassthrough(byte b) {
        if (b == 0x1B) { _inDcs = true; _state = ParserState.StringEscape; return; }
        if (b == 0x18 || b == 0x1A) { _actions.DcsUnhook(); _actions.Execute(b); Enter(ParserState.Ground); return; }
        if (b == 0x7F) return;
        _actions.DcsPut(b);
    }

    // ---- osc / sos-pm-apc ---------------------------------------------------

    void OscString(byte b) {
        if (b == 0x07) { DispatchOsc(); Enter(ParserState.Ground); return; }
        if (b == 0x1B) { _state = ParserState.OscEscape; return; }
        if (b == 0x18 || b == 0x1A) { _actions.Execute(b); Enter(ParserState.Ground); return; }
        if (b < 0x20) return;
        if (_oscLen < MaxOscBytes) _osc[_oscLen++] = b;
    }

    void OscEscape(byte b) {
        DispatchOsc();
        if (b == (byte)'\\') { Enter(ParserState.Ground); return; }
        Enter(ParserState.Escape);
        Escape(b);
    }

    void SosPmApcString(byte b) {
        if (b == 0x1B) { _inDcs = false; _state = ParserState.StringEscape; return; }
        if (b == 0x18 || b == 0x1A) { _actions.Execute(b); Enter(ParserState.Ground); }
    }

    void StringEscape(byte b) {
        if (_inDcs) { _actions.DcsUnhook(); _inDcs = false; }
        if (b == (byte)'\\') { Enter(ParserState.Ground); return; }
        Enter(ParserState.Escape);
        Escape(b);
    }

    void DispatchOsc() {
        _actions.OscDispatch(_osc.AsSpan(0, _oscLen));
        _oscLen = 0;
    }

    // ---- helpers ------------------------------------------------------------

    /// <summary>ESC restarts a sequence; CAN/SUB abort it and are executed. Returns true if handled.</summary>
    bool Anywhere(byte b) {
        if (b == 0x1B) { Enter(ParserState.Escape); return true; }
        if (b == 0x18 || b == 0x1A) { _actions.Execute(b); Enter(ParserState.Ground); return true; }
        return false;
    }

    void Enter(ParserState s) {
        _state = s;
        switch (s) {
            case ParserState.Ground:
                ClearSequence();
                break;
            case ParserState.Escape:
            case ParserState.CsiEntry:
            case ParserState.DcsEntry:
                ClearSequence();
                break;
            case ParserState.OscString:
                _oscLen = 0;
                break;
        }
    }

    void ClearSequence() {
        _paramCount = 0; _slotCount = 0; _needParam = true; _trailingSep = false; _dropping = false;
        _prefix = 0; _intermediateCount = 0; _oscLen = 0; _inDcs = false;
    }

    ReadOnlySpan<byte> Intermediates => _intermediates.AsSpan(0, _intermediateCount);

    void Collect(byte b) {
        if (_intermediateCount < MaxIntermediates) _intermediates[_intermediateCount++] = b;
    }

    void ParamByte(byte b) {
        if (b == (byte)';') {
            if (_needParam) StartParam();
            _needParam = true; _trailingSep = true;
        } else if (b == (byte)':') {
            if (_needParam) StartParam();
            if (!_dropping) StartSub();
            _trailingSep = false;
        } else {   // digit
            if (_needParam) StartParam();
            _trailingSep = false;
            if (_dropping) return;
            ref int v = ref _values[_slotCount - 1];
            v = Math.Min(v * 10 + (b - '0'), 65535);
        }
    }

    void StartParam() {
        _needParam = false; _trailingSep = false;
        if (_paramCount >= MaxParams || _slotCount >= MaxSlots) { _dropping = true; return; }   // overflow: parameter dropped
        _dropping = false;
        _start[_paramCount] = _slotCount;
        _values[_slotCount++] = 0;
        _paramCount++;
        _start[_paramCount] = _slotCount;
    }

    void StartSub() {
        if (_paramCount == 0 || _slotCount >= MaxSlots) { _dropping = true; return; }
        _values[_slotCount++] = 0;
        _start[_paramCount] = _slotCount;
    }
}
