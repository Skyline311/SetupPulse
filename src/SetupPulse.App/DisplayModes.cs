using System.Runtime.InteropServices;
using SetupPulse.Core;

namespace SetupPulse.App;

internal static class DisplayModes
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Device
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        public ushort Spec, Driver, Size, Extra;
        public uint Fields;
        public int X, Y;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Form;
        public ushort LogPixels;
        public uint Bits, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, Media, Dither, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [StructLayout(LayoutKind.Sequential)] struct Size { public uint Width, Height; }
    [StructLayout(LayoutKind.Sequential)] struct Signal { public ulong PixelRate; public Displays.Rational HSync, VSync; public Size Active, Total; public uint Standard, Scan; }
    [StructLayout(LayoutKind.Sequential)] struct Preferred { public Displays.Header Header; public uint Width, Height; public Signal Signal; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string? device, uint index, ref Device info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string name, int index, ref DevMode mode);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetPreferred(ref Preferred mode);
    public static Dictionary<string, string> Bindings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (uint i = 0; i < 128; i++)
        {
            var adapter = new Device { Size = (uint)Marshal.SizeOf<Device>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            for (uint j = 0; j < 128; j++)
            {
                var monitor = new Device { Size = (uint)Marshal.SizeOf<Device>() };
                if (!EnumDisplayDevices(adapter.Name, j, ref monitor, 1)) break;
                if (!string.IsNullOrWhiteSpace(monitor.Id)) result.TryAdd(monitor.Id, adapter.Name);
            }
        }
        return result;
    }
    public static List<DisplayMode> Read(Displays.DisplayPath path, string? gdiName, DisplaySetting? current)
    {
        var preferred = new Preferred { Header = new Displays.Header { Type = 3, Size = (uint)Marshal.SizeOf<Preferred>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
        bool hasPreferred = GetPreferred(ref preferred) == 0 && preferred.Width > 0 && preferred.Height > 0;
        double preferredHz = preferred.Signal.VSync.Denominator == 0 ? 60 : (double)preferred.Signal.VSync.Numerator / preferred.Signal.VSync.Denominator;
        var result = new List<DisplayMode>();
        if (gdiName != null)
        {
            for (int i = 0; i < 10000; i++)
            {
                var mode = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };
                if (!EnumDisplaySettings(gdiName, i, ref mode)) break;
                if (mode.Bits != 32 || mode.Width < 320 || mode.Height < 200 || mode.Frequency < 20 || (mode.Flags & 2) != 0) continue;
                // Publish landscape/native dimensions; the UI rotates them for portrait layouts.
                bool portrait = mode.Orientation is 1 or 3;
                result.Add(new((int)(portrait ? mode.Height : mode.Width), (int)(portrait ? mode.Width : mode.Height), mode.Frequency));
            }
        }
        if (hasPreferred)
        {
            result.Add(new((int)preferred.Width, (int)preferred.Height, Math.Round(preferredHz, 3), true));
        }
        if (current != null) result.Add(new(current.Rotation is 2 or 4 ? current.Height : current.Width, current.Rotation is 2 or 4 ? current.Width : current.Height, current.RefreshRate));
        return result.Select(m => m with { Recommended = hasPreferred && m.Width == preferred.Width && m.Height == preferred.Height && Math.Abs(m.RefreshRate - preferredHz) < .15 })
            .Distinct().OrderByDescending(m => (long)m.Width * m.Height).ThenByDescending(m => m.RefreshRate).ToList();
    }
}
