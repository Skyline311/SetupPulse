using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;

namespace SetupPulse.App;

// ABI adapted from File-New-Project/EarTrumpet 2.3.0.0 (MIT); see THIRD-PARTY-LICENSES.txt.
// Windows does not publish this routing interface as a supported Core Audio contract.
internal sealed class AppAudioRouting : IDisposable
{
    const string RuntimeClass = "Windows.Media.Internal.AudioPolicyConfig";
    const string DevicePrefix = @"\\?\SWD#MMDEVAPI#";
    const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    const int ChangedApartment = unchecked((int)0x80010106);
    const int NotFound = unchecked((int)0x80070490);
    IntPtr factory;
    bool initialized;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int SetEndpointDelegate(IntPtr self, uint processId, int flow, int role, IntPtr deviceId);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetEndpointDelegate(IntPtr self, uint processId, int flow, int role, out IntPtr deviceId);

    [DllImport("combase.dll", ExactSpelling = true)]
    static extern int RoInitialize(uint type);
    [DllImport("combase.dll", ExactSpelling = true)]
    static extern void RoUninitialize();
    [DllImport("combase.dll", ExactSpelling = true)]
    static extern int RoGetActivationFactory(IntPtr classId, in Guid iid, out IntPtr factory);
    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string source, uint length, out IntPtr value);
    [DllImport("combase.dll", ExactSpelling = true)]
    static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll", ExactSpelling = true)]
    static extern IntPtr WindowsGetStringRawBuffer(IntPtr value, out uint length);

    AppAudioRouting()
    {
        try
        {
            int hr = RoInitialize(1); // Task.Run uses an MTA; balance even S_FALSE.
            if (hr < 0 && hr != ChangedApartment) Marshal.ThrowExceptionForHR(hr);
            initialized = hr >= 0;
            var modern = new Guid("ab3d4648-e242-459f-b02f-541c70306324");
            var downlevel = new Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258");
            var first = Environment.OSVersion.Version.Build >= 21390 ? modern : downlevel;
            var second = first == modern ? downlevel : modern;
            var className = CreateString(RuntimeClass);
            try
            {
                hr = RoGetActivationFactory(className, in first, out factory);
                if (hr < 0)
                {
                    if (factory != IntPtr.Zero) { Marshal.Release(factory); factory = IntPtr.Zero; }
                    hr = RoGetActivationFactory(className, in second, out factory);
                }
                Marshal.ThrowExceptionForHR(hr);
                if (factory == IntPtr.Zero) throw new InvalidOperationException("Windows hat keine Schnittstelle für App-Audioausgänge bereitgestellt.");
            }
            finally { WindowsDeleteString(className); }
        }
        catch { Dispose(); throw; }
    }

    public static AppAudioRouting? TryCreate(out string? warning)
    {
        try { var routing = new AppAudioRouting(); warning = null; return routing; }
        catch (Exception ex) when (ex is COMException or PlatformNotSupportedException or EntryPointNotFoundException or DllNotFoundException or InvalidOperationException)
        {
            warning = $"App-Audioausgänge sind unter dieser Windows-Version nicht verfügbar (0x{ex.HResult:X8}). Lautstärke und Stummschaltung bleiben verfügbar.";
            return null;
        }
    }

    static IntPtr CreateString(string value)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(value, (uint)value.Length, out var result));
        return result;
    }

    T Method<T>(int slot) where T : Delegate
    {
        ObjectDisposedException.ThrowIf(factory == IntPtr.Zero, this);
        var table = Marshal.ReadIntPtr(factory);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, slot * IntPtr.Size));
    }

    string? ReadRaw(uint processId, Role role)
    {
        // IInspectable has six base slots, followed by nineteen unused methods.
        var call = Method<GetEndpointDelegate>(26);
        IntPtr value = IntPtr.Zero;
        try
        {
            int hr = call(factory, processId, (int)DataFlow.Render, (int)role, out value);
            if (hr == NotFound) return null;
            Marshal.ThrowExceptionForHR(hr);
            if (value == IntPtr.Zero) return null;
            var text = WindowsGetStringRawBuffer(value, out uint length);
            return length == 0 ? null : Marshal.PtrToStringUni(text, checked((int)length));
        }
        finally { if (value != IntPtr.Zero) WindowsDeleteString(value); }
    }

    static string? Unpack(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)) value = value[DevicePrefix.Length..];
        if (value.EndsWith(RenderSuffix, StringComparison.OrdinalIgnoreCase)) value = value[..^RenderSuffix.Length];
        return value;
    }

    public string? Get(uint processId) => Unpack(ReadRaw(processId, Role.Multimedia));

    void WriteRaw(uint processId, Role role, string? rawDeviceId)
    {
        IntPtr value = IntPtr.Zero;
        try
        {
            if (!string.IsNullOrEmpty(rawDeviceId)) value = CreateString(rawDeviceId);
            int hr = Method<SetEndpointDelegate>(25)(factory, processId, (int)DataFlow.Render, (int)role, value);
            if (hr < 0) throw new COMException($"Windows konnte den App-Ausgang für {RoleName(role)} nicht ändern (0x{hr:X8}).", hr);
        }
        finally { if (value != IntPtr.Zero) WindowsDeleteString(value); }
    }

    public void Set(uint processId, string? deviceId)
    {
        var roles = new[] { Role.Multimedia, Role.Console };
        // Read both before any write so a partial operation can restore each role independently.
        var before = roles.Select(role => ReadRaw(processId, role)).ToArray();
        string? target = deviceId == null ? null : DevicePrefix + deviceId + RenderSuffix;
        try
        {
            foreach (var role in roles) WriteRaw(processId, role, target);
            foreach (var role in roles)
                if (!string.Equals(Unpack(ReadRaw(processId, role)), deviceId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Windows hat den App-Ausgang für {RoleName(role)} nicht bestätigt.");
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            for (int i = 0; i < roles.Length; i++)
            {
                try
                {
                    WriteRaw(processId, roles[i], before[i]);
                    if (!string.Equals(ReadRaw(processId, roles[i]), before[i], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Windows hat die Wiederherstellung des App-Ausgangs für {RoleName(roles[i])} nicht bestätigt.");
                }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            if (errors.Count > 1) throw new AggregateException("Der App-Ausgang konnte nicht geändert und die vorherigen Einstellungen konnten nicht vollständig wiederhergestellt werden.", errors);
            throw new InvalidOperationException("Der App-Ausgang konnte nicht geändert werden. Die vorherigen Einstellungen wurden wiederhergestellt. " + failure.Message, failure);
        }
    }

    static string RoleName(Role role) => role == Role.Multimedia ? "Multimedia" : "Standardwiedergabe";

    public void Dispose()
    {
        if (factory != IntPtr.Zero) { Marshal.Release(factory); factory = IntPtr.Zero; }
        if (initialized) { RoUninitialize(); initialized = false; }
    }
}
