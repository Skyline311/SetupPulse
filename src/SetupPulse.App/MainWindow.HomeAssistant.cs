using SetupPulse.Core;

namespace SetupPulse.App;

internal sealed partial class MainWindow
{
    HomeAssistantSettingsStore homeAssistantSettings = null!;
    HomeAssistantDocument homeAssistantDocument = null!;
    HomeAssistantMqtt? homeAssistant;
    HomeAssistantSnapshot homeAssistantSnapshot = new([], null, false);
    string? homeAssistantLoadError;
    bool savingHomeAssistant;

    void InitializeHomeAssistantSettings()
    {
        homeAssistantSettings = new(store);
        try { homeAssistantDocument = homeAssistantSettings.Load(); }
        catch (Exception)
        {
            homeAssistantDocument = new(new(InstanceId: Guid.NewGuid().ToString("N")));
            homeAssistantLoadError = "Die MQTT-Einstellungen konnten nicht gelesen werden. Bitte die Verbindung neu einrichten.";
        }
        homeAssistantSnapshot = new(HomeAssistantProtocol.Choices(settings), null, false);
    }
    object HomeAssistantView() => new {
        options = homeAssistantDocument.Options,
        passwordSaved = homeAssistantDocument.ProtectedPassword != null,
        connection = homeAssistantLoadError != null ? new HomeAssistantConnection(false, "Einrichtung prüfen", homeAssistantLoadError)
            : homeAssistant?.Connection ?? new(false, demo && homeAssistantDocument.Options.Enabled ? "Demo – keine Verbindung" : "Deaktiviert"),
        availableSetups = homeAssistantSnapshot.Choices.Select(choice => choice.Label).ToList()
    };
    void NotifyHomeAssistant()
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(() => { if (!IsDisposed) Post(new { @event = "homeAssistant", data = HomeAssistantView() }); }); }
        catch (InvalidOperationException) { }
    }
    void StartHomeAssistant()
    {
        if (demo || !homeAssistantDocument.Options.Enabled || homeAssistantLoadError != null) return;
        try
        {
            HomeAssistantMqtt? created = null;
            created = new(homeAssistantDocument.Options, HomeAssistantSettingsStore.Password(homeAssistantDocument),
                typeof(MainWindow).Assembly.GetName().Version!.ToString(3), ActivateHomeAssistant,
                () => { if (ReferenceEquals(homeAssistant, created)) NotifyHomeAssistant(); }, ActivateHomeAssistantAudio);
            homeAssistant = created;
            created.Update(homeAssistantSnapshot);
        }
        catch (Exception)
        {
            homeAssistantLoadError = "Die MQTT-Verbindung konnte nicht gestartet werden. Bitte Zugangsdaten erneut eingeben und speichern.";
        }
    }
    async Task<object> SaveHomeAssistant(HomeAssistantSaveRequest request)
    {
        if (busy || engine.Pending != null || savingHomeAssistant)
            throw new InvalidOperationException("Bitte den laufenden Wechsel oder Speichervorgang zuerst abschließen.");
        savingHomeAssistant = true;
        try
        {
            var document = homeAssistantSettings.Save(homeAssistantDocument, request);
            if (homeAssistant != null) await homeAssistant.StopAsync();
            homeAssistant = null; homeAssistantDocument = document; homeAssistantLoadError = null;
            StartHomeAssistant(); NotifyHomeAssistant();
            return HomeAssistantView();
        }
        finally { savingHomeAssistant = false; }
    }
    void UpdateHomeAssistantState(Inventory inventory, Transaction? transaction)
    {
        string previousChoices = Json.Encode(homeAssistantSnapshot.Choices);
        homeAssistantSnapshot = HomeAssistantProtocol.Snapshot(settings, inventory, busy || engine.Busy, transaction, activeProfile);
        homeAssistant?.Update(homeAssistantSnapshot);
        if (Json.Encode(homeAssistantSnapshot.Choices) != previousChoices) NotifyHomeAssistant();
    }
    async Task ActivateHomeAssistant(string id, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(() => completion.TrySetCanceled(cancellation));
        if (!IsHandleCreated || IsDisposed) throw new InvalidOperationException("SetupPulse wird beendet.");
        BeginInvoke(async () => {
            if (cancellation.IsCancellationRequested) return;
            try
            {
                var profile = settings.Profiles.SingleOrDefault(p => p.Id == id);
                if (profile == null || !HomeAssistantProtocol.CanActivate(profile))
                    throw new InvalidOperationException("Dieses Setup muss zuerst am PC getestet und bestätigt werden.");
                if (busy || engine.Pending != null) throw new InvalidOperationException("Ein Wechsel läuft bereits.");
                await ApplyProfile(id);
                await SendState();
                completion.TrySetResult();
            }
            catch (Exception e) { await SendState(); completion.TrySetException(e); }
        });
        await completion.Task;
    }
    async Task ActivateHomeAssistantAudio(string kind, string id, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(() => completion.TrySetCanceled(cancellation));
        if (!IsHandleCreated || IsDisposed) throw new InvalidOperationException("SetupPulse wird beendet.");
        BeginInvoke(async () => {
            if (cancellation.IsCancellationRequested) return;
            try
            {
                if (busy || engine.Pending != null) throw new InvalidOperationException("Ein Wechsel läuft bereits.");
                var favorite = HomeAssistantProtocol.AudioSelection(settings, hardware.AudioInventory(), kind, id);
                await Run(() => engine.Apply(null, favorite, cancellation).GetAwaiter().GetResult());
                await SendState();
                completion.TrySetResult();
            }
            catch (Exception e) { await SendState(); completion.TrySetException(e); }
        });
        await completion.Task;
    }
}
