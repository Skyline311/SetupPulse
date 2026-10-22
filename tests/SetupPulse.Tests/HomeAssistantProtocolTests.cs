using System.Text.Json;
using SetupPulse.Core;

namespace SetupPulse.Tests;

public sealed class HomeAssistantProtocolTests
{
    static readonly DisplaySetting Screen = new("screen", "Screen", 1920, 1080, 0, 0, 1, 60, 100, true);
    static readonly HomeAssistantOptions Options = new(true, "localhost", InstanceId: new string('a', 32));
    static SetupProfile Verified(string id = "tv", string name = "TV") => new(id, name, [Screen], VerifiedHash: Validation.MonitorHash([Screen]));
    static Settings Saved(params SetupProfile[] profiles) => Settings.Empty with { Profiles = profiles.ToList() };

    [Fact] public void OnlyConfirmedMonitorProfilesAndNonemptyAudioProfilesAreExposed()
    {
        var verified = Verified();
        var settings = Saved(verified, verified with { Id = "changed", Monitors = [Screen with { Scale = 125 }] },
            new("untested", "Untested", [Screen]), new("audio", "Headset", Audio: new(new("headset"))), new("empty", "Empty"));
        Assert.Equal(new[] { "tv", "audio" }, HomeAssistantProtocol.Choices(settings).Select(c => c.Id));
    }
    [Fact] public void DuplicateReservedAndCollidingNamesRemainUnambiguous()
    {
        var choices = HomeAssistantProtocol.Choices(Saved(Verified("a"), Verified("b"), Verified("c", "None"), Verified("d", "TV [a]")));
        Assert.Equal(4, choices.Select(c => c.Label).Distinct().Count());
        Assert.DoesNotContain(choices, c => c.Label == "None");
        foreach (var choice in choices) Assert.Equal(choice.Id, HomeAssistantProtocol.ResolveCommand(new(choices, null, false), choice.Label, false));
    }
    [Fact] public void RetainedUnknownBusyAndOversizedCommandsAreRejected()
    {
        var snapshot = new HomeAssistantSnapshot(HomeAssistantProtocol.Choices(Saved(Verified())), null, false);
        Assert.Throws<InvalidDataException>(() => HomeAssistantProtocol.ResolveCommand(snapshot, "TV", true));
        Assert.Throws<InvalidDataException>(() => HomeAssistantProtocol.ResolveCommand(snapshot, "--exit", false));
        Assert.Throws<InvalidDataException>(() => HomeAssistantProtocol.ResolveCommand(snapshot, new string('x', 1025), false));
        Assert.Throws<InvalidOperationException>(() => HomeAssistantProtocol.ResolveCommand(snapshot with { Busy = true }, "TV", false));
    }
    [Theory]
    [InlineData("None")]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("nOnE")]
    public void ReservedSetupNamesRemainSelectableWithEveryLetterCase(string name)
    {
        var settings = Saved(Verified("reserved", name), Verified("collision", name + " [reserved]"));
        var inventory = new Inventory([new("screen", "Screen", true, true, Screen)], []);
        var snapshot = HomeAssistantProtocol.Snapshot(settings, inventory, false, preferredId: "reserved");
        Assert.Equal(2, snapshot.Choices.Select(c => c.Label).Distinct().Count());
        var selected = snapshot.Choices.Single(c => c.Id == "reserved");
        Assert.False(selected.Label.Equals("none", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("reserved", HomeAssistantProtocol.ResolveCommand(snapshot, selected.Label, false));
        using var state = JsonDocument.Parse(HomeAssistantProtocol.State(snapshot));
        Assert.Equal(selected.Label, state.RootElement.GetProperty("profile").GetString());
    }
    [Fact] public void StateReflectsActualHardwareAndUsesUnknownDuringTransactionsOrExternalChanges()
    {
        var settings = Saved(Verified());
        var inventory = new Inventory([new("screen", "Screen", true, true, Screen)], []);
        Assert.Equal("tv", HomeAssistantProtocol.Snapshot(settings, inventory, false).CurrentId);
        Assert.Null(HomeAssistantProtocol.Snapshot(settings, inventory, true).CurrentId);
        Assert.Null(HomeAssistantProtocol.Snapshot(settings, new([], []), false).CurrentId);
        var transaction = new Transaction("t", "rolledBack", DateTimeOffset.Now, null, [], "tv", null, Error: "Test error");
        using var json = JsonDocument.Parse(HomeAssistantProtocol.State(HomeAssistantProtocol.Snapshot(settings, inventory, false, transaction)));
        Assert.Equal("error", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Test error", json.RootElement.GetProperty("error").GetString());
        using var unknown = JsonDocument.Parse(HomeAssistantProtocol.State(new([], null, false)));
        Assert.Equal("None", unknown.RootElement.GetProperty("profile").GetString());
    }
    [Fact] public void DiscoveryUsesStableDeviceIdCommandStateAndAvailabilityWithoutCredentials()
    {
        var snapshot = new HomeAssistantSnapshot(HomeAssistantProtocol.Choices(Saved(Verified())), "tv", false);
        var discovery = HomeAssistantProtocol.Discovery(Options, snapshot, "0.3.0");
        Assert.Equal(6, discovery.Count);
        using var select = JsonDocument.Parse(discovery.Single(kv => kv.Key.EndsWith("/setup/config")).Value);
        Assert.False(select.RootElement.GetProperty("retain").GetBoolean());
        Assert.False(select.RootElement.GetProperty("optimistic").GetBoolean());
        Assert.EndsWith("/setup/set", select.RootElement.GetProperty("command_topic").GetString());
        Assert.EndsWith("/availability", select.RootElement.GetProperty("availability_topic").GetString());
        Assert.Equal("TV", select.RootElement.GetProperty("options")[0].GetString());
        Assert.DoesNotContain("localhost", string.Join("", discovery.Values));
        Assert.DoesNotContain("username", string.Join("", discovery.Values));
    }
    [Fact] public void MonitorSetupStaysSelectedWhenAudioChangesAndActualAudioStateIsIndependent()
    {
        var desk = Verified("desk", "Büro") with { AudioFavoriteId = "speakers-favorite" };
        var speakers = new AudioFavorite("speakers-favorite", "PC Lautsprecher", new("speakers"));
        var headset = new AudioFavorite("headset-favorite", "Headset", new("headset"));
        var settings = Saved(desk) with { AudioFavorites = [speakers, headset] };
        var before = new Inventory([new("screen", "Screen", true, true, Screen)], Devices);
        var after = before with { Audio = Devices.Select(d => !d.Input ? d with { Default = d.Id == "headset" } : d).ToList() };
        Assert.Equal("desk", HomeAssistantProtocol.Snapshot(settings, before, false).CurrentId);
        var snapshot = HomeAssistantProtocol.Snapshot(settings, after, false, preferredId: "desk");
        Assert.Equal("desk", snapshot.CurrentId);
        using var state = JsonDocument.Parse(HomeAssistantProtocol.State(snapshot));
        Assert.Equal("Büro", state.RootElement.GetProperty("profile").GetString());
        Assert.Equal("Headset", state.RootElement.GetProperty("audio_output").GetString());
        Assert.Equal("Headset", state.RootElement.GetProperty("audio_favorite").GetString());
        Assert.Same(speakers, ProfileAudioSelection.Resolve(desk, settings));
        var changedLayout = after with { Displays = [] };
        Assert.Null(HomeAssistantProtocol.Snapshot(settings, changedLayout, false).CurrentId);
        Assert.Null(HomeAssistantProtocol.Snapshot(settings, after, true).CurrentId);
    }

    static List<AudioDevice> Devices => [
        new("speakers", "Speakers", false, true, true, true, 30, false),
        new("headset", "Headset", false, true, false, false, 70, true),
        new("mic", "Mic", true, true, true, true, 50, false),
        new("missing", "Missing", false, false, false, false, 0, false)
    ];
    static Settings AudioSettings => Settings.Empty with { AudioFavorites = [
        new("desk", "Desk", new("speakers"), new("mic")),
        new("gone", "Gone", new("missing"))
    ] };

    [Fact] public void AudioChoicesUseAvailableEndpointsAndFavoritesWithActualDefaults()
    {
        var snapshot = HomeAssistantProtocol.Snapshot(AudioSettings, new([], Devices), false);
        Assert.Equal(new[] { "speakers", "headset" }, snapshot.Audio!.Outputs.Select(c => c.Id));
        Assert.Equal("mic", Assert.Single(snapshot.Audio.Inputs).Id);
        Assert.Equal("desk", Assert.Single(snapshot.Audio.Favorites).Id);
        using var state = JsonDocument.Parse(HomeAssistantProtocol.State(snapshot));
        Assert.Equal("Speakers", state.RootElement.GetProperty("audio_output").GetString());
        Assert.Equal("Mic", state.RootElement.GetProperty("audio_input").GetString());
        Assert.Equal("Desk", state.RootElement.GetProperty("audio_favorite").GetString());
        using var busy = JsonDocument.Parse(HomeAssistantProtocol.State(snapshot with { Busy = true }));
        Assert.Equal("None", busy.RootElement.GetProperty("audio_output").GetString());
        var changed = Devices.Select(d => d with { Default = d.Id == "headset" }).ToList();
        Assert.Null(HomeAssistantProtocol.AudioSnapshot(AudioSettings, changed).FavoriteId);
    }

    [Fact] public void DirectAudioCommandsPreserveVolumeMuteOtherDirectionAndCommunications()
    {
        var output = HomeAssistantProtocol.AudioSelection(AudioSettings, Devices, "output", "headset");
        Assert.Equal(new EndpointSetting("headset"), output.Output);
        Assert.Null(output.Input); Assert.Null(output.CommunicationsOutput); Assert.Null(output.CommunicationsInput);
        var input = HomeAssistantProtocol.AudioSelection(AudioSettings, Devices, "input", "mic");
        Assert.Equal(new EndpointSetting("mic"), input.Input); Assert.Null(input.Output);
        Assert.Equal(AudioSettings.AudioFavorites[0], HomeAssistantProtocol.AudioSelection(AudioSettings, Devices, "favorite", "desk"));
        Assert.Throws<DeviceUnavailableException>(() => HomeAssistantProtocol.AudioSelection(AudioSettings, Devices, "output", "mic"));
        Assert.Throws<DeviceUnavailableException>(() => HomeAssistantProtocol.AudioSelection(AudioSettings, Devices, "output", "missing"));
        Assert.Throws<DeviceUnavailableException>(() => HomeAssistantProtocol.AudioSelection(AudioSettings, Devices, "favorite", "gone"));
        Assert.Throws<InvalidDataException>(() => HomeAssistantProtocol.AudioSelection(AudioSettings, Devices, "shell", "headset"));
    }

    [Fact] public void AudioCommandsValidateScopeAvailabilityRetainAndBusyAndDiscoveryUsesOwnTopics()
    {
        var snapshot = HomeAssistantProtocol.Snapshot(AudioSettings, new([], Devices), false);
        Assert.Equal("headset", HomeAssistantProtocol.ResolveAudioCommand(snapshot, "output", "Headset", false));
        Assert.Throws<InvalidDataException>(() => HomeAssistantProtocol.ResolveAudioCommand(snapshot, "input", "Headset", false));
        Assert.Throws<InvalidDataException>(() => HomeAssistantProtocol.ResolveAudioCommand(snapshot, "output", "Missing", false));
        Assert.Throws<InvalidDataException>(() => HomeAssistantProtocol.ResolveAudioCommand(snapshot, "favorite", "Desk", true));
        Assert.Throws<InvalidDataException>(() => HomeAssistantProtocol.ResolveAudioCommand(snapshot, "input", new string('x', 1025), false));
        Assert.Throws<InvalidOperationException>(() => HomeAssistantProtocol.ResolveAudioCommand(snapshot with { Busy = true }, "output", "Headset", false));
        var discovery = HomeAssistantProtocol.Discovery(Options, snapshot, "test");
        foreach (var kind in new[] { "output", "input", "favorite" })
        {
            using var config = JsonDocument.Parse(discovery.Single(kv => kv.Key.EndsWith("/audio_" + kind + "/config")).Value);
            Assert.EndsWith("/audio/" + kind + "/set", config.RootElement.GetProperty("command_topic").GetString());
            Assert.False(config.RootElement.GetProperty("retain").GetBoolean());
            Assert.False(config.RootElement.GetProperty("optimistic").GetBoolean());
            Assert.Equal("{{ value_json.audio_" + kind + " }}", config.RootElement.GetProperty("value_template").GetString());
        }
    }

    [Fact] public void DuplicateReservedAudioNamesRemainDistinctAndCommandsResolveTheirIds()
    {
        var devices = Devices.Select(d => d with { Name = d.Id == "speakers" || d.Id == "headset" ? "Same" : "None" }).ToList();
        var snapshot = HomeAssistantProtocol.Snapshot(AudioSettings, new([], devices), false);
        Assert.Equal(2, snapshot.Audio!.Outputs.Select(c => c.Label).Distinct().Count());
        Assert.DoesNotContain(snapshot.Audio.Inputs, c => c.Label == "None");
        foreach (var c in snapshot.Audio.Outputs)
            Assert.Equal(c.Id, HomeAssistantProtocol.ResolveAudioCommand(snapshot, "output", c.Label, false));
    }
    [Theory]
    [InlineData("None")]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("nOnE")]
    public void ReservedDeviceAndFavoriteNamesRemainSelectableWithEveryLetterCase(string name)
    {
        var devices = Devices.Select(d => d with { Name = name }).ToList();
        var settings = AudioSettings with { AudioFavorites = AudioSettings.AudioFavorites.Select(f => f with { Name = name }).ToList() };
        var snapshot = HomeAssistantProtocol.Snapshot(settings, new([], devices), false);
        foreach (var (kind, choices) in new[] { ("output", snapshot.Audio!.Outputs), ("input", snapshot.Audio.Inputs), ("favorite", snapshot.Audio.Favorites) })
        {
            Assert.NotEmpty(choices);
            Assert.Equal(choices.Count, choices.Select(c => c.Label).Distinct().Count());
            foreach (var choice in choices)
            {
                Assert.False(choice.Label.Equals("none", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(choice.Id, HomeAssistantProtocol.ResolveAudioCommand(snapshot, kind, choice.Label, false));
            }
        }
        using var state = JsonDocument.Parse(HomeAssistantProtocol.State(snapshot));
        foreach (string key in new[] { "audio_output", "audio_input", "audio_favorite" })
            Assert.False(state.RootElement.GetProperty(key).GetString()!.Equals("none", StringComparison.OrdinalIgnoreCase));
    }
    [Theory]
    [InlineData("http://localhost", 1883, "homeassistant")]
    [InlineData("localhost:1883", 1883, "homeassistant")]
    [InlineData("localhost", 0, "homeassistant")]
    [InlineData("localhost", 1883, "homeassistant/#")]
    public void InvalidBrokerSettingsFailBeforeAnyConnection(string host, int port, string prefix) =>
        Assert.Throws<InvalidDataException>(() => HomeAssistantProtocol.Validate(Options with { Host = host, Port = port, DiscoveryPrefix = prefix }));

    [Fact] public void ValidIpv6AndDisabledEmptyHostAreSupported()
    {
        HomeAssistantProtocol.Validate(Options with { Host = "::1", UseTls = true, Port = 8883 });
        HomeAssistantProtocol.Validate(Options with { Enabled = false, Host = "" });
    }
}
