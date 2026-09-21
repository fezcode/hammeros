using System.Text;

namespace Clockt.Terminal.Parser;

/// <summary>Callbacks the <see cref="VtParser"/> drives. Implementations must not throw.</summary>
public interface IVtActions {
    void Print(Rune r);
    void Execute(byte control);
    void EscDispatch(ReadOnlySpan<byte> intermediates, byte final);
    void CsiDispatch(byte prefix, in CsiParams p, ReadOnlySpan<byte> intermediates, byte final);
    void OscDispatch(ReadOnlySpan<byte> payload);
    void DcsHook(byte prefix, in CsiParams p, ReadOnlySpan<byte> intermediates, byte final);
    void DcsPut(byte b);
    void DcsUnhook();
}
