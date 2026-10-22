using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SetupPulse.App;

internal sealed record BluetoothAdapter(string Id, string Name, bool? Enabled, bool Started, uint? ProblemCode);

// Read-only discovery. No service control, elevation, device enable/disable, or pairing changes.
internal static class BluetoothInventory
{
    static readonly Guid BluetoothClass = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");
    [StructLayout(LayoutKind.Sequential)]
    struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DeviceInstance; public UIntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr window, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo device);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DeviceInfo device, StringBuilder id, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfo device, uint property, out uint type, byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")]
    static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint instance, uint flags);

    static string? Property(IntPtr set, ref DeviceInfo device, uint property)
    {
        byte[] buffer = new byte[2048];
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (SetupDiGetDeviceRegistryPropertyW(set, ref device, property, out uint type, buffer, (uint)buffer.Length, out uint required))
                return type is 1 or 7 && required <= buffer.Length ? Encoding.Unicode.GetString(buffer, 0, (int)required).TrimEnd('\0') : null;
            int error = Marshal.GetLastWin32Error();
            if (error == 13) return null; // Optional property absent.
            if (error == 122 && required is > 0 and <= 65536) { buffer = new byte[required]; continue; }
            throw new Win32Exception(error, "Bluetooth-Geräteeigenschaft konnte nicht gelesen werden.");
        }
        throw new InvalidOperationException("Bluetooth-Geräteeigenschaft hat sich während der Abfrage geändert. Bitte aktualisieren.");
    }

    public static List<BluetoothAdapter> Inventory()
    {
        var classGuid = BluetoothClass;
        var set = SetupDiGetClassDevsW(ref classGuid, null, IntPtr.Zero, 2); // DIGCF_PRESENT includes disabled attached adapters.
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Bluetooth-Adapter konnten nicht gelesen werden.");
        try
        {
            var result = new List<BluetoothAdapter>();
            for (uint index = 0; ; index++)
            {
                var device = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref device))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error, "Bluetooth-Geräteliste konnte nicht gelesen werden.");
                }
                var buffer = new StringBuilder(1024);
                if (!SetupDiGetDeviceInstanceIdW(set, ref device, buffer, (uint)buffer.Capacity, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Bluetooth-Gerätekennung fehlt.");
                string id = buffer.ToString();
                // Physical transport devices only; excludes BTH/BTHENUM/BTHLE children, ROOT and SWD enumerators.
                // Integrated controllers can use USB, PCI or ACPI; manufacturer and connection location are irrelevant.
                if (device.ClassGuid != BluetoothClass || !new[] { "USB\\", "PCI\\", "ACPI\\" }.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
                string? name = Property(set, ref device, 12);
                if (string.IsNullOrWhiteSpace(name)) name = Property(set, ref device, 0);
                if (string.IsNullOrWhiteSpace(name)) name = "Bluetooth-Adapter";
                uint config = CM_Get_DevNode_Status(out uint status, out uint problem, device.DeviceInstance, 0);
                result.Add(new(id, name.Trim(), config == 0 ? problem != 22 : null,
                    config == 0 && problem == 0 && (status & 8) != 0, config == 0 ? problem : null));
            }
            return result.OrderByDescending(adapter => adapter.Started).ThenBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    public static List<BluetoothAdapter> Demo() => [
        new("demo-bluetooth-1", "Demo-Adapter 1", true, true, 0),
        new("demo-bluetooth-2", "Demo-Adapter 2", false, false, 22),
        new("demo-bluetooth-3", "Demo-Adapter 3", true, false, 31)
    ];
}
