using SetupPulse.Core;
namespace SetupPulse.Tests;

public sealed class EngineTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "SetupPulseTests", Guid.NewGuid().ToString("N"));
    Store Store => new(folder);
    static DisplaySetting Screen(string id = "screen", int x = 0, bool primary = true) => new(id, id, 1920, 1080, x, 0, 1, 60, 100, primary);
    static AudioFavorite Favorite => new("favorite", "Headset", new("headset", 40, false));
    sealed class FakeHardware : IHardware
    {
        public int DisplayWrites, DisplayReads, AudioWrites, Restores, MissingAttempts;
        public bool FailDisplay, FailAudio, UnsupportedDpi, FailRestore;
        public int PositionWrites, FullApplyAttempts, DisplayDelayMilliseconds, DisplayValidations;
        public List<DisplaySetting> Displays = [Screen()];
        public List<AudioDevice> Audio = [new("speakers", "Speakers", false, true, true, true, 50, false)];
        public Inventory Inventory() { DisplayReads++; return new(Displays.Select(d => new DisplayDevice(d.DeviceId, d.Name, true, true, d)).ToList(), Audio.ToList()); }
        public List<AudioDevice> AudioInventory() => Audio.ToList();
        public void ValidateDisplays(List<DisplaySetting> displays) { DisplayValidations++; Validation.Displays(displays); }
        public void ApplyDisplays(List<DisplaySetting> displays) { FullApplyAttempts++; if (UnsupportedDpi) throw new NotSupportedException("DPI-Bereich wird nicht unterstützt."); Thread.Sleep(DisplayDelayMilliseconds); DisplayWrites++; Displays = displays; }
        public void ValidateDisplayPositions(List<DisplaySetting> displays) => Validation.Displays(displays);
        public void ApplyDisplayPositions(List<DisplaySetting> displays) { PositionWrites++; DisplayWrites++; Displays = displays; }
        public void VerifyDisplays(List<DisplaySetting> displays) { if (FailDisplay) { FailDisplay = false; throw new Exception("display failed"); } }
        public void ApplyAudio(AudioFavorite favorite) { AudioWrites++; if (MissingAttempts-- > 0) throw new DeviceUnavailableException("HDMI initializing"); if (FailAudio) throw new Exception("audio failed"); }
        public void RestoreAudio(List<AudioDevice> devices) { Restores++; if (FailRestore) throw new Exception("audio restore failed"); Audio = devices; }
    }
    [Fact]
    public async Task AudioOnlyNeverWritesDisplays()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        Assert.Null(await engine.Apply(null, Favorite)); Assert.Equal(0, hardware.DisplayWrites); Assert.Equal(0, hardware.DisplayReads); Assert.Equal(1, hardware.AudioWrites);
    }
    [Theory]
    [InlineData("output", "speakers")]
    [InlineData("input", "mic")]
    [InlineData("favorite", "desk")]
    public async Task HomeAssistantAudioSelectionsNeverReadOrWriteDisplays(string kind, string id)
    {
        var hardware = new FakeHardware();
        hardware.Audio.Add(new("mic", "Microphone", true, true, true, true, 50, false));
        var settings = Settings.Empty with { AudioFavorites = [new("desk", "Desk", new("speakers"), new("mic"))] };
        var selection = HomeAssistantProtocol.AudioSelection(settings, hardware.AudioInventory(), kind, id);
        var engine = new SwitchEngine(hardware, Store, _ => { });
        Assert.Null(await engine.Apply(null, selection));
        Assert.Equal(0, hardware.DisplayReads); Assert.Equal(0, hardware.DisplayWrites); Assert.Equal(1, hardware.AudioWrites);
        Assert.Null(engine.Pending); Assert.Null(Store.Read<Transaction>("last-good.json"));
    }
    [Fact]
    public async Task DirectSetupAudioUsesExistingTransactionWithoutTouchingDisplays()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var profile = new SetupProfile("direct", "Direct", Audio: new(new("speakers")));
        var settings = Settings.Empty with { Profiles = [profile] };
        Assert.Null(await engine.Apply(profile, ProfileAudioSelection.Resolve(profile, settings)));
        Assert.Equal(1, hardware.AudioWrites); Assert.Equal(0, hardware.DisplayReads); Assert.Equal(0, hardware.DisplayWrites);
        Assert.Null(engine.Pending);
    }
    [Fact]
    public async Task AudioFailureRestoresOnlyAudio()
    {
        var hardware = new FakeHardware { FailAudio = true }; var engine = new SwitchEngine(hardware, Store, _ => { });
        await Assert.ThrowsAsync<Exception>(() => engine.Apply(null, Favorite)); Assert.Equal(0, hardware.DisplayWrites); Assert.Equal(1, hardware.Restores); Assert.Null(engine.Pending);
        Assert.Equal("rolledBack", Store.Read<Transaction>("transaction.json")!.Phase);
        Assert.Equal("audio failed", Store.Read<Transaction>("transaction.json")!.Error);
    }
    [Fact]
    public async Task FailedRollbackRetainsOriginalAndRecoveryErrors()
    {
        var hardware = new FakeHardware { FailAudio = true, FailRestore = true }; var engine = new SwitchEngine(hardware, Store, _ => { });
        await Assert.ThrowsAsync<AggregateException>(() => engine.Apply(null, Favorite));
        var journal = Store.Read<Transaction>("transaction.json")!;
        Assert.Equal("failed", journal.Phase); Assert.Contains("audio failed", journal.Error); Assert.Contains("audio restore failed", journal.Error);
    }
    [Fact]
    public async Task ChangedDisplayProfileNeedsConfirmation()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var p = new SetupProfile("profile", "Living room", [Screen("tv")]);
        var pending = await engine.Apply(p, null); Assert.NotNull(pending); Assert.NotNull(engine.Pending);
        Assert.Equal(p.Id, engine.Confirm(pending!.Id).ProfileId); Assert.Null(engine.Pending); Assert.NotNull(Store.Read<Transaction>("last-good.json"));
    }
    [Fact]
    public async Task VerifiedProfileSwitchesDirectly()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { }); var d = new List<DisplaySetting> { Screen("tv") };
        Assert.Null(await engine.Apply(new("p", "TV", d, VerifiedHash: Validation.MonitorHash(d)), null)); Assert.Null(engine.Pending);
    }
    [Fact]
    public async Task FailureAfterDisplayChangeRestoresBothSnapshots()
    {
        var hardware = new FakeHardware { FailAudio = true }; var engine = new SwitchEngine(hardware, Store, _ => { });
        await Assert.ThrowsAsync<Exception>(() => engine.Apply(new("p", "TV", [Screen("tv")]), Favorite));
        Assert.Equal(2, hardware.DisplayWrites); Assert.Equal("screen", hardware.Displays.Single().DeviceId); Assert.Equal(1, hardware.Restores);
    }
    [Fact]
    public async Task DisplayReadbackFailureRollsBack()
    {
        var hardware = new FakeHardware { FailDisplay = true }; var engine = new SwitchEngine(hardware, Store, _ => { });
        await Assert.ThrowsAsync<Exception>(() => engine.Apply(new("p", "TV", [Screen("tv")]), null)); Assert.Equal("screen", hardware.Displays.Single().DeviceId);
    }
    [Fact]
    public async Task DelayedHdmiAudioIsRetried()
    {
        var hardware = new FakeHardware { MissingAttempts = 1 }; var engine = new SwitchEngine(hardware, Store, _ => { });
        Assert.NotNull(await engine.Apply(new("p", "TV", [Screen("tv")]), Favorite)); Assert.Equal(2, hardware.AudioWrites);
    }
    [Fact]
    public async Task PendingTestBlocksFurtherSwitches()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        await engine.Apply(new("p", "TV", [Screen("tv")]), null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Apply(null, Favorite)); Assert.Equal(0, hardware.AudioWrites);
    }
    [Fact]
    public async Task ConcurrentSwitchIsRejected()
    {
        var hardware = new FakeHardware { MissingAttempts = 1 }; var engine = new SwitchEngine(hardware, Store, _ => { });
        var running = engine.Apply(new("p", "TV", [Screen("tv")]), Favorite);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Apply(null, Favorite)); await running;
    }
    [Fact]
    public async Task WatchdogStartFailurePreventsHardwareChanges()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => throw new Exception("cannot launch"));
        await Assert.ThrowsAsync<Exception>(() => engine.Apply(new("p", "TV", [Screen("tv")]), null));
        Assert.Equal(0, hardware.DisplayWrites); Assert.Equal(0, hardware.Restores); Assert.Null(engine.Pending);
    }
    [Fact]
    public void SavingRecoveredSettingsPreservesCorruptOriginalAndValidBackup()
    {
        var store = Store; store.Write("settings.json", Settings.Empty); store.Write("settings.json", Settings.Empty); File.WriteAllText(store.FilePath("settings.json"), "broken");
        store.Load(); store.Write("settings.json", Settings.Empty); Assert.Single(Directory.GetFiles(folder, "*.corrupt-*")); Assert.Equal(1, store.Read<Settings>("settings.json.bak")!.Version);
    }
    [Fact]
    public async Task StaleWatchdogCannotOverwriteConfirmedState()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var t = (await engine.Apply(new("p", "TV", [Screen("tv")]), null))!; engine.Confirm(t.Id); engine.Rollback(t);
        Assert.Equal(1, hardware.DisplayWrites); Assert.Equal(0, hardware.Restores);
    }
    [Fact]
    public async Task OldTransactionCannotRollbackNewTransaction()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var t = (await engine.Apply(new("p", "TV", [Screen("tv")]), null))!;
        Store.Write("transaction.json", t with { Id = "new" }); engine.Rollback(t); Assert.Equal(1, hardware.DisplayWrites);
    }
    [Fact]
    public async Task ExpiredTransactionCannotBeConfirmed()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var t = (await engine.Apply(new("p", "TV", [Screen("tv")]), null))!;
        Store.Write("transaction.json", t with { Deadline = DateTimeOffset.UtcNow.AddSeconds(-1) }); Assert.Throws<InvalidOperationException>(() => engine.Confirm(t.Id));
    }
    [Fact]
    public void CorruptSettingsRecoverFromValidBackup()
    {
        Store.Write("settings.json", Settings.Empty); Store.Write("settings.json", Settings.Empty with { Preferences = new("dark") }); File.WriteAllText(Store.FilePath("settings.json"), "{broken");
        var store = Store; Assert.Equal("system", store.Load().Preferences.Theme); Assert.NotNull(store.RecoveryWarning);
    }
    [Fact]
    public void CorruptSettingsWithoutBackupAreNotSilentlyDeleted()
    {
        Directory.CreateDirectory(folder); File.WriteAllText(Store.FilePath("settings.json"), "{broken"); Assert.Throws<InvalidDataException>(() => Store.Load()); Assert.Equal("{broken", File.ReadAllText(Store.FilePath("settings.json")));
    }
    [Fact]
    public void OverlapAndDisconnectedLayoutsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Validation.Displays([Screen(), Screen("other", 100, false)])); Assert.Throws<InvalidDataException>(() => Validation.Displays([Screen(), Screen("other", 2000, false)])); Validation.Displays([Screen(), Screen("other", 1920, false)]);
    }
    [Fact] public void DuplicatePhysicalDisplaysAreRejected() => Assert.Throws<InvalidDataException>(() => Validation.Displays([Screen(), Screen("SCREEN", 1920, false)]));
    [Theory][InlineData("Ctrl+Alt+9", "Strg+Alt+9")][InlineData("ALT+CTRL+F12", "Ctrl+Alt+F12")] public void HotkeyAliasesNormalize(string a, string b) => Assert.Equal(Shortcut.Normalize(a), Shortcut.Normalize(b));
    [Fact]
    public void DuplicateShortcutsAreRejected()
    {
        var s = Settings.Empty with { AudioFavorites = [Favorite with { Shortcut = "Ctrl+Alt+9" }], Profiles = [new("p", "Office", Shortcut: "Strg+Alt+9")] };
        Assert.Throws<InvalidDataException>(() => Validation.Settings(s));
    }
    [Fact] public void InvalidAudioVolumeIsRejected() => Assert.Throws<InvalidDataException>(() => Validation.Settings(Settings.Empty with { AudioFavorites = [Favorite with { Output = new("headset", float.NaN) }] }));
    static FakeHardware AlignmentHardware() => new() { Displays = [Screen(), Screen("right", 1920, false)] };
    [Fact] public void AlignmentStartsWatchdogBeforeAnyDisplayWrite()
    {
        var hardware = AlignmentHardware(); int watchdogs = 0;
        var engine = new SwitchEngine(hardware, Store, _ => { watchdogs++; Assert.Equal(0, hardware.DisplayWrites); });
        var transaction = engine.BeginAlignment();
        Assert.True(transaction.LiveAlignment); Assert.Equal(1, watchdogs); Assert.Equal(0, hardware.DisplayWrites);
    }
    [Fact] public void RepeatedAlignmentUpdatesCancelToOriginalAndNeverWriteAudio()
    {
        var hardware = AlignmentHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var transaction = engine.BeginAlignment();
        engine.UpdateAlignment(transaction.Id, [Screen(), Screen("right", 1920, false) with { Y = 80 }]);
        engine.UpdateAlignment(transaction.Id, [Screen(), Screen("right", 1920, false) with { Y = -120 }]);
        Assert.Equal(-120, hardware.Displays[1].Y);
        hardware.Audio = [hardware.Audio[0] with { Volume = 77 }];
        engine.Rollback(engine.Pending!);
        Assert.Equal(0, hardware.Displays[1].Y); Assert.Equal(77, hardware.Audio[0].Volume);
        Assert.Equal(0, hardware.AudioWrites); Assert.Equal(0, hardware.Restores); Assert.Null(engine.Pending);
    }
    [Fact] public void AlignmentConfirmationKeepsLatestLayoutWithoutUpdatingProfiles()
    {
        var hardware = AlignmentHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var transaction = engine.BeginAlignment();
        engine.UpdateAlignment(transaction.Id, [Screen(), Screen("right", 1920, false) with { Y = 100 }]);
        Assert.Null(engine.Confirm(transaction.Id).ProfileId);
        Assert.Equal(100, Store.Read<Transaction>("last-good.json")!.Displays![1].Y);
        Assert.Null(engine.Pending); Assert.Equal(0, hardware.Restores);
    }
    [Fact] public void AlignmentRejectsNonYChangesBeforeTouchingHardware()
    {
        var hardware = AlignmentHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var transaction = engine.BeginAlignment();
        Assert.Throws<InvalidDataException>(() => engine.UpdateAlignment(transaction.Id, [Screen(), Screen("right", 1920, false) with { Scale = 125 }]));
        Assert.Throws<InvalidDataException>(() => engine.UpdateAlignment(transaction.Id, [Screen()]));
        Assert.Equal(0, hardware.DisplayWrites); Assert.Equal(0, hardware.AudioWrites);
    }
    [Fact] public void ExpiredAlignmentCannotBeRevivedByAnUpdate()
    {
        var hardware = AlignmentHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var transaction = engine.BeginAlignment();
        Store.Write("transaction.json", transaction with { Deadline = DateTimeOffset.UtcNow.AddSeconds(-1) });
        Assert.Throws<InvalidOperationException>(() => engine.UpdateAlignment(transaction.Id, [Screen(), Screen("right", 1920, false) with { Y = 30 }]));
        Assert.Equal(0, hardware.DisplayWrites);
    }
    [Fact] public void AlignmentFailureRestoresOriginalLayoutWithoutRestoringAudio()
    {
        var hardware = AlignmentHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var transaction = engine.BeginAlignment(); hardware.FailDisplay = true;
        Assert.Throws<Exception>(() => engine.UpdateAlignment(transaction.Id, [Screen(), Screen("right", 1920, false) with { Y = 30 }]));
        Assert.Equal(0, hardware.Displays[1].Y); Assert.Equal(0, hardware.Restores); Assert.Null(engine.Pending);
    }
    [Fact] public void AlignmentWatchdogFailureLeavesNoPendingSessionOrDeviceChanges()
    {
        var hardware = AlignmentHardware(); var engine = new SwitchEngine(hardware, Store, _ => throw new Exception("watchdog unavailable"));
        Assert.Throws<Exception>(() => engine.BeginAlignment());
        Assert.Null(engine.Pending); Assert.Equal(0, hardware.DisplayWrites); Assert.Equal(0, hardware.Restores);
    }
    [Fact] public async Task AlignmentCannotReplaceANormalPendingMonitorTest()
    {
        var hardware = AlignmentHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var transaction = (await engine.Apply(new("profile", "Desk", hardware.Displays), null))!;
        Assert.Throws<InvalidOperationException>(() => engine.BeginAlignment());
        Assert.Throws<InvalidOperationException>(() => engine.UpdateAlignment(transaction.Id, hardware.Displays));
        Assert.Equal(transaction.Id, engine.Pending!.Id);
    }
    [Fact] public void WatchdogRechecksAnExtendedAlignmentDeadlineBeforeRollback()
    {
        var hardware = AlignmentHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var transaction = engine.BeginAlignment();
        var updated = engine.UpdateAlignment(transaction.Id, [Screen(), Screen("right", 1920, false) with { Y = 60 }]);
        var stale = updated with { Deadline = DateTimeOffset.UtcNow.AddSeconds(-1) };
        Store.Write("transaction.json", stale);
        Store.Write("transaction.json", updated with { Deadline = DateTimeOffset.UtcNow.AddSeconds(90) });
        Assert.False(engine.RollbackIfExpired(stale.Id)); Assert.Equal(60, hardware.Displays[1].Y);
        Store.Write("transaction.json", stale);
        var watchdog = new SwitchEngine(hardware, Store, _ => { });
        Assert.True(watchdog.RollbackIfExpired(stale.Id)); Assert.Equal(0, hardware.Displays[1].Y); Assert.Equal(0, hardware.Restores);
    }
    [Fact] public void AlignmentAndRollbackBypassFullModeAndDpiChanges()
    {
        var hardware = AlignmentHardware(); hardware.UnsupportedDpi = true;
        var engine = new SwitchEngine(hardware, Store, _ => { });
        var transaction = engine.BeginAlignment();
        engine.UpdateAlignment(transaction.Id, [Screen(), Screen("right", 1920, false) with { Y = -90 }]);
        Assert.Equal(-90, hardware.Displays[1].Y);
        engine.Rollback(engine.Pending!);
        Assert.Equal(0, hardware.Displays[1].Y); Assert.Equal(2, hardware.PositionWrites);
        Assert.Equal(0, hardware.FullApplyAttempts); Assert.Equal(0, hardware.Restores);
    }
    [Fact] public async Task ConfirmationDeadlineStartsAfterDisplayApplicationCompletes()
    {
        var hardware = new FakeHardware { DisplayDelayMilliseconds = 300 };
        DateTimeOffset initialDeadline = default;
        var engine = new SwitchEngine(hardware, Store, _ => initialDeadline = Store.Read<Transaction>("transaction.json")!.Deadline);
        var ready = (await engine.Apply(new("profile", "Desk", [Screen("tv")]), null))!;
        Assert.True(ready.Deadline - initialDeadline >= TimeSpan.FromMilliseconds(200));
        Assert.Equal(ready.Deadline, engine.Pending!.Deadline);
    }
    [Fact] public async Task ReactivatingCurrentVerifiedProfileDoesNotValidateOrWriteDisplaysButAppliesAudio()
    {
        var hardware = AlignmentHardware(); var displays = hardware.Displays.AsEnumerable().Reverse()
            .Select(d => d with { DeviceId = d.DeviceId.ToUpperInvariant(), Name = "Renamed" }).ToList();
        var engine = new SwitchEngine(hardware, Store, _ => { });
        var profile = new SetupProfile("office", "Office", displays, VerifiedHash: Validation.MonitorHash(displays));
        Assert.Null(await engine.Apply(profile, Favorite));
        Assert.Equal(0, hardware.DisplayValidations); Assert.Equal(0, hardware.FullApplyAttempts);
        Assert.Equal(0, hardware.DisplayWrites); Assert.Equal(1, hardware.AudioWrites);
        Assert.Equal("confirmed", Store.Read<Transaction>("transaction.json")!.Phase);
        Assert.Null(Store.Read<Transaction>("transaction.json")!.Displays);
        Assert.Equal(displays, Store.Read<Transaction>("last-good.json")!.Displays);
        Assert.Equal(1, hardware.DisplayReads);
    }
    [Fact] public async Task AudioFailureOnCurrentProfileNeverReappliesMonitorSnapshot()
    {
        var hardware = new FakeHardware { FailAudio = true };
        var engine = new SwitchEngine(hardware, Store, _ => { });
        await Assert.ThrowsAsync<Exception>(() => engine.Apply(new("office", "Office", [Screen()]), Favorite));
        Assert.Equal(0, hardware.DisplayValidations); Assert.Equal(0, hardware.DisplayWrites);
        Assert.Equal(1, hardware.Restores); Assert.Equal("rolledBack", Store.Read<Transaction>("transaction.json")!.Phase);
    }
    [Fact] public async Task UnverifiedMatchingProfileCanStillBeConfirmedWithoutDisplayWrites()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var pending = await engine.Apply(new("office", "Office", [Screen()]), null);
        Assert.NotNull(pending); Assert.Null(pending.Displays);
        engine.Confirm(pending.Id);
        Assert.Null(engine.Pending); Assert.Equal(0, hardware.DisplayWrites);
        Assert.Equal(0, hardware.DisplayValidations); Assert.Equal("office", Store.Read<Transaction>("last-good.json")!.ProfileId);
    }
    [Fact] public async Task ExpiredMatchingProfileConfirmationDoesNotWriteDisplays()
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var pending = (await engine.Apply(new("office", "Office", [Screen()]), null))!;
        Store.Write("transaction.json", pending with { Deadline = DateTimeOffset.UtcNow.AddSeconds(-1) });
        Assert.True(engine.RollbackIfExpired(pending.Id));
        Assert.Equal(0, hardware.DisplayWrites); Assert.Equal(0, hardware.DisplayValidations);
    }
    [Theory]
    [InlineData("resolution")][InlineData("height")][InlineData("position")]
    [InlineData("rotation")][InlineData("scale")][InlineData("refresh")][InlineData("device")]
    [InlineData("count")]
    public async Task RealDisplayDifferencesStillApplyNativeConfiguration(string difference)
    {
        var hardware = new FakeHardware(); var requested = Screen();
        requested = difference switch {
            "resolution" => requested with { Width = 2560 },
            "height" => requested with { Height = 1440 },
            "rotation" => requested with { Rotation = 3 },
            "scale" => requested with { Scale = 125 },
            "refresh" => requested with { RefreshRate = 59.94 },
            "device" => requested with { DeviceId = "tv" },
            _ => requested
        };
        if (difference == "position") {
            hardware.Displays.Add(Screen("right", 1920, false));
        }
        var displays = difference == "position" ? new List<DisplaySetting> { requested, Screen("right", 1920, false) with { Y = 100 } }
            : difference == "count" ? [requested, Screen("right", 1920, false)] : [requested];
        var engine = new SwitchEngine(hardware, Store, _ => { });
        Assert.NotNull(await engine.Apply(new("office", "Office", displays), null));
        Assert.Equal(1, hardware.DisplayValidations); Assert.Equal(1, hardware.FullApplyAttempts);
        Assert.Equal(1, hardware.DisplayWrites);
    }
    [Theory]
    [InlineData(59.999, true)]
    [InlineData(60.001, true)]
    [InlineData(60.0004, false)]
    public async Task NoOpComparisonUsesTheSameMillihertzResolutionAsNativeApplication(double refresh, bool changes)
    {
        var hardware = new FakeHardware(); var engine = new SwitchEngine(hardware, Store, _ => { });
        var requested = new List<DisplaySetting> { Screen() with { RefreshRate = refresh } };
        await engine.Apply(new("office", "Office", requested, VerifiedHash: Validation.MonitorHash(requested)), null);
        Assert.Equal(changes ? 1 : 0, hardware.FullApplyAttempts);
        Assert.Equal(changes ? 1 : 0, hardware.DisplayValidations);
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
