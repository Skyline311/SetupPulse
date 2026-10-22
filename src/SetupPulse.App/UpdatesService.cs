using System.Net.Http;
using SetupPulse.Core;

namespace SetupPulse.App;

internal record UpdatePreferences(bool Automatic = true, DateTimeOffset? LastChecked = null);
internal record UpdateView(string CurrentVersion, string Status, string Message, bool Automatic = true,
    DateTimeOffset? LastChecked = null, string? LatestVersion = null, string? ReleaseUrl = null, int? Progress = null);
internal record UpdateResult(string Status, string Message, string Version, DateTimeOffset At);
internal record PreparedUpdate(string Folder, UpdateRelease Release);

internal sealed class UpdatesService : IDisposable
{
    readonly Store store;
    readonly Action changed;
    readonly HttpClient http;
    readonly GitHubUpdates github;
    readonly object sync = new();
    UpdatePreferences preferences;
    UpdateView view;
    UpdateRelease? release;
    int operation;
    public UpdatesService(Store store, string currentVersion, Action changed, HttpClient? http = null)
    {
        this.store = store; this.changed = changed;
        this.http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        github = new(this.http);
        try { preferences = store.Read<UpdatePreferences>("updates.json") ?? new(); } catch (Exception) { preferences = new(); }
        view = new(UpdateProtocol.CurrentVersion(currentVersion), "idle", "Noch nicht nach Updates gesucht.", preferences.Automatic, preferences.LastChecked);
        try
        {
            if (store.Read<UpdateResult>("update-result.json") is { } result &&
                (result.Status == "installed" && result.Version == view.CurrentVersion ||
                 result.Status == "error" && UpdateProtocol.Version(result.Version) > UpdateProtocol.Version(view.CurrentVersion)))
                view = view with { Status = result.Status, Message = result.Message };
        }
        catch (Exception) { }
    }
    public UpdateView View => Volatile.Read(ref view);
    public bool ShouldCheckAtStartup => preferences.Automatic && (preferences.LastChecked == null
        || DateTimeOffset.UtcNow - preferences.LastChecked >= TimeSpan.FromHours(24));
    void Set(UpdateView next) { Volatile.Write(ref view, next); changed(); }
    public UpdateView Automatic(bool enabled)
    {
        lock (sync)
        {
            if (operation != 0) throw new InvalidOperationException("Bitte die laufende Update-Prüfung abschließen.");
            var incoming = preferences with { Automatic = enabled }; store.Write("updates.json", incoming); preferences = incoming;
            Set(View with { Automatic = enabled }); return View;
        }
    }
    void Begin()
    {
        lock (sync) { if (operation != 0) throw new InvalidOperationException("Eine Update-Aktion läuft bereits."); operation = 1; }
    }
    void End() { lock (sync) operation = 0; }
    public async Task<UpdateView> Check(CancellationToken token)
    {
        Begin(); Set(View with { Status = "checking", Message = "Suche nach Updates …", Progress = null });
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
            release = await github.Latest(deadline.Token);
            preferences = preferences with { LastChecked = DateTimeOffset.UtcNow }; store.Write("updates.json", preferences);
            bool newer = release != null && UpdateProtocol.Version(release.Version) > UpdateProtocol.Version(View.CurrentVersion);
            Set(View with { Status = release == null ? "unpublished" : newer ? "available" : "current",
                Message = release == null ? "Noch kein öffentliches Release verfügbar. Das Repository kann noch privat sein."
                    : newer ? "Version " + release!.Version + " ist verfügbar." : "Du verwendest die aktuelle Version.",
                LastChecked = preferences.LastChecked, LatestVersion = newer ? release!.Version : null, ReleaseUrl = newer ? release!.PageUrl : null });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) { Set(View with { Status = "error", Message = e is InvalidDataException ? e.Message : "Die Update-Prüfung ist fehlgeschlagen. Prüfe die Internetverbindung oder versuche es später erneut." }); }
        finally { End(); }
        return View;
    }
    public async Task<PreparedUpdate> Download(CancellationToken token)
    {
        Begin(); string? folder = null;
        try
        {
            var selected = release;
            if (selected == null || UpdateProtocol.Version(selected.Version) <= UpdateProtocol.Version(View.CurrentVersion))
                throw new InvalidOperationException("Bitte zuerst nach einer neueren Version suchen.");
            string root = store.FilePath("Updates"); UpdatePaths.NoLinks(root); Directory.CreateDirectory(root);
            folder = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            Set(View with { Status = "downloading", Message = "Update wird heruntergeladen und geprüft …", Progress = 0 });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(15));
            await github.Download(selected, folder, percent => Set(View with { Progress = percent }), deadline.Token);
            Set(View with { Status = "installing", Message = "Dateien geprüft. SetupPulse wird neu gestartet …", Progress = 100 });
            return new(folder, selected);
        }
        catch (Exception)
        {
            if (folder != null) try { UpdatePaths.DeleteTree(folder, store.FilePath("Updates")); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            Set(View with { Status = "error", Message = "Das Update konnte nicht vorbereitet werden. Die installierte Version bleibt erhalten.", Progress = null }); throw;
        }
        finally { End(); }
    }
    public void Failed(string message) => Set(View with { Status = "error", Message = message, Progress = null });
    public void Dispose() => http.Dispose();
}
