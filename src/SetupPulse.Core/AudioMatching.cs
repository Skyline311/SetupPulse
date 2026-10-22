namespace SetupPulse.Core;

public static class AudioMatching
{
    public static bool Available(AudioFavorite favorite, List<AudioDevice> devices)
    {
        return new[] { (favorite.Output?.DeviceId, false), (favorite.Input?.DeviceId, true), (favorite.CommunicationsOutput, false), (favorite.CommunicationsInput, true) }
            .All(pair => pair.Item1 == null || devices.Any(d => d.Id == pair.Item1 && d.Input == pair.Item2 && d.Available));
    }
    public static bool Matches(AudioFavorite favorite, List<AudioDevice> devices)
    {
        bool Endpoint(EndpointSetting? s, bool input) => s == null || devices.Any(d => d.Id == s.DeviceId && d.Input == input && d.Available && d.Default && (s.Volume == null || Math.Abs(d.Volume - s.Volume.Value) <= 1) && (s.Muted == null || d.Muted == s.Muted));
        bool Communications(string? id, bool input) => id == null || devices.Any(d => d.Id == id && d.Input == input && d.Available && d.Communications);
        return Endpoint(favorite.Output, false) && Endpoint(favorite.Input, true) && Communications(favorite.CommunicationsOutput, false) && Communications(favorite.CommunicationsInput, true);
    }
    public static AudioFavorite Cycle(List<AudioFavorite> favorites, List<AudioDevice> devices, int direction)
    {
        var choices = favorites.Where(f => Available(f, devices)).ToList();
        if (choices.Count == 0) throw new DeviceUnavailableException("Keine verfügbaren Audiofavoriten.");
        int current = choices.FindIndex(f => Matches(f, devices));
        int index = current < 0 ? (direction > 0 ? 0 : choices.Count - 1) : (current + direction + choices.Count) % choices.Count;
        return choices[index];
    }
}
