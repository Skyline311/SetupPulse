using SetupPulse.Core;

namespace SetupPulse.Tests;

public sealed class ProfileAudioSelectionTests
{
    static SetupProfile Direct(ProfileAudio? audio = null) => new("tv", "TV", Audio: audio ?? new(new("hdmi")));
    static Settings Saved(SetupProfile profile) => Settings.Empty with { Profiles = [profile] };

    [Fact] public void DirectSelectionNeedsNoFavoriteAndKeepsVolumeMuteAndCommunicationsUnspecified()
    {
        var profile = Direct();
        var settings = Saved(profile);
        Validation.Settings(settings);
        var operation = ProfileAudioSelection.Resolve(profile, settings)!;
        Assert.Equal("hdmi", operation.Output!.DeviceId);
        Assert.Null(operation.Output.Volume); Assert.Null(operation.Output.Muted);
        Assert.Null(operation.Input); Assert.Null(operation.CommunicationsOutput); Assert.Null(operation.Shortcut);
        Assert.Empty(settings.AudioFavorites);
    }

    [Fact] public void NameAndEndpointsSurviveExportImportWithoutGlobalFavoriteIds()
    {
        var profile = Direct(new(new("hdmi"), new("mic"), "Wohnzimmer"));
        var imported = Json.Decode<Settings>(Json.Encode(Saved(profile)));
        Validation.Settings(imported);
        var operation = ProfileAudioSelection.Resolve(imported.Profiles.Single(), imported)!;
        Assert.Equal("Wohnzimmer", operation.Name); Assert.Equal("mic", operation.Input!.DeviceId);
        Assert.Empty(imported.AudioFavorites); Assert.Null(imported.Profiles.Single().AudioFavoriteId);
    }

    [Fact] public void LegacyProfilesStillResolveOriginalFavoriteIncludingCommunicationAndVolume()
    {
        var favorite = new AudioFavorite("speaker", "PC", new("out", 15, false), CommunicationsOutput: "comm");
        var legacy = Json.Decode<SetupProfile>("""{"id":"old","name":"Old","audioFavoriteId":"speaker"}""");
        var settings = Saved(legacy) with { AudioFavorites = [favorite] };
        Validation.Settings(settings);
        Assert.Null(legacy.Audio); Assert.Same(favorite, ProfileAudioSelection.Resolve(legacy, settings));
    }

    [Fact] public void MatchingUsesDirectEndpointsAndDirectionWithoutChangingDisplays()
    {
        var profile = Direct(new(new("hdmi"), new("mic")));
        var inventory = new Inventory([], [new("hdmi", "TV", false, true, true, false, 80, true), new("mic", "Mic", true, true, true, false, 30, false)]);
        Assert.True(ProfileMatching.Matches(profile, Saved(profile), inventory));
        Assert.False(ProfileMatching.Matches(profile, Saved(profile), inventory with { Audio = [inventory.Audio[0] with { Input = true }, inventory.Audio[1]] }));
        Assert.False(ProfileMatching.Matches(profile, Saved(profile), inventory with { Audio = [inventory.Audio[0] with { Available = false }, inventory.Audio[1]] }));
        Assert.False(ProfileMatching.Matches(profile, Saved(profile), inventory with { Audio = [inventory.Audio[0], inventory.Audio[1] with { Default = false }] }));
    }

    [Fact] public void RejectsAmbiguousOrEmptySelectionButAllowsRememberingOfflineDevices()
    {
        var direct = Direct();
        Validation.Settings(Saved(direct));
        Assert.Throws<InvalidDataException>(() => Validation.Settings(Saved(direct with { Audio = new() })));
        Assert.Throws<InvalidDataException>(() => Validation.Settings(Saved(Direct(new(new(" "))))));
        Assert.Throws<InvalidDataException>(() => Validation.Settings(Saved(Direct(new(new("hdmi"), Name: new string('x', 81))))));
        var favorite = new AudioFavorite("f", "F", new("out"));
        Assert.Throws<InvalidDataException>(() => Validation.Settings(Saved(direct with { AudioFavoriteId = "f" }) with { AudioFavorites = [favorite] }));
    }
}
