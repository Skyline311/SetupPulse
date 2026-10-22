using SetupPulse.Core;

namespace SetupPulse.App;

internal sealed partial class MainWindow
{
    UpdatesService updates = null!;
    void InitializeUpdates() => updates = new(store, typeof(MainWindow).Assembly.GetName().Version!.ToString(3), NotifyUpdates);
    object UpdatesView() => demo ? updates.View with { Status = "demo", Message = "Demo – keine Update-Verbindung." } : updates.View;
    void NotifyUpdates()
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(() => { if (!IsDisposed) Post(new { @event = "updates", data = UpdatesView() }); }); }
        catch (InvalidOperationException) { }
    }
    async Task StartUpdateCheck()
    {
        if (demo) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), shutdown.Token);
            await Task.Run(UpdateInstaller.Cleanup, shutdown.Token);
            if (updates.ShouldCheckAtStartup)
            {
                var view = await updates.Check(shutdown.Token);
                if (view.Status == "available") tray.ShowBalloonTip(6000, "SetupPulse-Update verfügbar", "Version " + view.LatestVersion + " ist verfügbar. Öffne Einstellungen → Updates zum Aktualisieren.", ToolTipIcon.Info);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }
    async Task<object> CheckUpdate()
    {
        if (demo) return UpdatesView();
        if (busy || engine.Pending != null) throw new InvalidOperationException("Bitte den laufenden Wechsel zuerst abschließen.");
        return await updates.Check(shutdown.Token);
    }
    async Task<object> InstallUpdate()
    {
        if (demo) throw new InvalidOperationException("In der Demo werden keine Updates installiert.");
        if (busy || engine.Busy || engine.Pending != null) throw new InvalidOperationException("Bitte den laufenden Wechsel oder Monitortest zuerst abschließen.");
        busy = true; Post(new { @event = "busy", data = true, scope = "switch" });
        homeAssistant?.Update(homeAssistantSnapshot with { Busy = true, CurrentId = null });
        PreparedUpdate? prepared = null;
        try
        {
            prepared = await updates.Download(shutdown.Token);
            await UpdateInstaller.Prepare(prepared, shutdown.Token);
            // OnMessage posts the response before closing the old process. The helper already waits for its exit.
            return UpdatesView();
        }
        catch (Exception error)
        {
            if (prepared != null) try { await Task.Run(() => UpdatePaths.DeleteTree(prepared.Folder, store.FilePath("Updates"))); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            busy = false; Post(new { @event = "busy", data = false, scope = "switch" });
            updates.Failed(error is InvalidDataException ? error.Message
                : "Das Update konnte nicht gestartet werden. Die bisherige Version bleibt geöffnet. Prüfe Internetverbindung und Schreibrechte oder versuche es erneut.");
            await SendState(); throw new IOException(updates.View.Message);
        }
    }
}
