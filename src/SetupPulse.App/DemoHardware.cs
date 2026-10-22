using SetupPulse.Core;
namespace SetupPulse.App;

internal sealed class DemoHardware : IHardware
{
    List<DisplaySetting> displays = [new("demo-monitor", "Demo Monitor", 2560, 1440, 0, 0, 1, 60, 100, true)];
    List<AudioDevice> audio = [new("demo-speakers", "Demo Lautsprecher", false, true, true, true, 50, false), new("demo-headset", "Demo Headset", false, true, false, false, 30, false), new("demo-mic", "Demo Mikrofon", true, true, true, true, 70, false)];
    public Inventory Inventory() => new(displays.Select(d => new DisplayDevice(d.DeviceId, d.Name, true, true, d)).ToList(), audio.ToList());
    public List<AudioDevice> AudioInventory() => audio.ToList();
    public void ValidateDisplays(List<DisplaySetting> d) => Validation.Displays(d);
    public void ApplyDisplays(List<DisplaySetting> d) => displays = d.ToList();
    public void VerifyDisplays(List<DisplaySetting> d) { }
    public void ApplyAudio(AudioFavorite f) { audio = audio.Select(d => d with { Default = (d.Input ? f.Input?.DeviceId : f.Output?.DeviceId) is string id ? d.Id == id : d.Default, Volume = (d.Input ? f.Input : f.Output) is { } s && s.DeviceId == d.Id ? s.Volume ?? d.Volume : d.Volume }).ToList(); }
    public void RestoreAudio(List<AudioDevice> d) => audio = d.ToList();
}
