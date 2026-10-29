using SetupPulse.Core;

namespace SetupPulse.App;

internal static class StartupBehavior
{
    public const string AutostartArgument = "--autostart";

    // Manual launches open the main window; --tray remains an explicit override
    // for existing shortcuts and the updater.
    public static bool StartHidden(string[] args, Preferences preferences) =>
        args.Contains("--tray") || (args.Contains(AutostartArgument) && preferences.StartMinimized);
}
