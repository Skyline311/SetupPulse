using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using SetupPulse.Core;

namespace SetupPulse.App;

internal static class Audio
{
    public static void ValidatePolicyAdapter()
    {
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"), true)!)!;
        IntPtr unknown = IntPtr.Zero, policy = IntPtr.Zero;
        try { unknown = Marshal.GetIUnknownForObject(instance); var iid = new Guid("f8679f50-850a-41cf-9c72-430f290290c8"); Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out policy)); }
        finally { if (policy != IntPtr.Zero) Marshal.Release(policy); if (unknown != IntPtr.Zero) Marshal.Release(unknown); Marshal.FinalReleaseComObject(instance); }
    }
    // Undocumented Windows compatibility boundary; no dependency on AudioSwitcher binaries.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetDefaultDelegate(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    internal static void SetDefault(string id, Role role)
    {
        var clsid = new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");
        var iid = new Guid("f8679f50-850a-41cf-9c72-430f290290c8");
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true)!)!;
        IntPtr unknown = IntPtr.Zero, policy = IntPtr.Zero;
        try
        {
            unknown = Marshal.GetIUnknownForObject(instance);
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out policy));
            var vtable = Marshal.ReadIntPtr(policy);
            var call = Marshal.GetDelegateForFunctionPointer<SetDefaultDelegate>(Marshal.ReadIntPtr(vtable, 13 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(call(policy, id, (int)role));
        }
        finally { if (policy != IntPtr.Zero) Marshal.Release(policy); if (unknown != IntPtr.Zero) Marshal.Release(unknown); Marshal.FinalReleaseComObject(instance); }
    }
    static string? Default(MMDeviceEnumerator e, DataFlow flow, Role role)
    {
        try { using var d = e.GetDefaultAudioEndpoint(flow, role); return d.ID; } catch (COMException) { return null; }
    }
    public static List<AudioDevice> Inventory()
    {
        using var e = new MMDeviceEnumerator(); var result = new List<AudioDevice>();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            var standard = Default(e, flow, Role.Multimedia); var communications = Default(e, flow, Role.Communications); var console = Default(e, flow, Role.Console);
            foreach (var d in e.EnumerateAudioEndPoints(flow, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged))
                using (d)
                {
                    bool active = d.State == DeviceState.Active;
                    result.Add(new(d.ID, d.FriendlyName, flow == DataFlow.Capture, active, d.ID == standard, d.ID == communications, active ? d.AudioEndpointVolume.MasterVolumeLevelScalar * 100 : 0, active && d.AudioEndpointVolume.Mute, d.ID == console));
                }
        }
        return result;
    }
    static void Endpoint(EndpointSetting? setting, DataFlow flow, bool changeDefault = true)
    {
        if (setting == null) return;
        using var e = new MMDeviceEnumerator(); using var d = e.GetDevice(setting.DeviceId);
        if (d.State != DeviceState.Active || d.DataFlow != flow) throw new DeviceUnavailableException("Audiogerät ist nicht verfügbar.");
        if (changeDefault) { SetDefault(d.ID, Role.Console); SetDefault(d.ID, Role.Multimedia); }
        if (setting.Volume is float v) d.AudioEndpointVolume.MasterVolumeLevelScalar = v / 100;
        if (setting.Muted is bool m) d.AudioEndpointVolume.Mute = m;
    }
    public static void Apply(AudioFavorite f)
    {
        var devices = Inventory();
        foreach (var (id, input, role) in new[] { (f.Output?.DeviceId, false, "Ausgabegerät"), (f.Input?.DeviceId, true, "Mikrofon"), (f.CommunicationsOutput, false, "Kommunikationsausgabe"), (f.CommunicationsInput, true, "Kommunikationsmikrofon") })
            if (id != null && !devices.Any(x => x.Id == id && x.Input == input && x.Available)) throw new DeviceUnavailableException($"Audio „{f.Name}“: {role} fehlt oder ist noch nicht bereit. Prüfe die Gerätezuordnung und ob das Gerät verbunden ist.");
        Endpoint(f.Output, DataFlow.Render); Endpoint(f.Input, DataFlow.Capture);
        if (f.CommunicationsOutput != null) SetDefault(f.CommunicationsOutput, Role.Communications);
        if (f.CommunicationsInput != null) SetDefault(f.CommunicationsInput, Role.Communications);
        var actual = Inventory();
        foreach (var (s, input) in new[] { (f.Output, false), (f.Input, true) })
        {
            if (s == null) continue;
            var d = actual.Single(x => x.Id == s.DeviceId && x.Input == input);
            if (!d.Default || !d.ConsoleDefault || s.Volume is float v && Math.Abs(d.Volume - v) > 1 || s.Muted is bool m && d.Muted != m) throw new InvalidOperationException("Audioprüfung fehlgeschlagen.");
        }
        if (f.CommunicationsOutput != null && !actual.Any(x => x.Id == f.CommunicationsOutput && x.Communications) || f.CommunicationsInput != null && !actual.Any(x => x.Id == f.CommunicationsInput && x.Communications)) throw new InvalidOperationException("Kommunikationsgerät wurde nicht übernommen.");
    }
    public static void Restore(List<AudioDevice> devices)
    {
        var available = Inventory().Where(x => x.Available).Select(x => x.Id).ToHashSet();
        var errors = new List<Exception>();
        foreach (var d in devices.Where(x => x.Available))
        {
            if (!available.Contains(d.Id)) { if (d.Default || d.Communications || d.ConsoleDefault) errors.Add(new DeviceUnavailableException($"Wiederherstellung: {d.Name} fehlt.")); continue; }
            try { Endpoint(new(d.Id, d.Volume, d.Muted), d.Input ? DataFlow.Capture : DataFlow.Render, false); if (d.Default) SetDefault(d.Id, Role.Multimedia); if (d.ConsoleDefault) SetDefault(d.Id, Role.Console); if (d.Communications) SetDefault(d.Id, Role.Communications); }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException(errors);
    }
}
internal sealed class Hardware : IHardware
{
    public Inventory Inventory() => new(Displays.Inventory(), Audio.Inventory());
    public List<AudioDevice> AudioInventory() => Audio.Inventory();
    public void ValidateDisplays(List<DisplaySetting> displays) => Displays.Validate(displays);
    public void ApplyDisplays(List<DisplaySetting> displays) => Displays.Apply(displays);
    public void ValidateDisplayPositions(List<DisplaySetting> displays) => Displays.ValidatePositions(displays);
    public void ApplyDisplayPositions(List<DisplaySetting> displays) => Displays.ApplyPositions(displays);
    public void VerifyDisplays(List<DisplaySetting> displays) => Displays.Verify(displays);
    public void ApplyAudio(AudioFavorite favorite) => Audio.Apply(favorite);
    public void RestoreAudio(List<AudioDevice> devices) => Audio.Restore(devices);
}
