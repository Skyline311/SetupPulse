using SetupPulse.Core;

namespace SetupPulse.Tests;

public class MixerPreferencesTests
{
    [Fact]
    public void LegacySettingsKeepExistingProfilesAndDoNotEnableNewHotkeysOrMouseHook()
    {
        var settings = Json.Decode<Settings>("""{"version":1,"profiles":[{"id":"old","name":"Office","shortcut":"Ctrl+Alt+8"}],"audioFavorites":[],"preferences":{"theme":"dark","closeToTray":true}}""");
        Validation.Settings(settings);
        Assert.Equal("Ctrl+Alt+8", Assert.Single(settings.Profiles).Shortcut);
        Assert.Null(settings.Preferences.MixerShortcut);
        Assert.Null(settings.Preferences.QuickMixerShortcut);
        Assert.Null(settings.Preferences.VolumeUpShortcut);
        Assert.False(settings.Preferences.TrayScrollVolume);
        Assert.False(settings.Preferences.LogarithmicVolume);
    }

    [Fact]
    public void MixerHotkeysCannotStealAnExistingSetupShortcut()
    {
        var settings = Settings.Empty with { Profiles = [new("p", "Office", Shortcut: "Ctrl+Alt+8")], Preferences = new(MixerShortcut: "Strg+Alt+8") };
        Assert.Throws<InvalidDataException>(() => Validation.Settings(settings));
    }

    [Fact]
    public void VolumeHotkeysRemainDistinctFromNumberPadFavorites()
    {
        var settings = Settings.Empty with { AudioFavorites = [new("f", "Headset", new("audio"), Shortcut: "Ctrl+Alt+Num2")], Preferences = new(VolumeUpShortcut: "Ctrl+Alt+2", VolumeDownShortcut: "Ctrl+Alt+3") };
        Validation.Settings(settings);
        Assert.Throws<InvalidDataException>(() => Validation.Settings(settings with { Preferences = settings.Preferences with { QuickMixerShortcut = "Alt+Ctrl+2" } }));
    }
}
