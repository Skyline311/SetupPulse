namespace SetupPulse.Core;

public static class ProfileMatching
{
    public static bool Matches(SetupProfile profile, Settings settings, Inventory inventory)
    {
        if (profile.Monitors is { } expected)
        {
            var actual = inventory.Displays.Where(d => d.Active).ToList();
            if (expected.Count == 0 || actual.Count != expected.Count) return false;
            foreach (var target in expected)
            {
                var current = actual.FirstOrDefault(d => d.Id.Equals(target.DeviceId, StringComparison.OrdinalIgnoreCase))?.Current;
                if (current == null || current.Width != target.Width || current.Height != target.Height || current.X != target.X || current.Y != target.Y || current.Rotation != target.Rotation || current.Scale != target.Scale || current.Primary != target.Primary || Math.Abs(current.RefreshRate - target.RefreshRate) > .15) return false;
            }
            // Linked audio is applied on activation; a later independent audio switch keeps this layout active.
            return true;
        }
        if (profile.AudioFavoriteId != null || profile.Audio != null)
        {
            var favorite = ProfileAudioSelection.Resolve(profile, settings);
            return favorite != null && AudioMatching.Matches(favorite, inventory.Audio);
        }
        return false;
    }
}
