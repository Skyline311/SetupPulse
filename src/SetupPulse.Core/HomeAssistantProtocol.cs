namespace SetupPulse.Core;

public record HomeAssistantOptions(bool Enabled = false, string Host = "", int Port = 1883,
    string Username = "", bool UseTls = false, string DeviceName = "SetupPulse", string InstanceId = "",
    string DiscoveryPrefix = "homeassistant");
public record HomeAssistantChoice(string Id, string Label);
public record HomeAssistantSnapshot(List<HomeAssistantChoice> Choices, string? CurrentId, bool Busy,
    string? Error = null, string? TransactionId = null, HomeAssistantAudioSnapshot? Audio = null);
public record HomeAssistantAudioSnapshot(List<HomeAssistantChoice> Outputs, List<HomeAssistantChoice> Inputs,
    List<HomeAssistantChoice> Favorites, string? OutputId, string? InputId, string? FavoriteId);

public static class HomeAssistantProtocol
{
    public static void Validate(HomeAssistantOptions options)
    {
        if (options.InstanceId.Length != 32 || !options.InstanceId.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Ungültige Home-Assistant-Gerätekennung.");
        if (string.IsNullOrWhiteSpace(options.DeviceName) || options.DeviceName.Length > 80)
            throw new InvalidDataException("Bitte einen Gerätenamen mit höchstens 80 Zeichen verwenden.");
        if (options.Port is < 1 or > 65535 || options.Username.Length > 256)
            throw new InvalidDataException("Ungültiger MQTT-Port oder Benutzername.");
        if (options.Host.Length > 253 || options.Host.Any(char.IsWhiteSpace) ||
            (options.Enabled && Uri.CheckHostName(options.Host) == UriHostNameType.Unknown))
            throw new InvalidDataException("Bitte nur den MQTT-Hostnamen oder die IP-Adresse eingeben, ohne http:// oder Port.");
        if (options.DiscoveryPrefix.Length is < 1 or > 128 || options.DiscoveryPrefix.Split('/').Any(part =>
            part.Length == 0 || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')))
            throw new InvalidDataException("Ungültiges MQTT-Discovery-Präfix.");
    }

    public static bool CanActivate(SetupProfile profile) =>
        profile.Monitors is { } monitors ? profile.VerifiedHash == Validation.MonitorHash(monitors)
        : profile.Audio != null || profile.AudioFavoriteId != null;

    public static List<HomeAssistantChoice> Choices(Settings settings)
    {
        return UniqueChoices(settings.Profiles.Where(CanActivate).Select(p => new HomeAssistantChoice(p.Id, p.Name)).ToList());
    }
    static List<HomeAssistantChoice> UniqueChoices(List<HomeAssistantChoice> items)
    {
        var used = new HashSet<string>(items.Select(p => p.Label), StringComparer.Ordinal) { "None" };
        var counts = items.GroupBy(p => p.Label).ToDictionary(g => g.Key, g => g.Count());
        return items.Select(p => {
            string label = p.Label;
            if (counts[label] > 1 || label.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                label = $"{p.Label} [{p.Id}]";
                while (!used.Add(label)) label += "_";
            }
            return new HomeAssistantChoice(p.Id, label);
        }).ToList();
    }

    public static HomeAssistantSnapshot Snapshot(Settings settings, Inventory inventory, bool busy,
        Transaction? transaction = null, string? preferredId = null)
    {
        var choices = Choices(settings);
        bool Matches(SetupProfile profile) => choices.Any(c => c.Id == profile.Id) && ProfileMatching.Matches(profile, settings, inventory);
        var matching = settings.Profiles.FirstOrDefault(p => p.Id == preferredId && Matches(p)) ?? settings.Profiles.FirstOrDefault(Matches);
        string? error = transaction?.Phase is "failed" or "rolledBack" ? transaction.Error : null;
        return new(choices, busy || transaction?.Phase == "pending" ? null : matching?.Id,
            busy || transaction?.Phase == "pending", error, transaction?.Id, AudioSnapshot(settings, inventory.Audio));
    }

    public static HomeAssistantAudioSnapshot AudioSnapshot(Settings settings, List<AudioDevice> devices)
    {
        List<HomeAssistantChoice> Endpoints(bool input) => UniqueChoices(devices.Where(d => d.Available && d.Input == input)
            .Select(d => new HomeAssistantChoice(d.Id, d.Name)).ToList());
        var favorites = settings.AudioFavorites.Where(f => AudioMatching.Available(f, devices)).ToList();
        return new(Endpoints(false), Endpoints(true), UniqueChoices(favorites.Select(f => new HomeAssistantChoice(f.Id, f.Name)).ToList()),
            devices.FirstOrDefault(d => d.Available && !d.Input && d.Default)?.Id,
            devices.FirstOrDefault(d => d.Available && d.Input && d.Default)?.Id,
            favorites.FirstOrDefault(f => AudioMatching.Matches(f, devices))?.Id);
    }

    public static string ResolveAudioCommand(HomeAssistantSnapshot snapshot, string kind, string payload, bool retained)
    {
        var audio = snapshot.Audio;
        var choices = kind switch {
            "output" => audio?.Outputs, "input" => audio?.Inputs, "favorite" => audio?.Favorites,
            _ => throw new InvalidDataException("Unbekannter Audio-Befehl.")
        };
        return ResolveChoice(choices ?? [], snapshot.Busy, payload, retained,
            "Dieses Audiogerät oder dieser Audiofavorit ist nicht verfügbar.");
    }

    // Recheck live inventory on the UI thread before entering the existing audio-only transaction path.
    public static AudioFavorite AudioSelection(Settings settings, List<AudioDevice> devices, string kind, string id)
    {
        if (kind == "favorite")
            return settings.AudioFavorites.SingleOrDefault(f => f.Id == id && AudioMatching.Available(f, devices))
                ?? throw new DeviceUnavailableException("Dieser Audiofavorit ist nicht verfügbar.");
        if (kind is not ("output" or "input")) throw new InvalidDataException("Unbekannter Audio-Befehl.");
        var device = devices.SingleOrDefault(d => d.Id == id && d.Input == (kind == "input") && d.Available)
            ?? throw new DeviceUnavailableException("Dieses Audiogerät ist nicht verfügbar.");
        return new("ha-audio-" + kind, device.Name,
            Output: kind == "output" ? new(device.Id) : null, Input: kind == "input" ? new(device.Id) : null);
    }

    public static string ResolveCommand(HomeAssistantSnapshot snapshot, string payload, bool retained)
        => ResolveChoice(snapshot.Choices, snapshot.Busy, payload, retained,
            "Dieses Setup fehlt oder muss zuerst am PC getestet und bestätigt werden.");

    static string ResolveChoice(List<HomeAssistantChoice> choices, bool busy, string payload, bool retained, string missing)
    {
        if (retained) throw new InvalidDataException("Gespeicherte MQTT-Befehle werden nicht ausgeführt.");
        if (busy) throw new InvalidOperationException("Ein Wechsel läuft bereits.");
        if (payload.Length is < 1 or > 1024) throw new InvalidDataException("Ungültiger Auswahl-Befehl.");
        return choices.SingleOrDefault(c => c.Label == payload)?.Id ?? throw new InvalidDataException(missing);
    }

    public static string Topic(HomeAssistantOptions options) => "setuppulse/" + options.InstanceId;
    public static Dictionary<string, string> Discovery(HomeAssistantOptions options, HomeAssistantSnapshot snapshot, string version)
    {
        string root = Topic(options), id = "setuppulse_" + options.InstanceId;
        var device = new { identifiers = new[] { id }, name = options.DeviceName, manufacturer = "Skyline311", model = "SetupPulse", sw_version = version };
        var origin = new { name = "SetupPulse", sw_version = version, support_url = "https://github.com/Skyline311/SetupPulse" };
        Dictionary<string, object> Common(string suffix, string name) => new() {
            ["unique_id"] = id + "_" + suffix, ["name"] = name, ["device"] = device, ["origin"] = origin,
            ["availability_topic"] = root + "/availability", ["state_topic"] = root + "/state"
        };
        var select = Common("setup", "Setup");
        select["command_topic"] = root + "/setup/set";
        select["options"] = snapshot.Choices.Select(c => c.Label).ToList();
        select["value_template"] = "{{ value_json.profile }}";
        select["retain"] = false; select["qos"] = 0; select["optimistic"] = false; select["icon"] = "mdi:monitor-multiple";
        var status = Common("status", "Wechselstatus");
        status["value_template"] = "{{ value_json.status }}"; status["entity_category"] = "diagnostic";
        var error = Common("error", "Letzter Fehler");
        error["value_template"] = "{{ value_json.error[:255] if value_json.error else 'Kein Fehler' }}"; error["entity_category"] = "diagnostic";
        var discovery = new Dictionary<string, string> {
            [$"{options.DiscoveryPrefix}/select/{id}/setup/config"] = Json.Encode(select),
            [$"{options.DiscoveryPrefix}/sensor/{id}/status/config"] = Json.Encode(status),
            [$"{options.DiscoveryPrefix}/sensor/{id}/error/config"] = Json.Encode(error)
        };
        var audio = snapshot.Audio ?? new([], [], [], null, null, null);
        foreach (var (kind, name, icon, choices) in new[] {
            ("output", "Audioausgabe", "mdi:speaker", audio.Outputs),
            ("input", "Mikrofon", "mdi:microphone", audio.Inputs),
            ("favorite", "Audiofavorit", "mdi:star", audio.Favorites)
        })
        {
            var audioSelect = Common("audio_" + kind, name);
            audioSelect["command_topic"] = root + "/audio/" + kind + "/set";
            audioSelect["options"] = choices.Select(c => c.Label).ToList();
            audioSelect["value_template"] = "{{ value_json.audio_" + kind + " }}";
            audioSelect["retain"] = false; audioSelect["qos"] = 0; audioSelect["optimistic"] = false; audioSelect["icon"] = icon;
            discovery[$"{options.DiscoveryPrefix}/select/{id}/audio_{kind}/config"] = Json.Encode(audioSelect);
        }
        return discovery;
    }

    public static string State(HomeAssistantSnapshot snapshot, string? commandError = null)
    {
        string? error = commandError ?? snapshot.Error;
        string AudioLabel(List<HomeAssistantChoice>? choices, string? id) => snapshot.Busy ? "None"
            : choices?.FirstOrDefault(c => c.Id == id)?.Label ?? "None";
        return Json.Encode(new {
            profile = snapshot.Choices.FirstOrDefault(c => c.Id == snapshot.CurrentId)?.Label ?? "None",
            audio_output = AudioLabel(snapshot.Audio?.Outputs, snapshot.Audio?.OutputId),
            audio_input = AudioLabel(snapshot.Audio?.Inputs, snapshot.Audio?.InputId),
            audio_favorite = AudioLabel(snapshot.Audio?.Favorites, snapshot.Audio?.FavoriteId),
            status = snapshot.Busy ? "switching" : !string.IsNullOrEmpty(error) ? "error" : "ready",
            error = error ?? ""
        });
    }
}
