using SetupPulse.Core;

namespace SetupPulse.Tests;

public sealed class ShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Num0", 0x60u)]
    [InlineData("Ctrl+Alt+Num2", 0x62u)]
    [InlineData("Strg+Alt+Numpad2", 0x62u)]
    [InlineData("Ctrl+Alt+Num9", 0x69u)]
    public void NumberPadShortcutsUseNumberPadVirtualKeys(string shortcut, uint virtualKey)
    {
        var (modifiers, key) = Shortcut.Parse(shortcut);
        Assert.Equal(3u, modifiers);
        Assert.Equal(virtualKey, key);
    }

    [Fact]
    public void NumberRowAndNumberPadCanTriggerDifferentFavorites()
    {
        var settings = Settings.Empty with
        {
            AudioFavorites =
            [
                new("row", "Speakers", new("speakers"), Shortcut: "Ctrl+Alt+2"),
                new("pad", "Headset", new("headset"), Shortcut: "Ctrl+Alt+Num2")
            ]
        };
        Validation.Settings(settings);
        Assert.NotEqual(Shortcut.Normalize("Ctrl+Alt+2"), Shortcut.Normalize("Ctrl+Alt+Num2"));
        Assert.Throws<InvalidDataException>(() => Validation.Settings(settings with
        {
            AudioFavorites = [settings.AudioFavorites[1], new("alias", "Other", new("other"), Shortcut: "Strg+Alt+Numpad2")]
        }));
    }

    [Theory]
    [InlineData("Ctrl+Alt+Num10")]
    [InlineData("Ctrl+Alt+Num")]
    [InlineData("Ctrl+Alt+NumpadX")]
    public void InvalidNumberPadKeysAreRejected(string shortcut) =>
        Assert.Throws<InvalidDataException>(() => Shortcut.Parse(shortcut));
}
