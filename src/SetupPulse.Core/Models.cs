using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SetupPulse.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true, IncludeFields = true };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("Leere JSON-Datei.");
}
public record DisplaySetting(string DeviceId, string Name, int Width, int Height, int X, int Y, int Rotation, double RefreshRate, int Scale, bool Primary);
public record DisplayMode(int Width, int Height, double RefreshRate, bool Recommended = false);
public record DisplayDevice(string Id, string Name, bool Connected, bool Active, DisplaySetting? Current, List<DisplayMode>? Modes = null);
public record AudioDevice(string Id, string Name, bool Input, bool Available, bool Default, bool Communications, float Volume, bool Muted, bool ConsoleDefault = false);
public record EndpointSetting(string DeviceId, float? Volume = null, bool? Muted = null);
public record AudioFavorite(string Id, string Name, EndpointSetting? Output = null, EndpointSetting? Input = null, string? CommunicationsOutput = null, string? CommunicationsInput = null, string? Shortcut = null);
public record ProfileAudio(EndpointSetting? Output = null, EndpointSetting? Input = null, string? Name = null);
public record SetupProfile(string Id, string Name, List<DisplaySetting>? Monitors = null, string? AudioFavoriteId = null, string? Shortcut = null, string? VerifiedHash = null, ProfileAudio? Audio = null);
public record Preferences(string Theme = "system", bool StartWithWindows = false, bool CloseToTray = true, string? NextAudioShortcut = null, string? PreviousAudioShortcut = null, string? RescueShortcut = null, string? MixerShortcut = null, string? QuickMixerShortcut = null, string? SettingsShortcut = null, string? VolumeUpShortcut = null, string? VolumeDownShortcut = null, bool TrayScrollVolume = false, bool LogarithmicVolume = false, bool StartMinimized = true);
public record Settings(int Version, List<SetupProfile> Profiles, List<AudioFavorite> AudioFavorites, Preferences Preferences)
{
    public static Settings Empty => new(1, [], [], new());
}
public record Inventory(List<DisplayDevice> Displays, List<AudioDevice> Audio);
public record Transaction(string Id, string Phase, DateTimeOffset Deadline, List<DisplaySetting>? Displays, List<AudioDevice> Audio, string? ProfileId, string? Hash, string? Error = null, List<DisplaySetting>? RequestedDisplays = null, bool LiveAlignment = false);

