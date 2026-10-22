using System.Runtime.InteropServices;
using SetupPulse.Core;

namespace SetupPulse.App;

internal static class Displays
{
    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; public override readonly string ToString() => $"{High}:{Low}"; }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Source { public Luid Adapter; public uint Id, Mode, Status; }
    [StructLayout(LayoutKind.Sequential)] public struct Target { public Luid Adapter; public uint Id, Mode, Technology, Rotation, Scaling; public Rational Refresh; public uint Scan; public int Available; public uint Status; }
    [StructLayout(LayoutKind.Sequential)] public struct DisplayPath { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct SourceMode { public uint Width, Height, Format; public Point Position; }
    [StructLayout(LayoutKind.Explicit, Size = 48)] public struct ModeUnion { [FieldOffset(0)] public SourceMode Source; }
    [StructLayout(LayoutKind.Sequential)] public struct Mode { public uint Type, Id; public Luid Adapter; public ModeUnion Data; }
    [StructLayout(LayoutKind.Sequential)] public struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct TargetName { public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Friendly; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SourceName { public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct DpiGet { public Header Header; public int Min, Current, Max; }
    [StructLayout(LayoutKind.Sequential)] struct DpiSet { public Header Header; public int Value; }
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint pc, [Out] DisplayPath[] paths, ref uint mc, [Out] Mode[] modes, IntPtr topology);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint pc, DisplayPath[] paths, uint mc, Mode[] modes, uint flags);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetName(ref TargetName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetSourceName(ref SourceName name);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetDpi(ref DpiGet dpi);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] static extern int SetDpi(ref DpiSet dpi);
    static readonly int[] Scales = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];
    static void Check(int code, string action) { if (code != 0) throw new InvalidOperationException($"{action}: Windows-Fehler {code}."); }
    static Header H(uint type, int size, Source source) => new() { Type = type, Size = (uint)size, Adapter = source.Adapter, Id = source.Id };
    static (DisplayPath[] Paths, Mode[] Modes) Query(uint flags)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(flags, out uint pc, out uint mc), "Monitoranzahl");
            var paths = new DisplayPath[pc]; var modes = new Mode[mc];
            int code = QueryDisplayConfig(flags, ref pc, paths, ref mc, modes, IntPtr.Zero);
            if (code == 122) continue;
            Check(code, "Monitorabfrage"); Array.Resize(ref paths, (int)pc); Array.Resize(ref modes, (int)mc); return (paths, modes);
        }
        throw new InvalidOperationException("Monitorliste verändert sich. Bitte erneut versuchen.");
    }
    static (string Id, string Name) Identity(DisplayPath path)
    {
        var n = new TargetName { Header = new Header { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
        Check(GetName(ref n), "Monitoridentität"); return (n.Device, string.IsNullOrWhiteSpace(n.Friendly) ? n.Device : n.Friendly);
    }
    static DpiGet Dpi(Source source)
    {
        var d = new DpiGet { Header = H(unchecked((uint)-3), Marshal.SizeOf<DpiGet>(), source) };
        Check(GetDpi(ref d), "DPI-Kompatibilitätsabfrage");
        if (d.Min > 0 || d.Min > d.Max) throw new NotSupportedException("DPI-Bereich wird nicht unterstützt.");
        return d;
    }
    internal static int DpiTarget(int minimum, int maximum, int percent)
    {
        if (minimum > 0 || minimum > maximum) throw new NotSupportedException("DPI-Bereich wird nicht unterstützt.");
        int index = Array.IndexOf(Scales, percent);
        long relative = (long)index + minimum;
        if (index < 0 || relative < minimum || relative > maximum || relative < int.MinValue || relative > int.MaxValue)
            throw new NotSupportedException($"{percent}% wird nicht unterstützt.");
        return (int)relative;
    }
    public static void ValidateDpiAdapter() { foreach (var path in Query(2).Paths) Dpi(path.Source); }
    internal static int ScaleFromDpi(uint dpi) => dpi == 0 ? throw new InvalidOperationException("Effektive Monitor-DPI fehlt.") : checked((int)Math.Round(dpi * 100d / 96, MidpointRounding.AwayFromZero));
    static Dictionary<string, int> EffectiveScales()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); Exception? failure = null;
        var old = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (old == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Monitor-DPI-Kontext konnte nicht gesetzt werden.");
        try
        {
            MonitorCallback callback = (IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data) =>
            {
                try
                {
                    var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                    if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Monitorinformationen fehlen.");
                    // GetScaleFactorForMonitor may report legacy resource buckets such as140% for175%.
                    // A temporary, invisible per-monitor-v2 HWND reads actual window DPI on each monitor.
                    // WS_VISIBLE is deliberately absent: no activation, taskbar entry or UI appears.
                    var probe = CreateWindowExW(0x08000000, "STATIC", "", 0x80000000, rect.Left, rect.Top, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (probe == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Monitor-DPI konnte nicht gelesen werden.");
                    try { result.Add(info.Device, ScaleFromDpi(GetDpiForWindow(probe))); }
                    finally { DestroyWindow(probe); }
                }
                catch (Exception e) { failure = e; }
                return true;
            };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)) throw new InvalidOperationException("Effektive Monitorliste konnte nicht gelesen werden.");
            if (failure != null) throw failure; return result;
        }
        finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
    }
    static string GdiName(Source source) { var n = new SourceName { Header = H(1, Marshal.SizeOf<SourceName>(), source) }; Check(GetSourceName(ref n), "Anzeigequelle"); return n.Name; }
    public static List<DisplayDevice> Inventory()
    {
        var active = Query(2); var all = Query(1); var result = new List<DisplayDevice>(); var scales = EffectiveScales(); var bindings = DisplayModes.Bindings();
        foreach (var p in all.Paths.Where(x => x.Target.Available != 0))
        {
            var (id, name) = Identity(p); if (string.IsNullOrEmpty(id) || result.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
            DisplaySetting? current = null;
            foreach (var a in active.Paths)
            {
                if (!Identity(a).Id.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                var m = active.Modes[a.Source.Mode].Data.Source; bool rotate = a.Target.Rotation is 2 or 4;
                current = new(id, name, (int)(rotate ? m.Height : m.Width), (int)(rotate ? m.Width : m.Height), m.Position.X, m.Position.Y, (int)a.Target.Rotation, Math.Round((double)a.Target.Refresh.Numerator / Math.Max(1, a.Target.Refresh.Denominator), 3), scales.TryGetValue(GdiName(a.Source), out int scale) ? scale : throw new InvalidOperationException("Effektive Skalierung fehlt."), m.Position.X == 0 && m.Position.Y == 0);
                break;
            }
            result.Add(new(id, name, true, current != null, current, DisplayModes.Read(p, bindings.GetValueOrDefault(id), current)));
        }
        return result;
    }
    static (DisplayPath[] Paths, Mode[] Modes) Build(List<DisplaySetting> settings)
    {
        Validation.Displays(settings); var all = Query(1); var active = Query(2);
        var choices = settings.Select(d => all.Paths.Where(p => p.Target.Available != 0 && Identity(p).Id.Equals(d.DeviceId, StringComparison.OrdinalIgnoreCase)).OrderByDescending(p => active.Paths.Any(a => a.Source.Adapter.Equals(p.Source.Adapter) && a.Source.Id == p.Source.Id && a.Target.Id == p.Target.Id)).ToList()).ToList();
        var paths = new DisplayPath[settings.Count]; var used = new HashSet<string>();
        bool Match(int index)
        {
            if (index == paths.Length) return true;
            foreach (var p in choices[index]) { var key = $"{p.Source.Adapter}:{p.Source.Id}"; if (!used.Add(key)) continue; paths[index] = p; if (Match(index + 1)) return true; used.Remove(key); }
            return false;
        }
        if (!Match(0)) throw new DeviceUnavailableException("Nicht alle Monitore sind mit einer unabhängigen Anzeigequelle verfügbar.");
        var modes = new Mode[settings.Count];
        for (int i = 0; i < paths.Length; i++)
        {
            var d = settings[i]; var p = paths[i]; bool rotate = d.Rotation is 2 or 4;
            p.Flags = 1; p.Source.Mode = (uint)i; p.Target.Mode = uint.MaxValue; p.Target.Rotation = (uint)d.Rotation; p.Target.Scaling = 1; p.Target.Scan = 1;
            p.Target.Refresh = new Rational { Numerator = (uint)Math.Round(d.RefreshRate * 1000), Denominator = 1000 };
            paths[i] = p;
            modes[i] = new Mode { Type = 1, Id = p.Source.Id, Adapter = p.Source.Adapter, Data = new ModeUnion { Source = new SourceMode { Width = (uint)(rotate ? d.Height : d.Width), Height = (uint)(rotate ? d.Width : d.Height), Format = 4, Position = new Point { X = d.X, Y = d.Y } } } };
        }
        return (paths, modes);
    }
    internal static (DisplayPath[] Paths, Mode[] Modes) BuildPositionPlan(DisplayPath[] activePaths, Mode[] activeModes, List<DisplaySetting> settings, string[] identities)
    {
        Validation.Displays(settings);
        if (activePaths.Length != settings.Count || identities.Length != activePaths.Length)
            throw new InvalidOperationException("Die aktiven Monitore haben sich geändert. Live-Abgleich neu öffnen.");
        var paths = activePaths.ToArray(); var modes = activeModes.ToArray(); var used = new HashSet<uint>();
        for (int i = 0; i < paths.Length; i++)
        {
            var path = paths[i];
            var target = settings.SingleOrDefault(d => d.DeviceId.Equals(identities[i], StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Die Monitorzuordnung hat sich geändert. Live-Abgleich neu öffnen.");
            uint index = path.Source.Mode;
            if (index >= modes.Length || !used.Add(index) || modes[index].Type != 1 || modes[index].Id != path.Source.Id || !modes[index].Adapter.Equals(path.Source.Adapter))
                throw new NotSupportedException("Für den Live-Abgleich werden unabhängige Anzeigequellen benötigt.");
            var mode = modes[index]; var source = mode.Data.Source; bool rotate = path.Target.Rotation is 2 or 4;
            if ((rotate ? source.Height : source.Width) != target.Width || (rotate ? source.Width : source.Height) != target.Height
                || source.Position.X != target.X || path.Target.Rotation != target.Rotation
                || Math.Abs((double)path.Target.Refresh.Numerator / Math.Max(1, path.Target.Refresh.Denominator) - target.RefreshRate) > .15)
                throw new InvalidOperationException("Auflösung, Ausrichtung oder Bildwiederholrate haben sich geändert. Live-Abgleich neu öffnen.");
            // Preserve the complete current path and timing data. Only the source Y coordinate changes.
            mode.Data.Source.Position.Y = target.Y; modes[index] = mode;
        }
        return (paths, modes);
    }
    static (DisplayPath[] Paths, Mode[] Modes) Positions(List<DisplaySetting> settings)
    {
        var active = Query(2);
        return BuildPositionPlan(active.Paths, active.Modes, settings, active.Paths.Select(p => Identity(p).Id).ToArray());
    }
    public static void ValidatePositions(List<DisplaySetting> settings) { var (p, m) = Positions(settings); Check(SetDisplayConfig((uint)p.Length, p, (uint)m.Length, m, 0x60), "Höhenprüfung"); }
    public static void ApplyPositions(List<DisplaySetting> settings) { var (p, m) = Positions(settings); Check(SetDisplayConfig((uint)p.Length, p, (uint)m.Length, m, 0x2a0), "Monitorhöhe ändern"); }
    public static void Validate(List<DisplaySetting> settings) { var (p, m) = Build(settings); Check(SetDisplayConfig((uint)p.Length, p, (uint)m.Length, m, 0x60), "Monitorprüfung"); }
    public static void Apply(List<DisplaySetting> settings)
    {
        var (p, m) = Build(settings); Check(SetDisplayConfig((uint)p.Length, p, (uint)m.Length, m, 0x2a0), "Monitorwechsel");
        // Query again: source identifiers may change after topology application.
        var now = Query(2);
        foreach (var d in settings)
        {
            var path = now.Paths.Single(p => Identity(p).Id.Equals(d.DeviceId, StringComparison.OrdinalIgnoreCase));
            var dpi = Dpi(path.Source);
            int relative;
            try { relative = DpiTarget(dpi.Min, dpi.Max, d.Scale); }
            catch (NotSupportedException error) { throw new NotSupportedException($"{d.Name}: {error.Message}", error); }
            if (dpi.Current != relative) { var set = new DpiSet { Header = H(unchecked((uint)-4), Marshal.SizeOf<DpiSet>(), path.Source), Value = relative }; Check(SetDpi(ref set), "Skalierung setzen"); }
        }
    }
    public static void Verify(List<DisplaySetting> settings)
        => VerifyReadback(settings, () => Inventory().Where(x => x.Active).Select(x => x.Current!).ToList(), () => Thread.Sleep(100));
    internal static void VerifyReadback(List<DisplaySetting> settings, Func<List<DisplaySetting>> read, Action wait, int attempts = 21)
    {
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));
        string? mismatch = null;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var actual = read();
            mismatch = actual.Count != settings.Count ? $"Anzahl aktiver Monitore: erwartet {settings.Count}, erkannt {actual.Count}." : null;
            if (mismatch == null) foreach (var d in settings)
            {
                var a = actual.SingleOrDefault(x => x.DeviceId.Equals(d.DeviceId, StringComparison.OrdinalIgnoreCase));
                if (a == null) { mismatch = $"{d.Name}: Monitor ist noch nicht aktiv."; break; }
                var differences = new List<string>();
                if (a.Width != d.Width || a.Height != d.Height) differences.Add($"Auflösung erwartet {d.Width}×{d.Height}, erkannt {a.Width}×{a.Height}");
                if (a.X != d.X || a.Y != d.Y) differences.Add($"Position erwartet {d.X}/{d.Y}, erkannt {a.X}/{a.Y}");
                if (a.Rotation != d.Rotation) differences.Add($"Ausrichtung erwartet {d.Rotation}, erkannt {a.Rotation}");
                if (a.Scale != d.Scale) differences.Add($"Skalierung erwartet {d.Scale}%, erkannt {a.Scale}%");
                if (Math.Abs(a.RefreshRate - d.RefreshRate) > .15) differences.Add($"Hz erwartet {d.RefreshRate:0.###}, erkannt {a.RefreshRate:0.###}");
                if (differences.Count != 0) { mismatch = d.Name + ": " + string.Join("; ", differences) + "."; break; }
            }
            if (mismatch == null) return;
            // Windows' effective monitor/DPI readback may lag a successful topology/DPI call.
            // Read again without applying anything; permanent mismatches still fail and roll back.
            if (attempt + 1 < attempts) wait();
        }
        throw new InvalidOperationException("Monitorprüfung fehlgeschlagen: " + mismatch);
    }
}
