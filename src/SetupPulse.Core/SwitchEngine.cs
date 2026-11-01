namespace SetupPulse.Core;

public sealed class SwitchEngine(IHardware hardware, Store store, Action<string> startWatchdog, bool? liveAlignmentEnabled = null)
{
    readonly SemaphoreSlim gate = new(1, 1);
    public bool Busy { get; private set; }
    public Transaction? Pending => store.Read<Transaction>("transaction.json") is { Phase: "pending" } t ? t : null;
    public Transaction BeginAlignment() => AlignmentLocked(() =>
    {
        if (Pending != null) throw new InvalidOperationException("Bitte den laufenden Monitortest bestätigen oder zurücksetzen.");
        var inventory = hardware.Inventory();
        var original = inventory.Displays.Where(d => d.Active).Select(d => d.Current ?? throw new InvalidOperationException("Monitorwerte fehlen.")).ToList();
        if (original.Count < 2) throw new InvalidOperationException("Für den Live-Abgleich müssen mindestens zwei Monitore aktiv sein.");
        Validation.Displays(original);
        var transaction = new Transaction(Guid.NewGuid().ToString("N"), "pending", DateTimeOffset.UtcNow.AddSeconds(90), original, inventory.Audio, null, Validation.MonitorHash(original), RequestedDisplays: original, LiveAlignment: true);
        store.Write("transaction.json", transaction);
        try { startWatchdog(transaction.Id); }
        catch (Exception error) { store.Write("transaction.json", transaction with { Phase = "failed", Error = error.Message }); throw; }
        return transaction;
    });
    public Transaction UpdateAlignment(string id, List<DisplaySetting> requested) => AlignmentLocked(() =>
    {
        var transaction = Pending ?? throw new InvalidOperationException("Kein Live-Abgleich aktiv.");
        if (!transaction.LiveAlignment || transaction.Id != id || DateTimeOffset.UtcNow >= transaction.Deadline) throw new InvalidOperationException("Live-Abgleich ist nicht mehr aktiv.");
        var original = transaction.Displays ?? throw new InvalidOperationException("Ursprüngliche Anordnung fehlt.");
        Validation.Displays(requested);
        if (requested.Count != original.Count || requested.Any(target => !original.Any(before => before == target with { Y = before.Y })))
            throw new InvalidDataException("Im Live-Abgleich darf nur die Y-Position der bestehenden Monitore geändert werden.");
        hardware.ValidateDisplayPositions(requested);
        var updated = transaction with { RequestedDisplays = requested, Hash = Validation.MonitorHash(requested), Deadline = DateTimeOffset.UtcNow.AddSeconds(90) };
        store.Write("transaction.json", updated);
        try { hardware.ApplyDisplayPositions(requested); hardware.VerifyDisplays(requested); }
        catch { Rollback(updated); throw; }
        return updated;
    });
    T AlignmentLocked<T>(Func<T> action)
    {
        if (!(liveAlignmentEnabled ?? ProductFeatures.LiveMonitorAlignment))
            throw new NotSupportedException("Der Live-Höhenabgleich ist vorübergehend deaktiviert. Positionen können im Setup über X/Y gespeichert werden.");
        if (!gate.Wait(0)) throw new InvalidOperationException("Ein Wechsel läuft bereits.");
        Busy = true;
        try { return action(); }
        finally { Busy = false; gate.Release(); }
    }
    public async Task<Transaction?> Apply(SetupProfile? profile, AudioFavorite? favorite, CancellationToken token = default)
    {
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("Ein Wechsel läuft bereits.");
        Busy = true;
        try
        {
            if (Pending != null) throw new InvalidOperationException("Bitte den laufenden Monitortest bestätigen oder zurücksetzen.");
            var displays = profile?.Monitors;
            var inventory = displays == null ? new Inventory([], hardware.AudioInventory()) : hardware.Inventory();
            if (displays != null) Validation.Displays(displays);
            var before = displays == null ? null : inventory.Displays.Where(x => x.Active).Select(x => x.Current!).ToList();
            bool changeDisplays = displays != null && !SameDisplaySettings(displays, before!);
            // Re-selecting the current profile must not rebuild native signal timings or DPI.
            if (changeDisplays) hardware.ValidateDisplays(displays!);
            var hash = displays == null ? null : Validation.MonitorHash(displays);
            bool needsTest = displays != null && profile!.VerifiedHash != hash;
            // All display transitions are guarded, including previously verified profiles.
            // Only snapshot displays for rollback when this transaction will actually change them.
            var transaction = new Transaction(Guid.NewGuid().ToString("N"), "pending", DateTimeOffset.UtcNow.AddSeconds(90), changeDisplays ? before : null, inventory.Audio, profile?.Id, hash, RequestedDisplays: displays, Applying: true);
            if (before != null && store.Read<Transaction>("last-good.json") == null) store.Write("last-good.json", transaction with { Phase = "confirmed", Applying = false, Displays = before, ProfileId = null, Hash = Validation.MonitorHash(before) });
            store.Write("transaction.json", transaction);
            bool mutated = false;
            try
            {
                startWatchdog(transaction.Id);
                if (changeDisplays) { mutated = true; hardware.ApplyDisplays(displays!); hardware.VerifyDisplays(displays!); }
                if (favorite != null)
                {
                    var until = DateTimeOffset.UtcNow.AddSeconds(displays != null ? 15 : 0);
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        try { mutated = true; hardware.ApplyAudio(favorite); break; }
                        catch (DeviceUnavailableException) when (DateTimeOffset.UtcNow < until) { await Task.Delay(500, token); }
                        catch (DeviceUnavailableException e) when (displays != null)
                        {
                            // HDMI audio can remain unavailable even after its display is ready.
                            // Keep the display transition and saved audio selection independent.
                            transaction = transaction with { Warning = $"Monitorwechsel fortgesetzt. Die gespeicherte Audioauswahl konnte nicht angewendet werden: {e.Message}" };
                            break;
                        }
                    }
                }
                if (needsTest)
                {
                    var ready = transaction with { Applying = false, Deadline = DateTimeOffset.UtcNow.AddSeconds(90) };
                    store.Write("transaction.json", ready);
                    return ready;
                }
                var completed = transaction with { Phase = "confirmed", Applying = false };
                store.Write("transaction.json", completed);
                if (displays != null) store.Write("last-good.json", completed with { Displays = displays, Audio = hardware.AudioInventory() });
                return null;
            }
            catch (Exception e)
            {
                if (!mutated) { store.Write("transaction.json", transaction with { Phase = "failed", Error = e.Message }); throw; }
                try { Rollback(transaction with { Error = e.Message }); } catch (Exception rollback) { throw new AggregateException("Wechsel und Wiederherstellung fehlgeschlagen.", e, rollback); }
                throw;
            }
        }
        finally { Busy = false; gate.Release(); }
    }
    static bool SameDisplaySettings(List<DisplaySetting> requested, List<DisplaySetting> current)
        => requested.Count == current.Count && requested.All(target => current.Any(actual =>
            actual != null && actual.DeviceId.Equals(target.DeviceId, StringComparison.OrdinalIgnoreCase)
            && actual.Width == target.Width && actual.Height == target.Height
            && actual.X == target.X && actual.Y == target.Y && actual.Rotation == target.Rotation
            && actual.Scale == target.Scale && actual.Primary == target.Primary
            // Inventory rounds native refresh rates to three decimals; do not conflate59.94 and60Hz.
            && double.IsFinite(actual.RefreshRate) && double.IsFinite(target.RefreshRate)
            && Math.Round(actual.RefreshRate * 1000) == Math.Round(target.RefreshRate * 1000)));
    public Transaction Confirm(string id)
    {
        var t = Pending ?? throw new InvalidOperationException("Kein Monitortest aktiv.");
        if (Busy || t.Applying) throw new InvalidOperationException("Der Monitorwechsel wird noch angewendet. Bitte warten.");
        if (t.Id != id || DateTimeOffset.UtcNow >= t.Deadline) throw new InvalidOperationException("Monitortest abgelaufen.");
        var current = hardware.Inventory();
        var displays = current.Displays.Where(x => x.Active).Select(x => x.Current!).ToList();
        hardware.VerifyDisplays(t.RequestedDisplays ?? throw new InvalidOperationException("Testdaten fehlen."));
        store.Write("transaction.json", t with { Phase = "confirmed" });
        store.Write("last-good.json", t with { Displays = displays, Audio = current.Audio });
        return t;
    }
    public bool RollbackIfExpired(string id)
    {
        var current = Pending;
        if (current == null || current.Id != id || DateTimeOffset.UtcNow < current.Deadline) return false;
        Rollback(current); return true;
    }
    public void Rollback(Transaction t)
    {
        // Coordinator uses a cross-process mutex around this method and confirmation.
        var current = store.Read<Transaction>("transaction.json");
        if (current?.Id != t.Id || current.Phase != "pending") return;
        var errors = new List<Exception>();
        if (t.Displays != null) try { if (t.LiveAlignment) hardware.ApplyDisplayPositions(t.Displays); else hardware.ApplyDisplays(t.Displays); hardware.VerifyDisplays(t.Displays); } catch (Exception e) { errors.Add(e); }
        if (!t.LiveAlignment) try
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(15);
            bool Missing(Exception e) => e is DeviceUnavailableException || e is AggregateException a && a.InnerExceptions.Any(Missing);
            while (true)
            {
                try { hardware.RestoreAudio(t.Audio); break; }
                catch (Exception e) when (Missing(e) && DateTimeOffset.UtcNow < until) { Thread.Sleep(500); }
            }
        }
        catch (Exception e) { errors.Add(e); }
        var messages = new[] { t.Error }.Where(x => !string.IsNullOrWhiteSpace(x)).Concat(errors.Select(x => x.Message));
        store.Write("transaction.json", t with { Phase = errors.Count == 0 ? "rolledBack" : "failed", Error = string.Join("; ", messages) });
        if (errors.Count != 0) throw new AggregateException(errors);
    }
    public void Rescue()
    {
        if (Pending is { } pending) { Rollback(pending); return; }
        var good = store.Read<Transaction>("last-good.json") ?? throw new InvalidOperationException("Noch keine sichere Konfiguration gespeichert.");
        if (good.Displays != null) { hardware.ValidateDisplays(good.Displays); hardware.ApplyDisplays(good.Displays); hardware.VerifyDisplays(good.Displays); }
        hardware.RestoreAudio(good.Audio);
    }
}
public sealed class DeviceUnavailableException(string message) : Exception(message);