public static class Validation
{
    public static string MonitorHash(List<DisplaySetting> displays) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Encode(displays.OrderBy(x => x.DeviceId, StringComparer.OrdinalIgnoreCase)))));
    public static void Displays(List<DisplaySetting> items)
    {
        if (items.Count is < 1 or > 16) throw new InvalidDataException("Mindestens ein Monitor muss aktiv sein.");
        if (items.Select(x => x.DeviceId.ToUpperInvariant()).Distinct().Count() != items.Count) throw new InvalidDataException("Ein Monitor ist mehrfach eingetragen.");
        if (items.Count(x => x.Primary) != 1 || items.Single(x => x.Primary).X != 0 || items.Single(x => x.Primary).Y != 0) throw new InvalidDataException("Genau ein Hauptmonitor muss bei 0 / 0 liegen.");
        foreach (var d in items)
            if (string.IsNullOrWhiteSpace(d.DeviceId) || d.Width is < 320 or > 16384 || d.Height is < 200 or > 16384 || Math.Abs((long)d.X) > 32768 || Math.Abs((long)d.Y) > 32768 || d.Rotation is < 1 or > 4 || d.RefreshRate is < 20 or > 1000 || d.Scale is < 100 or > 500)
                throw new InvalidDataException("Ungültige Monitoreinstellung.");
        for (int i = 0; i < items.Count; i++) for (int j = i + 1; j < items.Count; j++)
        {
            var a = items[i]; var b = items[j];
            if (Math.Max(a.X, b.X) < Math.Min(a.X + a.Width, b.X + b.Width) && Math.Max(a.Y, b.Y) < Math.Min(a.Y + a.Height, b.Y + b.Height)) throw new InvalidDataException("Monitore dürfen sich nicht überlappen.");
        }
        var reached = new HashSet<int> { items.FindIndex(x => x.Primary) };
        bool Edge(DisplaySetting a, DisplaySetting b) => ((a.X + a.Width == b.X || b.X + b.Width == a.X) && Math.Max(a.Y, b.Y) < Math.Min(a.Y + a.Height, b.Y + b.Height)) || ((a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && Math.Max(a.X, b.X) < Math.Min(a.X + a.Width, b.X + b.Width));
        for (int pass = 0; pass < items.Count; pass++) for (int i = 0; i < items.Count; i++) if (reached.Any(j => Edge(items[i], items[j]))) reached.Add(i);
        if (reached.Count != items.Count) throw new InvalidDataException("Monitore müssen an einer gemeinsamen Kante verbunden sein.");
    }
    public static void Settings(Settings settings)
    {
        if (settings.Version != 1 || settings.Profiles.Count > 100 || settings.AudioFavorites.Count > 100) throw new InvalidDataException("Nicht unterstütztes Dateiformat.");
        if (settings.Profiles.Select(x => x.Id).Distinct().Count() != settings.Profiles.Count || settings.AudioFavorites.Select(x => x.Id).Distinct().Count() != settings.AudioFavorites.Count) throw new InvalidDataException("Doppelte Kennungen.");
        foreach (var p in settings.Profiles)
        {
            Name(p.Name);
            if (p.Monitors != null) Displays(p.Monitors);
            if (p.AudioFavoriteId != null && !settings.AudioFavorites.Any(a => a.Id == p.AudioFavoriteId)) throw new InvalidDataException("Verknüpfter Audiofavorit fehlt.");
            if (p.Audio is { } audio)
            {
                if (p.AudioFavoriteId != null) throw new InvalidDataException("Direkte Audioauswahl und Audiofavorit können nicht gleichzeitig verknüpft sein.");
                if (audio.Output == null && audio.Input == null) throw new InvalidDataException("Bitte mindestens ein Audiogerät auswählen.");
                if (audio.Name != null && audio.Name.Length > 80) throw new InvalidDataException("Der Audio-Anzeigename darf höchstens 80 Zeichen haben.");
                Endpoints(audio.Output, audio.Input);
            }
        }
        foreach (var f in settings.AudioFavorites)
        {
            Name(f.Name);
            if (f.Output == null && f.Input == null && f.CommunicationsOutput == null && f.CommunicationsInput == null) throw new InvalidDataException("Ein Audiofavorit benötigt mindestens ein Gerät.");
            Endpoints(f.Output, f.Input);
        }
        var keys = settings.Profiles.Select(x => x.Shortcut).Concat(settings.AudioFavorites.Select(x => x.Shortcut)).Concat([settings.Preferences.NextAudioShortcut, settings.Preferences.PreviousAudioShortcut, settings.Preferences.RescueShortcut, settings.Preferences.MixerShortcut, settings.Preferences.QuickMixerShortcut, settings.Preferences.SettingsShortcut, settings.Preferences.VolumeUpShortcut, settings.Preferences.VolumeDownShortcut]).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Shortcut.Normalize(x!)).ToList();
        if (keys.Distinct().Count() != keys.Count) throw new InvalidDataException("Ein Shortcut ist mehrfach vergeben.");
        if (settings.Preferences.Theme is not ("dark" or "light" or "system")) throw new InvalidDataException("Unbekanntes Farbschema.");
    }
    static void Endpoints(params EndpointSetting?[] endpoints)
    {
        foreach (var e in endpoints)
        {
            if (e != null && string.IsNullOrWhiteSpace(e.DeviceId)) throw new InvalidDataException("Ein Audiogerät benötigt eine Kennung.");
            if (e?.Volume is float volume && (!float.IsFinite(volume) || volume < 0 || volume > 100)) throw new InvalidDataException("Lautstärke muss zwischen 0 und 100 liegen.");
        }
    }
    static void Name(string name) { if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidDataException("Bitte einen Namen mit höchstens 80 Zeichen verwenden."); }
}
public static class Shortcut
{
    public static (uint Modifiers, uint Key) Parse(string value)
    {
        uint mods = 0, key = 0;
        foreach (var part in value.ToUpperInvariant().Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part is "CTRL" or "STRG") mods |= 2;
            else if (part == "ALT") mods |= 1;
            else if (part is "SHIFT" or "UMSCHALT") mods |= 4;
            else if (part is "WIN" or "WINDOWS") mods |= 8;
            else
            {
                if (key != 0) throw new InvalidDataException("Ungültiger Shortcut.");
                string num = part.StartsWith("NUMPAD", StringComparison.Ordinal) ? part[6..] : part.StartsWith("NUM", StringComparison.Ordinal) ? part[3..] : "";
                key = num.Length == 1 && char.IsAsciiDigit(num[0]) ? (uint)(0x60 + num[0] - '0')
                    : part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]) ? part[0]
                    : part.StartsWith('F') && int.TryParse(part[1..], out var n) && n is >= 1 and <= 24 ? (uint)(111 + n)
                    : throw new InvalidDataException("Shortcut: Buchstabe, Ziffer, Num0–Num9 oder F1–F24 verwenden.");
            }
        }
        if (key == 0 || mods == 0) throw new InvalidDataException("Shortcut benötigt eine Kombination mit Strg, Alt, Umschalt oder Win.");
        return (mods, key);
    }
    public static string Normalize(string value) { var (m, k) = Parse(value); return $"{m}:{k}"; }
}
