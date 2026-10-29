using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HammerOS.Core;

public static class ScreenCapture
{
    public static Bitmap Capture(int x, int y, int width, int height)
    {
        var screen = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(screen); var bitmap = CreateCompatibleBitmap(screen, width, height); var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, 0x00CC0020)) throw new IOException("Windows could not capture this display.");
            SelectObject(memory, old); var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var bytes = new byte[checked(width * height * 4)]; if (GetDIBits(memory, bitmap, 0, (uint)height, bytes, ref info, 0) == 0) throw new IOException("Screenshot pixels are unavailable.");
            for (var i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
            var result = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var locked = result.Lock()) for (var row = 0; row < height; row++) Marshal.Copy(bytes, row * width * 4, locked.Address + row * locked.RowBytes, width * 4);
            return result;
        }
        finally { SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint Used, Important; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int sx, int sy, uint op);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint count, byte[] pixels, ref BitmapInfo info, uint usage);
}
