using System.Runtime.InteropServices;

namespace HammerOS.Core;

public static class SystemDevices
{
    public static string Battery()
    { if (!GetSystemPowerStatus(out var p)) return "Battery unavailable"; if (p.Flag == 128) return "AC power"; return (p.Percent == 255 ? "Battery" : $"Battery {p.Percent}%") + (p.AC == 1 ? " · plugged in" : " · on battery"); }
    public static float Volume { get => WithEndpoint(v => { v.GetMasterVolumeLevelScalar(out var level); return level; }); set => WithEndpoint(v => { var id = Guid.Empty; v.SetMasterVolumeLevelScalar(Math.Clamp(value, 0, 1), ref id); return 0; }); }
    public static bool Muted { get => WithEndpoint(v => { v.GetMute(out var mute); return mute; }); set => WithEndpoint(v => { var id = Guid.Empty; v.SetMute(value, ref id); return 0; }); }
    private static T WithEndpoint<T>(Func<IAudioEndpointVolume,T> action)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows audio is required.");
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        IMMDevice? device = null; object? endpoint = null;
        try { enumerator.GetDefaultAudioEndpoint(0, 1, out device); var iid = typeof(IAudioEndpointVolume).GUID; device.Activate(ref iid, 23, IntPtr.Zero, out endpoint); return action((IAudioEndpointVolume)endpoint); }
        finally { if (endpoint is not null) Marshal.ReleaseComObject(endpoint); if (device is not null) Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Power { public byte AC, Flag, Percent, Reserved; public uint Remaining, Full; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out Power status);
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceEnumerator
    { void EnumAudioEndpoints(int flow, uint mask, out IntPtr collection); void GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDevice
    { void Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify); void UnregisterControlChangeNotify(IntPtr notify); void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float level, ref Guid context); void SetMasterVolumeLevelScalar(float level, ref Guid context); void GetMasterVolumeLevel(out float level); void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float level, ref Guid context); void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context); void GetChannelVolumeLevel(uint channel, out float level); void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context); void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
