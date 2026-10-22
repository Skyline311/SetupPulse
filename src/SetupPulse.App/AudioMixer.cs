using Microsoft.Win32.SafeHandles;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SetupPulse.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SetupPulse.App;

// Volume uses percent; Peak is a scalar from 0 to 1. IDs remain native identifiers.
internal sealed record MixerDevice(string Id, string Name, bool Input, float Volume, bool Muted, bool Default, bool Communications, float Peak);
internal sealed record MixerSession(string Id, string DeviceId, uint ProcessId, string AppId, string Name, bool SystemSounds, float Volume, bool Muted, float Peak, string? RoutedDeviceId);
internal sealed record MixerSnapshot(List<MixerDevice> Devices, List<MixerSession> Sessions, bool RoutingAvailable, string? RoutingWarning);

internal static class AudioMixer
{
    static readonly object RoutingLock = new();
    sealed record AppIdentity(string Id, string Name);
    sealed record IdentityEntry(AppIdentity Identity, long Created, long LastUsed);
    static readonly object IdentityLock = new();
    static readonly Dictionary<(uint ProcessId, string SessionIdentifier), IdentityEntry> IdentityCache = new();
    const long IdentityLifetimeMilliseconds = 30_000;

    static AppIdentity CachedIdentity(uint pid, string sessionIdentifier)
    {
        // Visible-mixer polling needs fresh audio state, not repeated executable metadata reads.
        // Keep only strings and timestamps; session identity prevents a reused PID matching alone.
        lock (IdentityLock)
        {
            long now = Environment.TickCount64;
            var key = (pid, sessionIdentifier);
            if (IdentityCache.TryGetValue(key, out var cached) && now - cached.Created < IdentityLifetimeMilliseconds)
            {
                IdentityCache[key] = cached with { LastUsed = now };
                return cached.Identity;
            }
            IdentityCache.Remove(key);
            if (IdentityCache.Count >= 128)
                foreach (var expired in IdentityCache.Where(item => now - item.Value.Created >= IdentityLifetimeMilliseconds).Select(item => item.Key).ToArray())
                    IdentityCache.Remove(expired);
            if (IdentityCache.Count >= 256)
                IdentityCache.Remove(IdentityCache.MinBy(item => item.Value.LastUsed).Key);
            var identity = Identify(pid, sessionIdentifier);
            // Absolute expiry refreshes metadata even for continuously visible sessions.
            IdentityCache[key] = new(identity, Environment.TickCount64, Environment.TickCount64);
            return identity;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? appId);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int SHLoadIndirectString(string source, StringBuilder output, uint length, IntPtr reserved);

    static bool AudioRace(Exception ex) => ex is COMException or CoreAudioException;

    static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        try { using var endpoint = enumerator.GetDefaultAudioEndpoint(flow, role); return endpoint.ID; }
        catch (Exception ex) when (AudioRace(ex)) { return null; }
    }

    static float ReadPeak(Func<float> read)
    {
        try { return Math.Clamp(read(), 0, 1); }
        catch (Exception ex) when (AudioRace(ex)) { return 0; }
    }

