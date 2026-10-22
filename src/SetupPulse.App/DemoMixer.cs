using SetupPulse.Core;

namespace SetupPulse.App;

// Isolated simulated mixer. It cannot be constructed with the real hardware adapter.
internal sealed class DemoMixer
{
    readonly IHardware hardware;
    readonly object gate = new();
    List<MixerSession> sessions =
    [
        new("demo-chromium-tab", "demo-speakers", 101, "demo:chromium", "Chromium", false, 65, false, .36f, null),
        new("demo-chromium-audio", "demo-speakers", 102, "demo:chromium", "Chromium", false, 65, false, .22f, null),
        new("demo-music", "demo-headset", 201, "demo:music", "Musik", false, 40, false, .48f, "demo-headset"),
        new("demo-system", "demo-speakers", 0, "system-sounds", "Systemsounds", true, 80, false, 0, null),
    ];

    public DemoMixer(IHardware hardware)
    {
        if (hardware is not DemoHardware) throw new ArgumentException("Der Demomixer benötigt simulierte Hardware.", nameof(hardware));
        this.hardware = hardware;
    }

    static void ValidateVolume(float? volume)
    {
        if (volume is float value && (!float.IsFinite(value) || value < 0 || value > 100))
            throw new ArgumentOutOfRangeException(nameof(volume), "Die Lautstärke muss zwischen 0 und 100 liegen.");
    }

    static AudioDevice Device(List<AudioDevice> audio, string deviceId, bool renderOnly = false)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Bitte ein Audiogerät auswählen.", nameof(deviceId));
        return audio.FirstOrDefault(d => d.Id == deviceId && d.Available && (!renderOnly || !d.Input))
            ?? throw new DeviceUnavailableException("Das Audiogerät ist nicht mehr verfügbar.");
    }

    static string EffectiveDevice(MixerSession session, List<AudioDevice> audio) => session.RoutedDeviceId ??
        audio.FirstOrDefault(d => d.Available && !d.Input && d.Default)?.Id ?? session.DeviceId;

    MixerSession Session(List<AudioDevice> audio, string deviceId, string sessionId, uint processId)
    {
        Device(audio, deviceId, renderOnly: true);
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Bitte eine Audiositzung auswählen.", nameof(sessionId));
        var session = sessions.FirstOrDefault(s => s.Id == sessionId && EffectiveDevice(s, audio) == deviceId)
            ?? throw new InvalidOperationException("Die Audiositzung wurde beendet oder verschoben. Bitte den Mixer aktualisieren und erneut versuchen.");
        if (session.ProcessId != processId)
            throw new InvalidOperationException("Die Audiositzung hat sich geändert. Bitte den Mixer aktualisieren und erneut versuchen.");
        return session;
    }

    public MixerSnapshot Inventory()
    {
        lock (gate)
        {
            var audio = hardware.AudioInventory();
            var available = audio.Where(d => d.Available && !d.Input).Select(d => d.Id).ToHashSet();
            var visible = sessions.Select(s => s with { DeviceId = EffectiveDevice(s, audio) }).Where(s => available.Contains(s.DeviceId)).ToList();
            var devices = audio.Where(d => d.Available).Select(d => new MixerDevice(d.Id, d.Name, d.Input, d.Volume, d.Muted, d.Default, d.Communications,
                d.Muted ? 0 : visible.Where(s => s.DeviceId == d.Id && !s.Muted).Select(s => s.Peak).DefaultIfEmpty(0).Max())).ToList();
            return new(devices, visible, true, null);
        }
    }

    public void SetEndpoint(string deviceId, float? volume, bool? muted, string? defaultRole)
    {
        ValidateVolume(volume);
        if (defaultRole is not (null or "standard" or "communications"))
            throw new ArgumentException("Unbekannte Rolle für das Standard-Audiogerät.", nameof(defaultRole));
        lock (gate)
        {
            var audio = hardware.AudioInventory();
            var selected = Device(audio, deviceId);
            hardware.RestoreAudio(audio.Select(d => d with
            {
                Volume = d.Id == deviceId ? volume ?? d.Volume : d.Volume,
                Muted = d.Id == deviceId ? muted ?? d.Muted : d.Muted,
                Default = defaultRole == "standard" && d.Input == selected.Input ? d.Id == deviceId : d.Default,
                ConsoleDefault = defaultRole == "standard" && d.Input == selected.Input ? d.Id == deviceId : d.ConsoleDefault,
                Communications = defaultRole == "communications" && d.Input == selected.Input ? d.Id == deviceId : d.Communications,
            }).ToList());
        }
    }

    public void SetSession(string deviceId, string sessionId, uint processId, float? volume, bool? muted)
    {
        ValidateVolume(volume);
        lock (gate)
        {
            var session = Session(hardware.AudioInventory(), deviceId, sessionId, processId);
            sessions[sessions.IndexOf(session)] = session with { Volume = volume ?? session.Volume, Muted = muted ?? session.Muted };
        }
    }

    public void Route(string deviceId, string sessionId, uint processId, string? targetDeviceId)
    {
        if (targetDeviceId != null && string.IsNullOrWhiteSpace(targetDeviceId))
            throw new ArgumentException("Bitte einen verfügbaren Ausgang oder den Systemstandard auswählen.", nameof(targetDeviceId));
        lock (gate)
        {
            var audio = hardware.AudioInventory();
            if (targetDeviceId != null) Device(audio, targetDeviceId, renderOnly: true);
            var session = Session(audio, deviceId, sessionId, processId);
            if (session.SystemSounds || processId == 0)
                throw new InvalidOperationException("Systemsounds können keinem App-Ausgang zugewiesen werden.");
            var destination = targetDeviceId ?? audio.FirstOrDefault(d => d.Available && !d.Input && d.Default)?.Id ?? deviceId;
            // Windows routing is per process, even if that process owns several sessions.
            sessions = sessions.Select(s => s.ProcessId == processId ? s with { DeviceId = destination, RoutedDeviceId = targetDeviceId } : s).ToList();
        }
    }
}
