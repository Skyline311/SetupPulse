using SetupPulse.Core;

namespace SetupPulse.Tests;

public sealed class ProfileMatchingTests
{
    static readonly DisplaySetting Screen = new("screen", "Display", 2560, 1440, 0, 0, 1, 60, 100, true);
    static readonly AudioFavorite Speakers = new("speakers", "Speakers", new("output"));
    static Inventory Current(DisplaySetting? display = null, bool speakersDefault = true) => new(
        [new("SCREEN", "Renamed display", true, true, (display ?? Screen) with { Name = "Renamed display", RefreshRate = 59.94 })],
        [new("output", "Speakers", false, true, speakersDefault, false, 50, false)]);
    static Settings Saved(SetupProfile profile) => new(1, [profile], [Speakers], new());

    [Fact] public void StartupMatchUsesHardwareAndToleratesDriverRefreshRounding()
    {
        var profile = new SetupProfile("desk", "Desk", [Screen], Speakers.Id);
        Assert.True(ProfileMatching.Matches(profile, Saved(profile), Current()));
    }
    [Fact] public void SeparateAudioChangeKeepsLinkedAndMonitorOnlyLayoutsActive()
    {
        var linked = new SetupProfile("desk", "Desk", [Screen], Speakers.Id);
        var monitors = linked with { AudioFavoriteId = null };
        Assert.True(ProfileMatching.Matches(linked, Saved(linked), Current(speakersDefault: false)));
        Assert.True(ProfileMatching.Matches(monitors, Saved(monitors), Current(speakersDefault: false)));
        Assert.False(AudioMatching.Matches(Speakers, Current(speakersDefault: false).Audio));
        Assert.Same(Speakers, ProfileAudioSelection.Resolve(linked, Saved(linked)));
    }
    [Fact] public void ExternalLayoutOrScalingChangeDeactivatesSetup()
    {
        var profile = new SetupProfile("desk", "Desk", [Screen]);
        Assert.False(ProfileMatching.Matches(profile, Saved(profile), Current(Screen with { Scale = 125 })));
        Assert.False(ProfileMatching.Matches(profile, Saved(profile), Current(Screen with { X = 100 })));
        Assert.False(ProfileMatching.Matches(profile, Saved(profile), Current(Screen with { Primary = false })));
    }
    [Fact] public void AdditionalActiveMonitorDeactivatesSavedLayout()
    {
        var profile = new SetupProfile("desk", "Desk", [Screen]);
        var inventory = Current();
        inventory.Displays.Add(new("other", "Other", true, true, Screen with { DeviceId = "other", X = 2560, Primary = false }));
        Assert.False(ProfileMatching.Matches(profile, Saved(profile), inventory));
    }
    [Fact] public void AudioOnlyProfileDoesNotRequireMatchingDisplays()
    {
        var profile = new SetupProfile("audio", "Audio", AudioFavoriteId: Speakers.Id);
        Assert.True(ProfileMatching.Matches(profile, Saved(profile), Current(Screen with { Scale = 150 })));
        Assert.False(ProfileMatching.Matches(profile, Saved(profile), Current(speakersDefault: false)));
    }
    [Fact] public void MissingFavoriteDoesNotHideMatchingLayoutButAudioOnlyAndEmptyProfilesAreInactive()
    {
        var missing = new SetupProfile("missing", "Missing", [Screen], "removed");
        var empty = new SetupProfile("empty", "Empty");
        Assert.True(ProfileMatching.Matches(missing, Saved(missing), Current()));
        Assert.False(ProfileMatching.Matches(missing with { Monitors = null }, Saved(missing), Current()));
        Assert.False(ProfileMatching.Matches(empty, Saved(empty), Current()));
    }
    [Fact] public void InlineAudioVolumeMuteCommunicationsAndUnavailableDevicesDoNotChangeActiveLayout()
    {
        var linked = new SetupProfile("desk", "Desk", [Screen], Speakers.Id);
        var direct = linked with { AudioFavoriteId = null, Audio = new(new("other", 20, true), new("mic")) };
        var changed = Current() with { Audio = [] };
        Assert.True(ProfileMatching.Matches(linked, Saved(linked), changed));
        Assert.True(ProfileMatching.Matches(direct, Saved(direct), changed));
        var wrongRate = changed with { Displays = [new("screen", "Display", true, true, Screen with { RefreshRate = 144 })] };
        Assert.False(ProfileMatching.Matches(direct, Saved(direct), wrongRate));
    }
}
