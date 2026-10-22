using SetupPulse.Core;
namespace SetupPulse.Tests;
public sealed class AudioMatchingTests
{
    static AudioDevice Output(string id, bool current = false, float volume = 50) => new(id, id, false, true, current, false, volume, false);
    [Fact] public void CycleSkipsUnavailableDevices()
    {
        var first = new AudioFavorite("a", "Speakers", new("speakers")); var missing = new AudioFavorite("b", "Offline", new("offline")); var next = new AudioFavorite("c", "Headset", new("headset"));
        Assert.Equal(next, AudioMatching.Cycle([first, missing, next], [Output("speakers", true), Output("headset")], 1));
    }
    [Fact] public void PreviousWithoutMatchStartsAtLastAvailableFavorite()
    {
        var first = new AudioFavorite("a", "A", new("a")); var last = new AudioFavorite("b", "B", new("b"));
        Assert.Equal(last, AudioMatching.Cycle([first, last], [Output("a"), Output("b"), Output("other", true)], -1));
    }
    [Fact] public void VolumeAndCommunicationsArePartOfMatch()
    {
        Assert.False(AudioMatching.Matches(new("a", "A", new("a", 30)), [Output("a", true, 50)]));
        Assert.False(AudioMatching.Matches(new("a", "A", new("a"), CommunicationsOutput: "a"), [Output("a", true)]));
        Assert.True(AudioMatching.Matches(new("a", "A", new("a", 50)), [Output("a", true)]));
    }
    [Fact] public void DeviceNamesDoNotDecideMatching()
    {
        Assert.False(AudioMatching.Available(new("a", "A", new("saved-id")), [Output("new-id") with { Name = "saved-id" }]));
    }
}