    static string SessionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.StartsWith('@'))
        {
            var buffer = new StringBuilder(1024);
            if (SHLoadIndirectString(value, buffer, (uint)buffer.Capacity, IntPtr.Zero) == 0) return buffer.ToString();
            return "";
        }
        return value;
    }

    static AppIdentity Identify(uint pid, string sessionIdentifier)
    {
        string? path = null, appId = null, name = null;
        using (var process = OpenProcess(0x1000, false, pid)) // PROCESS_QUERY_LIMITED_INFORMATION
        {
            if (!process.IsInvalid)
            {
                var image = new StringBuilder(32768);
                uint size = (uint)image.Capacity;
                if (QueryFullProcessImageName(process, 0, image, ref size)) path = image.ToString();
                uint length = 0;
                if (GetApplicationUserModelId(process, ref length, null) == 122 && length is > 0 and < 32768)
                {
                    var value = new StringBuilder((int)length);
                    if (GetApplicationUserModelId(process, ref length, value) == 0) appId = value.ToString();
                }
            }
        }
        if (path != null)
        {
            try { name = FileVersionInfo.GetVersionInfo(path).FileDescription; }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            try { using var process = Process.GetProcessById(checked((int)pid)); name = process.ProcessName; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException) { }
        }
        return new(appId != null ? "app:" + appId : path != null ? "exe:" + path.ToUpperInvariant() : "session:" + sessionIdentifier,
            string.IsNullOrWhiteSpace(name) ? $"Anwendung {pid}" : name);
    }

    public static MixerSnapshot Inventory()
    {
        using var routing = AppAudioRouting.TryCreate(out var routingWarning);
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<MixerDevice>();
        var sessions = new List<MixerSession>();
        var identities = new Dictionary<(uint ProcessId, string SessionIdentifier), AppIdentity>();
        var routes = new Dictionary<uint, string?>();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            var standard = DefaultId(enumerator, flow, Role.Multimedia);
            var communications = DefaultId(enumerator, flow, Role.Communications);
            using var endpoints = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            foreach (var endpoint in endpoints)
            {
                using (endpoint)
                {
                    try
                    {
                        string endpointId = endpoint.ID;
                        var volume = endpoint.AudioEndpointVolume;
                        devices.Add(new(endpointId, endpoint.FriendlyName, flow == DataFlow.Capture,
                            volume.MasterVolumeLevelScalar * 100, volume.Mute, endpointId == standard, endpointId == communications,
                            ReadPeak(() => endpoint.AudioMeterInformation.MasterPeakValue)));
                        if (flow != DataFlow.Render) continue;
                        // MMDevice owns its manager; the manager owns the enumerator collection.
                        var collection = endpoint.AudioSessionManager.Sessions;
                        for (int i = 0; i < collection.Count; i++)
                        {
                            try
                            {
                                using var session = collection[i];
                                if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                                bool system = session.IsSystemSoundsSession;
                                uint pid = session.GetProcessID;
                                string identifier = session.GetSessionIdentifier;
                                var identityKey = (pid, identifier);
                                var identity = system ? new AppIdentity("system-sounds", "Systemsounds") :
                                    identities.TryGetValue(identityKey, out var known) ? known : identities[identityKey] = CachedIdentity(pid, identifier);
                                string name = SessionName(session.DisplayName);
                                if (string.IsNullOrWhiteSpace(name) || system) name = identity.Name;
                                string? routed = null;
                                if (!system && pid != 0 && routing != null)
                                {
                                    if (!routes.TryGetValue(pid, out routed))
                                    {
                                        try { routed = routing.Get(pid); routes[pid] = routed; }
                                        catch (COMException ex) { routingWarning ??= $"Einige App-Audioausgänge konnten nicht gelesen werden (0x{ex.HResult:X8})."; }
                                    }
                                }
                                sessions.Add(new(session.GetSessionInstanceIdentifier, endpointId, pid, identity.Id, name, system,
                                    session.SimpleAudioVolume.Volume * 100, session.SimpleAudioVolume.Mute,
                                    ReadPeak(() => session.AudioMeterInformation.MasterPeakValue), routed));
                            }
                            catch (Exception ex) when (AudioRace(ex)) { Debug.WriteLine($"Mixer session disappeared: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) when (AudioRace(ex)) { Debug.WriteLine($"Mixer endpoint disappeared: {ex.Message}"); }
                }
            }
        }
        return new(devices, sessions, routing != null, routingWarning);
    }

    static void ValidateVolume(float? volume)
    {
        if (volume is float value && (!float.IsFinite(value) || value < 0 || value > 100))
            throw new ArgumentOutOfRangeException(nameof(volume), "Die Lautstärke muss zwischen 0 und 100 liegen.");
    }

    static MMDevice ActiveEndpoint(MMDeviceEnumerator enumerator, string deviceId, bool renderOnly = false)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Bitte ein Audiogerät auswählen.", nameof(deviceId));
        var endpoint = enumerator.GetDevice(deviceId);
        try
        {
            if (endpoint.State != DeviceState.Active || renderOnly && endpoint.DataFlow != DataFlow.Render)
                throw new DeviceUnavailableException("Das Audiogerät ist nicht mehr verfügbar.");
            return endpoint;
        }
        catch { endpoint.Dispose(); throw; }
    }

    public static void SetEndpoint(string deviceId, float? volume, bool? muted, string? defaultRole)
    {
        ValidateVolume(volume);
        if (defaultRole is not (null or "standard" or "communications"))
            throw new ArgumentException("Unbekannte Rolle für das Standard-Audiogerät.", nameof(defaultRole));
        using var enumerator = new MMDeviceEnumerator();
        using var endpoint = ActiveEndpoint(enumerator, deviceId);
        if (defaultRole == "standard") { Audio.SetDefault(endpoint.ID, Role.Console); Audio.SetDefault(endpoint.ID, Role.Multimedia); }
        else if (defaultRole == "communications") Audio.SetDefault(endpoint.ID, Role.Communications);
        if (volume is float level) endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = level / 100;
        if (muted is bool mute) endpoint.AudioEndpointVolume.Mute = mute;
        if (volume is float expected && Math.Abs(endpoint.AudioEndpointVolume.MasterVolumeLevelScalar * 100 - expected) > 1 ||
            muted is bool expectedMute && endpoint.AudioEndpointVolume.Mute != expectedMute)
            throw new InvalidOperationException("Windows hat die Lautstärke oder Stummschaltung des Geräts nicht bestätigt.");
        foreach (var role in defaultRole == "standard" ? new[] { Role.Console, Role.Multimedia } : defaultRole == "communications" ? new[] { Role.Communications } : [])
            if (DefaultId(enumerator, endpoint.DataFlow, role) != endpoint.ID)
                throw new InvalidOperationException("Windows hat das Standard-Audiogerät nicht bestätigt.");
    }

    static void WithSession(string deviceId, string sessionId, uint pid, Action<AudioSessionControl> change)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Bitte eine Audiositzung auswählen.", nameof(sessionId));
        using var enumerator = new MMDeviceEnumerator();
        using var endpoint = ActiveEndpoint(enumerator, deviceId, renderOnly: true);
        var collection = endpoint.AudioSessionManager.Sessions;
        for (int i = 0; i < collection.Count; i++)
        {
            using var session = collection[i];
            if (session.GetSessionInstanceIdentifier != sessionId) continue;
            if (session.GetProcessID != pid || session.State == AudioSessionState.AudioSessionStateExpired)
                throw new InvalidOperationException("Die Audiositzung hat sich geändert. Bitte den Mixer aktualisieren und erneut versuchen.");
            change(session);
            return;
        }
        throw new InvalidOperationException("Die Audiositzung wurde beendet. Bitte den Mixer aktualisieren und erneut versuchen.");
    }

    public static void SetSession(string deviceId, string sessionId, uint processId, float? volume, bool? muted)
    {
        ValidateVolume(volume);
        WithSession(deviceId, sessionId, processId, session =>
        {
            var control = session.SimpleAudioVolume;
            if (volume is float level) control.Volume = level / 100;
            if (muted is bool mute) control.Mute = mute;
            if (volume is float expected && Math.Abs(control.Volume * 100 - expected) > 1 || muted is bool expectedMute && control.Mute != expectedMute)
                throw new InvalidOperationException("Windows hat die Lautstärke oder Stummschaltung der App nicht bestätigt.");
        });
    }

    public static void Route(string deviceId, string sessionId, uint processId, string? targetDeviceId)
    {
        if (targetDeviceId != null && string.IsNullOrWhiteSpace(targetDeviceId))
            throw new ArgumentException("Bitte einen verfügbaren Ausgang oder den Systemstandard auswählen.", nameof(targetDeviceId));
        lock (RoutingLock)
        {
            // Dispose all endpoint/session wrappers before balancing WinRT initialization.
            using var routing = AppAudioRouting.TryCreate(out var warning);
            if (routing == null) throw new InvalidOperationException(warning);
            using var enumerator = new MMDeviceEnumerator();
            // Hold a validated render endpoint through the operation; null means system default.
            using var target = targetDeviceId == null ? null : ActiveEndpoint(enumerator, targetDeviceId, renderOnly: true);
            WithSession(deviceId, sessionId, processId, session =>
            {
                if (processId == 0 || session.IsSystemSoundsSession) throw new InvalidOperationException("Systemsounds können keinem App-Ausgang zugewiesen werden.");
                routing.Set(processId, target?.ID);
            });
        }
    }
}
