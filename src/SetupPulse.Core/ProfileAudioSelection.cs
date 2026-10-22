namespace SetupPulse.Core;

public static class ProfileAudioSelection
{
    // Inline selections reuse the engine's audio operation without creating a saved favorite.
    public static AudioFavorite? Resolve(SetupProfile profile, Settings settings)
    {
        if (profile.Audio is { } audio)
            return new($"setup:{profile.Id}", string.IsNullOrWhiteSpace(audio.Name) ? profile.Name : audio.Name, audio.Output, audio.Input);
        return settings.AudioFavorites.FirstOrDefault(f => f.Id == profile.AudioFavoriteId);
    }
}
