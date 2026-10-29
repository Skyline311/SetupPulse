using System.Reflection;

namespace SetupPulse.App;

internal static class TrayIconArtwork
{
    // The user-selected Pulse/arrows SVG, pre-rendered into native ICO frames.
    // Exact small-size frames keep the pulse legible without scaling down 64px.
    internal static Bitmap Render(int size)
    {
        using var stream = Open();
        using var icon = new Icon(stream, new Size(size, size));
        return icon.ToBitmap();
    }

    public static Icon Create()
    {
        using var stream = Open();
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    static Stream Open() => Assembly.GetExecutingAssembly().GetManifestResourceStream("TrayIcon")
        ?? throw new InvalidDataException("Das Tray-Icon fehlt im App-Paket.");
}
