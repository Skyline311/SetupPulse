using System.Net;
using System.Net.Http;
using SetupPulse.Core;

namespace SetupPulse.App;

internal sealed class GitHubUpdates(HttpClient client)
{
    static HttpRequestMessage Request(string url, string accept) => new(HttpMethod.Get, url) {
        Headers = { { "User-Agent", "SetupPulse-Updater" }, { "Accept", accept }, { "X-GitHub-Api-Version", "2026-03-10" } }
    };
    public async Task<UpdateRelease?> Latest(CancellationToken token)
    {
        using var request = Request(UpdateProtocol.LatestUrl, "application/vnd.github+json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if ((int)response.StatusCode is 403 or 429) throw new IOException("GitHub begrenzt gerade die Anfragen. Bitte später erneut prüfen.");
        response.EnsureSuccessStatusCode();
        using var memory = new MemoryStream(); await Copy(response, memory, 1_000_000, null, null, token);
        return UpdateProtocol.Parse(System.Text.Encoding.UTF8.GetString(memory.ToArray()));
    }
    static bool DownloadHost(Uri uri) => uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0
        && uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    async Task<HttpResponseMessage> DownloadResponse(string url, CancellationToken token)
    {
        var uri = new Uri(url);
        for (int redirect = 0; redirect < 6; redirect++)
        {
            if (!DownloadHost(uri)) throw new InvalidDataException("Unerwarteter Update-Downloadserver.");
            using var request = Request(uri.AbsoluteUri, "application/octet-stream");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location == null) throw new IOException("GitHub lieferte keine Download-Adresse.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue;
            }
            if (!response.IsSuccessStatusCode) { response.Dispose(); throw new IOException("Das Update konnte nicht von GitHub geladen werden."); }
            return response;
        }
        throw new IOException("Zu viele Download-Weiterleitungen.");
    }
    public async Task Download(UpdateRelease release, string folder, Action<int> progress, CancellationToken token)
    {
        using var checksumResponse = await DownloadResponse(release.ChecksumUrl, token);
        using var checksum = new MemoryStream(); await Copy(checksumResponse, checksum, 1024, null, null, token);
        string hash = UpdateProtocol.PackageHash(System.Text.Encoding.UTF8.GetString(checksum.ToArray()), release.PackageName);
        if (release.Digest != null && !hash.Equals(release.Digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub- und Paket-Prüfsumme stimmen nicht überein.");
        using var packageResponse = await DownloadResponse(release.PackageUrl, token);
        string zip = Path.Combine(folder, release.PackageName);
        await using (var file = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await Copy(packageResponse, file, UpdateProtocol.MaxPackageSize, release.PackageSize, progress, token);
        token.ThrowIfCancellationRequested();
        await Task.Run(() => UpdatePackage.Extract(zip, hash, Path.Combine(folder, "package")), token);
    }
    static async Task Copy(HttpResponseMessage response, Stream target, long maximum, long? expected, Action<int>? progress, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is long length && (length > maximum || expected != null && length != expected))
            throw new InvalidDataException("Unerwartete Download-Größe.");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[81920]; long total = 0; int count, lastProgress = -1;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            total += count;
            if (total > maximum || expected != null && total > expected) throw new InvalidDataException("Update-Download ist zu groß.");
            await target.WriteAsync(buffer.AsMemory(0, count), token);
            if (expected is > 0 && (int)(total * 100 / expected.Value) is int percent && percent != lastProgress)
            { lastProgress = percent; progress?.Invoke(percent); }
        }
        if (expected != null && total != expected) throw new InvalidDataException("Der Update-Download ist unvollständig.");
    }
}
