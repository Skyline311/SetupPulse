using SetupPulse.App;
using SetupPulse.Core;

namespace SetupPulse.Tests;

public class StartupBehaviorTests
{
    [Fact]
    public void ExistingAutostartSettingsKeepTrayStartupAndProfiles()
    {
        var settings = Json.Decode<Settings>("""{"version":1,"profiles":[{"id":"office","name":"Office"}],"audioFavorites":[],"preferences":{"startWithWindows":true}}""");
        Assert.True(settings.Preferences.StartMinimized);
        Assert.True(StartupBehavior.StartHidden(["--autostart"], settings.Preferences));
        Assert.Equal("office", Assert.Single(settings.Profiles).Id);
    }

    [Fact]
    public void UserCanChooseVisibleAutostartWithoutChangingManualOrExplicitTrayLaunches()
    {
        var preferences = Json.Decode<Preferences>(Json.Encode(new Preferences(StartWithWindows: true, StartMinimized: false)));
        Assert.False(StartupBehavior.StartHidden(["--autostart"], preferences));
        Assert.True(StartupBehavior.StartHidden(["--tray"], preferences));
        Assert.False(StartupBehavior.StartHidden([], preferences with { StartMinimized = true }));
    }
}
