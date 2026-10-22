namespace SetupPulse.App;

internal sealed class TrayClickActions : IDisposable
{
    readonly System.Windows.Forms.Timer singleClick = new();
    readonly Action openMain;
    bool doubleClickRelease;

    public TrayClickActions(Action openQuickMixer, Action openMain)
    {
        this.openMain = openMain;
        singleClick.Tick += (_, _) => { singleClick.Stop(); openQuickMixer(); };
    }

    public void MouseClick(MouseButtons button)
    {
        Cancel();
        if (button != MouseButtons.Left || doubleClickRelease) return;
        // Wait for Windows to distinguish a single click from the first click of a double-click.
        singleClick.Interval = SystemInformation.DoubleClickTime + 1;
        singleClick.Start();
    }

    public void MouseDoubleClick(MouseButtons button)
    {
        Cancel();
        if (button == MouseButtons.Left) { doubleClickRelease = true; openMain(); }
    }

    public void MouseDown(MouseButtons button, int clicks)
    {
        // A second press must cancel the first click even while its button is held.
        Cancel();
        // NotifyIcon raises MouseDoubleClick before MouseDown(clicks: 2).
        if (button != MouseButtons.Left || clicks == 1) doubleClickRelease = false;
    }

    public void Cancel() => singleClick.Stop();
    public void Dispose() => singleClick.Dispose();
}
