using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SetupPulse.App;

internal static class WindowTheme
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public static bool IsDark(string theme)
    {
        if (theme != "system") return theme == "dark";
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
    public static void Apply(IntPtr window, bool dark)
    {
        void Set(int attribute, int value) => DwmSetWindowAttribute(window, attribute, ref value, sizeof(int));
        // Preserve system contrast settings; unsupported appearance attributes gracefully fall back.
        if (SystemInformation.HighContrast) { Set(20, 0); Set(34, -1); Set(35, -1); Set(36, -1); return; }
        Set(20, dark ? 1 : 0);
        Set(33, 2); // Windows 11 rounded frame, with native resize/Snap/caption buttons.
        Set(34, dark ? 0x303030 : 0xe5e5e5);
        Set(35, dark ? 0x171717 : 0xfafafa);
        Set(36, dark ? 0xfafafa : 0x252525);
    }
}
