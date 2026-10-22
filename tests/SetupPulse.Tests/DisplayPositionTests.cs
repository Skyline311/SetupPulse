using System.Runtime.InteropServices;
using SetupPulse.App;
using SetupPulse.Core;
using static SetupPulse.App.Displays;

namespace SetupPulse.Tests;

public sealed class DisplayPositionTests
{
    [Theory]
    [InlineData(96, 100)][InlineData(120, 125)][InlineData(144, 150)][InlineData(168, 175)][InlineData(192, 200)][InlineData(240, 250)]
    public void ActualWindowDpiMapsToDesktopScale(uint dpi, int expected) => Assert.Equal(expected, ScaleFromDpi(dpi));
    [Fact] public void MissingWindowDpiIsNotAcceptedAsAScale() => Assert.Throws<InvalidOperationException>(() => ScaleFromDpi(0));
    static DisplaySetting Tv() => new("tv", "Sony TV", 1920, 1080, 0, 0, 1, 60, 175, true);
    [Fact]
    public void VerificationAcceptsDelayedDpiReadbackWithoutChangingRequestedSettings()
    {
        var requested = Tv(); int reads = 0, waits = 0;
        VerifyReadback([requested], () => [++reads < 3 ? requested with { Scale = 100 } : requested], () => waits++);
        Assert.Equal(3, reads); Assert.Equal(2, waits); Assert.Equal(175, requested.Scale);
    }
    [Fact]
    public void VerificationRejectsPersistentScaleMismatchAndReportsBothValues()
    {
        int waits = 0;
        var error = Assert.Throws<InvalidOperationException>(() => VerifyReadback([Tv()], () => [Tv() with { Scale = 100 }], () => waits++, attempts: 3));
        Assert.Contains("175%", error.Message); Assert.Contains("100%", error.Message); Assert.Equal(2, waits);
    }
    [Fact]
    public void VerificationStillRejectsWrongResolutionAfterTopologySettles()
    {
        var error = Assert.Throws<InvalidOperationException>(() => VerifyReadback([Tv()], () => [Tv() with { Width = 1280, Height = 720 }], () => { }, attempts: 2));
        Assert.Contains("1920×1080", error.Message); Assert.Contains("1280×720", error.Message);
    }
    [Fact]
    public void MatchingReadbackDoesNotWait()
        => VerifyReadback([Tv()], () => [Tv()], () => throw new Exception("Unexpected wait"));
    [Theory]
    [InlineData(-2, 1, 100, -2)] // Captured range; stale current=-3 must not prevent this valid target.
    [InlineData(-2, 1, 150, 0)]
    [InlineData(0, 20, 100, 0)] // Windows may expose scales above the app's selection list.
    [InlineData(0, 20, 500, 11)]
    public void DpiTargetDependsOnRequestedScaleAndAllowedRange(int minimum, int maximum, int percent, int expected)
        => Assert.Equal(expected, DpiTarget(minimum, maximum, percent));

    [Theory]
    [InlineData(1, 2, 100)]
    [InlineData(0, -1, 100)]
    [InlineData(-2, 1, 200)]
    [InlineData(0, 3, 110)]
    public void DpiTargetRejectsUnsupportedTargetsAndIncoherentRanges(int minimum, int maximum, int percent)
        => Assert.Throws<NotSupportedException>(() => DpiTarget(minimum, maximum, percent));

    static (DisplayPath[] Paths, Mode[] Modes, List<DisplaySetting> Settings) Layout()
    {
        var adapter = new Luid { Low = 11, High = 22 };
        var paths = Enumerable.Range(0, 2).Select(i => new DisplayPath {
            Flags = 1, Source = new() { Adapter = adapter, Id = (uint)i, Mode = (uint)i, Status = 3 },
            Target = new() { Adapter = adapter, Id = (uint)i + 5, Mode = (uint)i + 2, Rotation = i == 1 ? 4u : 1u,
                Scaling = 4, Technology = 10, Scan = 1, Available = 1, Status = 7,
                Refresh = new() { Numerator = i == 0 ? 144000u : 59950u, Denominator = 1000 } }
        }).ToArray();
        var modes = Enumerable.Range(0, 4).Select(i => new Mode { Type = i < 2 ? 1u : 2u, Id = (uint)(i < 2 ? i : i + 3), Adapter = adapter,
            Data = new() { Source = new() { Width = i == 0 ? 2560u : 1920u, Height = i == 0 ? 1440u : 1200u,
                Format = 4, Position = new() { X = i == 1 ? 2560 : 0, Y = i == 1 ? -235 : 0 } } }
        }).ToArray();
        return (paths, modes, [new("main", "Main", 2560, 1440, 0, 0, 1, 144, 100, true), new("dell", "Dell", 1200, 1920, 2560, -235, 4, 59.95, 150, false)]);
    }
    static byte[] Bytes<T>(T[] values) where T : struct => MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    [Fact] public void PositionPlanPreservesPathsTimingRotationScaleAndEveryNonYModeByte()
    {
        var (paths, modes, settings) = Layout();
        byte[] originalModes = Bytes(modes), originalPaths = Bytes(paths);
        settings[1] = settings[1] with { Y = -300 };
        var plan = BuildPositionPlan(paths, modes, settings, ["main", "dell"]);
        Assert.Equal(originalPaths, Bytes(plan.Paths)); Assert.Equal(originalPaths, Bytes(paths));
        Assert.Equal(originalModes, Bytes(modes)); Assert.Equal(-300, plan.Modes[1].Data.Source.Position.Y);
        plan.Modes[1].Data.Source.Position.Y = -235;
        Assert.Equal(originalModes, Bytes(plan.Modes));
    }
    [Fact] public void PositionPlanRejectsChangedActiveSetBeforeApplying()
    {
        var (paths, modes, settings) = Layout();
        Assert.Throws<InvalidOperationException>(() => BuildPositionPlan(paths, modes, settings.Take(1).ToList(), ["main", "dell"]));
        Assert.Throws<InvalidOperationException>(() => BuildPositionPlan(paths, modes, settings, ["main", "other"]));
    }
    [Fact] public void PositionPlanRejectsModeOrSourceChangesInsteadOfSilentlyReconfiguring()
    {
        var (paths, modes, settings) = Layout();
        settings[1] = settings[1] with { RefreshRate = 75 };
        Assert.Throws<InvalidOperationException>(() => BuildPositionPlan(paths, modes, settings, ["main", "dell"]));
        settings[1] = settings[1] with { RefreshRate = 59.95 };
        paths[1].Source.Mode = uint.MaxValue;
        Assert.Throws<NotSupportedException>(() => BuildPositionPlan(paths, modes, settings, ["main", "dell"]));
    }
}
