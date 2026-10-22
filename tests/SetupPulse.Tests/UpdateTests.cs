using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using SetupPulse.App;
using SetupPulse.Core;

namespace SetupPulse.Tests;

public sealed class UpdateTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "SetupPulseUpdates-" + Guid.NewGuid().ToString("N"));
    string PathIn(string name) { Directory.CreateDirectory(folder); return Path.Combine(folder, name); }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static string ReleaseJson(long size = 100, string tag = "v0.4.1", bool draft = false, bool prerelease = false, string? packageUrl = null, string? digest = null)
    {
        string name = "SetupPulse-0.4.1-win-x64.zip", root = "https://github.com/" + UpdateProtocol.Repository;
        return Json.Encode(new { tag_name = tag, html_url = root + "/releases/tag/" + tag, draft, prerelease,
            assets = new[] {
                new { name, state = "uploaded", size, browser_download_url = packageUrl ?? root + "/releases/download/v0.4.1/" + name, digest },
                new { name = name + ".sha256", state = "uploaded", size = 100L, browser_download_url = root + "/releases/download/v0.4.1/" + name + ".sha256", digest = (string?)null }
            } });
    }
    string Package(string name = "package", string? extraEntry = null, bool wrongFileHash = false)
    {
        string path = PathIn(name + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var sums = new List<string>();
        foreach (string file in UpdatePackage.Files.Where(n => n != "SHA256SUMS.txt"))
        {
            byte[] bytes = Encoding.UTF8.GetBytes("new " + file);
            using (var output = archive.CreateEntry(file).Open()) output.Write(bytes);
            sums.Add((wrongFileHash ? new string('0', 64) : Hash(bytes)) + " *" + file);
        }
        using (var writer = new StreamWriter(archive.CreateEntry("SHA256SUMS.txt").Open())) writer.Write(string.Join('\n', sums));
        if (extraEntry != null) using (var output = archive.CreateEntry(extraEntry).Open()) output.WriteByte(1);
        return path;
    }
    string Extract(string name = "package")
    {
        string zip = Package(name), destination = PathIn(name);
        UpdatePackage.Extract(zip, UpdatePackage.Hash(zip), destination); return destination;
    }
    [Fact] public void StableVersionsCompareNumericallyAndRejectUnsafeTags()
    {
        Assert.True(UpdateProtocol.Version("v0.10.0") > UpdateProtocol.Version("0.9.9"));
        Assert.Equal("0.4.0", UpdateProtocol.CurrentVersion("0.4.0+abc"));
        foreach (string value in new[] { "1.2", "1.2.3-beta", "01.2.3", "1.2.3/evil", "1.2.3.4", "1.2.-3" })
            Assert.Throws<InvalidDataException>(() => UpdateProtocol.Version(value));
    }
    [Fact] public void ReleaseRequiresStableOfficialAssetsAndConsistentNamesHashesAndSizes()
    {
        var release = UpdateProtocol.Parse(ReleaseJson(digest: "sha256:" + new string('a', 64)));
        Assert.Equal("0.4.1", release.Version); Assert.Equal(new string('a', 64), release.Digest);
        Assert.Throws<InvalidDataException>(() => UpdateProtocol.Parse(ReleaseJson(draft: true)));
        Assert.Throws<InvalidDataException>(() => UpdateProtocol.Parse(ReleaseJson(prerelease: true)));
        Assert.Throws<InvalidDataException>(() => UpdateProtocol.Parse(ReleaseJson(tag: "v0.4.2")));
        Assert.Throws<InvalidDataException>(() => UpdateProtocol.Parse(ReleaseJson(size: UpdateProtocol.MaxPackageSize + 1)));
        Assert.Throws<InvalidDataException>(() => UpdateProtocol.Parse(ReleaseJson(packageUrl: "https://evil.example/update.zip")));
        Assert.Throws<InvalidDataException>(() => UpdateProtocol.Parse(ReleaseJson(digest: "md5:abc")));
        Assert.Throws<InvalidDataException>(() => UpdateProtocol.PackageHash(new string('a', 64) + " *other.zip", release.PackageName));
    }
    [Theory]
    [InlineData("../settings.json")]
    [InlineData("SetupPulse.exe/evil")]
    [InlineData("setuppulse.exe")]
    [InlineData("SetupPulse.exe")]
    public void UnexpectedTraversalOrDuplicateZipEntriesAreRejectedBeforeExtraction(string name)
    {
        string zip = Package(extraEntry: name), destination = PathIn("output");
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(zip, UpdatePackage.Hash(zip), destination));
        Assert.False(Directory.Exists(destination));
    }
    [Fact] public void CorruptedArchiveOrIndividualFileNeverBecomesAnInstallablePackage()
    {
        string zip = Package();
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(zip, new string('0', 64), PathIn("bad-zip")));
        string wrong = Package("wrong", wrongFileHash: true);
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(wrong, UpdatePackage.Hash(wrong), PathIn("bad-content")));
    }
    [Fact] public void FileSwapInstallsOnlyPackageFilesAndLeavesSettingsUntouched()
    {
        string package = Extract(), target = PathIn("installed"); Directory.CreateDirectory(target);
        foreach (string file in UpdatePackage.Files) File.WriteAllText(Path.Combine(target, file), "old " + file);
        File.WriteAllText(Path.Combine(target, "settings.json"), "keep-me");
        using (var swap = new UpdateFileSwap(package, target)) { swap.Apply(); swap.Commit(); }
        foreach (string file in UpdatePackage.Files) Assert.Equal(File.ReadAllBytes(Path.Combine(package, file)), File.ReadAllBytes(Path.Combine(target, file)));
        Assert.Equal("keep-me", File.ReadAllText(Path.Combine(target, "settings.json")));
        Assert.Empty(Directory.GetDirectories(target));
    }
    [Fact] public void FailedStartupRollbackRestoresOriginalFilesAndRemovesNewFiles()
    {
        string package = Extract(), target = PathIn("installed"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "SetupPulse.exe"), "old app");
        File.WriteAllText(Path.Combine(target, "settings.json"), "keep-me");
        using (var swap = new UpdateFileSwap(package, target)) { swap.Apply(); swap.Rollback(); }
        Assert.Equal("old app", File.ReadAllText(Path.Combine(target, "SetupPulse.exe")));
        Assert.False(File.Exists(Path.Combine(target, "LICENSE")));
        Assert.Equal("keep-me", File.ReadAllText(Path.Combine(target, "settings.json")));
    }
    [Fact] public void FailureHalfwayThroughReplacementRollsBackAlreadyReplacedFiles()
    {
        string package = Extract(), target = PathIn("installed"); Directory.CreateDirectory(target);
        foreach (string file in UpdatePackage.Files.Where(f => f != "README.txt")) File.WriteAllText(Path.Combine(target, file), "old " + file);
        Directory.CreateDirectory(Path.Combine(target, "README.txt"));
        Assert.Throws<IOException>(() => { using var swap = new UpdateFileSwap(package, target); swap.Apply(); });
        foreach (string file in UpdatePackage.Files.Where(f => f != "README.txt")) Assert.Equal("old " + file, File.ReadAllText(Path.Combine(target, file)));
    }
    [Fact] public void JobsAndCleanupCannotEscapeTheirRoot()
    {
        string root = PathIn("jobs"); Directory.CreateDirectory(root);
        string valid = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Assert.Equal(valid, UpdatePaths.Job(root, valid));
        Assert.Throws<InvalidDataException>(() => UpdatePaths.Job(root, Path.Combine(root, "..", "outside")));
        Assert.Throws<InvalidDataException>(() => UpdatePaths.Job(root, Path.Combine(root, "non-guid")));
        Assert.Throws<IOException>(() => UpdatePaths.DeleteTree(folder, root));
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Requests.Add(request.RequestUri!.AbsoluteUri); return Task.FromResult(respond(request)); }
    }
    [Fact] public async Task PublicReleaseCheckNeverSendsCredentialsAndPrivateRepoIsNotAnError()
    {
        var handler = new Handler(request => {
            Assert.Null(request.Headers.Authorization); Assert.Equal(UpdateProtocol.LatestUrl, request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.NotFound);
        });
        using var service = new UpdatesService(new Store(PathIn("data")), "0.4.0", () => { }, new HttpClient(handler));
        var state = await service.Check(default);
        Assert.Equal("unpublished", state.Status); Assert.Null(state.LatestVersion); Assert.False(service.ShouldCheckAtStartup);
        service.Automatic(false); Assert.False(service.View.Automatic);
    }
    [Fact] public async Task DownloadValidatesChecksumsAndRestrictsRedirectHosts()
    {
        string zip = Package(); byte[] bytes = File.ReadAllBytes(zip); string hash = Hash(bytes);
        var release = UpdateProtocol.Parse(ReleaseJson(bytes.Length, digest: "sha256:" + hash));
        var handler = new Handler(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsoluteUri.EndsWith(".sha256")
            ? new StringContent(hash + " *" + release.PackageName) : new ByteArrayContent(bytes) });
        var github = new GitHubUpdates(new HttpClient(handler));
        string target = PathIn("download"); Directory.CreateDirectory(target); int progress = 0;
        await github.Download(release, target, p => progress = p, default);
        Assert.Equal(100, progress); Assert.True(File.Exists(Path.Combine(target, "package", "SetupPulse.exe")));
        var redirect = new Handler(_ => { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://evil.example/asset"); return response; });
        string rejected = PathIn("rejected"); Directory.CreateDirectory(rejected);
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdates(new HttpClient(redirect)).Download(release, rejected, _ => { }, default));
        Assert.Single(redirect.Requests);
    }
    [Fact] public async Task TruncatedDownloadsFailAndServiceNeverOffersDowngrades()
    {
        string zip = Package(); byte[] bytes = File.ReadAllBytes(zip); string hash = Hash(bytes);
        var release = UpdateProtocol.Parse(ReleaseJson(bytes.Length + 1));
        var handler = new Handler(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsoluteUri.EndsWith(".sha256")
            ? new StringContent(hash + " *" + release.PackageName) : new ByteArrayContent(bytes) });
        string target = PathIn("truncated"); Directory.CreateDirectory(target);
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdates(new HttpClient(handler)).Download(release, target, _ => { }, default));
        using var service = new UpdatesService(new Store(PathIn("data")), "1.0.0", () => { }, new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson()) })));
        Assert.Equal("current", (await service.Check(default)).Status);
        Assert.Null(service.View.LatestVersion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Download(default));
    }
    [Fact] public void StaleFailedUpdateDoesNotAppearAsFailureAfterManualRecoveryToThatVersion()
    {
        var store = new Store(PathIn("data"));
        store.Write("update-result.json", new UpdateResult("error", "old failure", "0.4.1", DateTimeOffset.UtcNow));
        using var old = new UpdatesService(store, "0.4.0", () => { });
        Assert.Equal("error", old.View.Status);
        using var recovered = new UpdatesService(store, "0.4.1", () => { });
        Assert.Equal("idle", recovered.View.Status);
    }
    public void Dispose() { if (Directory.Exists(folder)) UpdatePaths.DeleteTree(folder, Path.GetTempPath()); }
}
