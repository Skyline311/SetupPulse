using System.Reflection;
using System.Runtime.InteropServices;

namespace SetupPulse.App;

// Hosts the existing WebView while the compact mixer is open; no second browser.
internal sealed class TrayMixerWindow : Form
{
    public TrayMixerWindow(Action dismiss)
    {
        Text = "SetupPulse · Schnellmixer";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false; TopMost = true;
        MinimizeBox = false; MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(23, 23, 23);
        FormClosing += (_, e) => { e.Cancel = true; dismiss(); };
        Deactivate += (_, _) => { if (Visible) dismiss(); };
    }

    public void PositionAtTray(NotifyIcon icon)
    {
        var anchor = IconBounds(icon);
        var area = (anchor is { } bounds ? Screen.FromRectangle(bounds) : Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
        float scale = DeviceDpi / 96f;
        ClientSize = new(Math.Min((int)(460 * scale), Math.Max(1, area.Width - 24)),
            Math.Min((int)(600 * scale), Math.Max(1, area.Height - 64)));
        Location = new(Math.Clamp((anchor?.Right ?? area.Right) - Width - 12, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Max(area.Top, area.Bottom - Height - 12));
    }

    static Rectangle? IconBounds(NotifyIcon icon)
    {
        // Same verified .NET 10 identity as TrayVolumeScroll; query only our own icon.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        if (typeof(NotifyIcon).Assembly.GetName().Version?.Major != 10) return null;
        try
        {
            if (typeof(NotifyIcon).GetField("_id", flags)?.GetValue(icon) is not uint id ||
                typeof(NotifyIcon).GetField("_window", flags)?.GetValue(icon) is not NativeWindow window || window.Handle == IntPtr.Zero) return null;
            var identifier = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = window.Handle, Id = id };
            return Shell_NotifyIconGetRect(in identifier, out var rect) >= 0
                ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
        }
        catch (MemberAccessException) { return null; }
    }
    [StructLayout(LayoutKind.Sequential)] struct IconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(in IconIdentifier identifier, out Rect rectangle);
}
