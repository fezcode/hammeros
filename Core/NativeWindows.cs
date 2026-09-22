using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HammerOS.Core;

public record DesktopWindow(IntPtr Handle, int ProcessId, string Title, string Application);

public static class NativeWindows
{
    public static IReadOnlyList<DesktopWindow> Enumerate()
    {
        var windows = new List<DesktopWindow>(); if (!OperatingSystem.IsWindows()) return windows;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero || GetWindowTextLength(window) == 0) return true;
            GetWindowThreadProcessId(window, out var pid); if (pid == Environment.ProcessId) return true;
            var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
            var type = new StringBuilder(128); GetClassName(window, type, type.Capacity); if (type.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return true;
            var name = "Windows app"; try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch (Exception ex) when (ex is ArgumentException or Win32Exception or InvalidOperationException) { }
            windows.Add(new(window, (int)pid, title.ToString(), name)); return true;
        }, IntPtr.Zero);
        return windows.OrderBy(x => x.Application).ThenBy(x => x.Title).ToArray();
    }
    public static void RequestClose(IntPtr window) => PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero);
    public static bool Exists(IntPtr window) => OperatingSystem.IsWindows() && IsWindow(window);
    public static bool BelongsTo(DesktopWindow window) { if (!Exists(window.Handle)) return false; GetWindowThreadProcessId(window.Handle, out var pid); return pid == window.ProcessId; }
    public static void ResizeChild(IntPtr window, IntPtr parent)
    { if (IsWindow(window) && GetClientRect(parent, out var rect)) SetWindowPos(window, IntPtr.Zero, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), 0x0010 | 0x0004); }
    public static string Title(IntPtr window) { var text = new StringBuilder(512); GetWindowText(window, text, text.Capacity); return text.ToString(); }

    internal delegate bool EnumWindowProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumWindowProc callback, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);
    [DllImport("user32.dll")] internal static extern bool SetWindowPlacement(IntPtr window, ref WindowPlacement placement);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [StructLayout(LayoutKind.Sequential)] public struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct WindowPlacement { public int Length, Flags, Show; public NativePoint Min, Max; public NativeRect Normal; }
}

/// <summary>Temporarily borrows a window. Closing Tiled restores it; it never terminates its process.</summary>
public sealed class WindowAttachment : IDisposable
{
    public DesktopWindow Window { get; }
    public bool IsAttached { get; private set; }
    public IntPtr Container { get; private set; }
    private readonly IntPtr _parent, _style, _extended;
    private int _width = -1, _height = -1;
    private NativeWindows.WindowPlacement _placement;
    public WindowAttachment(DesktopWindow window)
    {
        if (!NativeWindows.BelongsTo(window)) throw new InvalidOperationException("This window has already closed.");
        Window = window; _parent = NativeWindows.GetParent(window.Handle); _style = NativeWindows.GetWindowLongPtr(window.Handle, -16); _extended = NativeWindows.GetWindowLongPtr(window.Handle, -20);
        _placement.Length = Marshal.SizeOf<NativeWindows.WindowPlacement>(); if (!NativeWindows.GetWindowPlacement(window.Handle, ref _placement)) throw new Win32Exception();
    }
    public void Attach(IntPtr container)
    {
        if (IsAttached) return;
        if (!NativeWindows.BelongsTo(Window)) throw new InvalidOperationException("The application window closed before it could be attached.");
        IsAttached = true;
        try
        {
            var style = (_style.ToInt64() & ~0x80000000L & ~0x00C40000L) | 0x40000000L; // CHILD, no POPUP/caption/frame.
            Marshal.SetLastPInvokeError(0); NativeWindows.SetWindowLongPtr(Window.Handle, -16, new IntPtr(style)); var error = Marshal.GetLastPInvokeError(); if (error != 0) throw new Win32Exception(error);
            NativeWindows.SetWindowLongPtr(Window.Handle, -20, new IntPtr(_extended.ToInt64() & ~0x40000L));
            Marshal.SetLastPInvokeError(0); NativeWindows.SetParent(Window.Handle, container); error = Marshal.GetLastPInvokeError(); if (error != 0 || NativeWindows.GetParent(Window.Handle) != container) throw new Win32Exception(error, "Windows could not embed this application. Try a non-elevated desktop app.");
            Container = container; _width = _height = -1; NativeWindows.ShowWindow(Window.Handle, 9); NativeWindows.SetWindowPos(Window.Handle, IntPtr.Zero, 0, 0, 100, 100, 0x0020 | 0x0004 | 0x0010); Resize();
        }
        catch { Detach(); throw; }
    }
    public void Resize()
    {
        if (!IsAttached || !NativeWindows.GetClientRect(Container, out var rect)) return;
        if (_width == rect.Right && _height == rect.Bottom) return;
        _width = rect.Right; _height = rect.Bottom; NativeWindows.ResizeChild(Window.Handle, Container);
    }
    public void Detach()
    {
        if (!IsAttached) return; IsAttached = false; Container = IntPtr.Zero;
        if (!NativeWindows.BelongsTo(Window)) return;
        NativeWindows.ShowWindow(Window.Handle, 0);
        NativeWindows.SetParent(Window.Handle, _parent);
        NativeWindows.SetWindowLongPtr(Window.Handle, -16, _style); NativeWindows.SetWindowLongPtr(Window.Handle, -20, _extended);
        NativeWindows.SetWindowPlacement(Window.Handle, ref _placement);
        NativeWindows.SetWindowPos(Window.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0004 | 0x0002 | 0x0001 | 0x0010);
        NativeWindows.ShowWindow(Window.Handle, _placement.Show is >= 1 and <= 11 ? _placement.Show : 5);
    }
    public void Dispose() => Detach();
}
