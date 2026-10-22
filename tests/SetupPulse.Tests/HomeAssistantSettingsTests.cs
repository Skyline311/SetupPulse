using SetupPulse.App;
using SetupPulse.Core;

namespace SetupPulse.Tests;

public sealed class HomeAssistantSettingsTests
{
    [Fact] public void PasswordIsProtectedPreservedAndClearedSeparatelyFromProfileExport()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI is a Windows user facility; CI runs on Windows.
        string directory = Path.Combine(Path.GetTempPath(), "SetupPulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(directory); var credentials = new HomeAssistantSettingsStore(store);
            var initial = credentials.Load();
            Assert.False(initial.Options.Enabled); Assert.False(File.Exists(store.FilePath("home-assistant.json")));
            var saved = credentials.Save(initial, new(initial.Options with { Host = "localhost", Enabled = true, InstanceId = new string('f', 32) }, "test-only-password"));
            Assert.Equal(initial.Options.InstanceId, saved.Options.InstanceId);
            Assert.Equal("test-only-password", HomeAssistantSettingsStore.Password(credentials.Load()));
            Assert.DoesNotContain("test-only-password", File.ReadAllText(store.FilePath("home-assistant.json")));
            Assert.DoesNotContain("localhost", Json.Encode(Settings.Empty));
            var kept = credentials.Save(saved, new(saved.Options with { Enabled = false }));
            Assert.Equal(saved.ProtectedPassword, kept.ProtectedPassword);
            var cleared = credentials.Save(kept, new(kept.Options, ClearPassword: true));
            Assert.Null(cleared.ProtectedPassword); Assert.Equal("", HomeAssistantSettingsStore.Password(cleared));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
