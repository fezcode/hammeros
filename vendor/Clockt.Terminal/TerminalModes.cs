namespace Clockt.Terminal;

/// <summary>ANSI and DEC private mode flags. Value type so snapshots copy it wholesale.</summary>
public struct TerminalModes {
    public bool CursorKeysApp;    // DECCKM  ?1
    public bool ReverseVideo;     // DECSCNM ?5
    public bool Origin;           // DECOM   ?6
    public bool AutoWrap;         // DECAWM  ?7   (default on)
    public bool MouseX10;         //         ?9
    public bool CursorBlink;      //         ?12
    public bool CursorVisible;    // DECTCEM ?25  (default on)
    public bool AltScreen;        // ?47 / ?1047 / ?1049
    public bool MouseNormal;      //         ?1000
    public bool MouseButton;      //         ?1002
    public bool MouseAny;         //         ?1003
    public bool FocusEvents;      //         ?1004
    public bool MouseUtf8;        //         ?1005
    public bool MouseSgr;         //         ?1006
    public bool MouseUrxvt;       //         ?1015
    public bool AltSendsEscape;   //         ?1036 (default on)
    public bool BracketedPaste;   //         ?2004
    public bool SyncOutput;       //         ?2026
    public bool Insert;           // IRM     4
    public bool NewLine;          // LNM     20
    public bool KeypadApp;        // DECKPAM / DECKPNM

    public static TerminalModes Default => new() { AutoWrap = true, CursorVisible = true, AltSendsEscape = true };

    public bool AnyMouse => MouseX10 || MouseNormal || MouseButton || MouseAny;
}
