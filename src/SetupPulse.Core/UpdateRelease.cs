using System.Text.Json;

namespace SetupPulse.Core;

public record UpdateRelease(string Version, string Tag, string PageUrl, string PackageName, string PackageUrl,
    long PackageSize, string ChecksumUrl, string? Digest);

public static class UpdateProtocol
{
    public const string Repository = "Skyline311/SetupPulse";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const long MaxPackageSize = 300L * 1024 * 1024;
    public static Version Version(string value)
    {
        if (value.StartsWith('v')) value = value[1..];
        var parts = value.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length is < 1 or > 6 || p.Any(c => !char.IsAsciiDigit(c)) || p.Length > 1 && p[0] == '0'))
            throw new InvalidDataException("Ungültige Release-Version.");
        return new(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }
    public static string CurrentVersion(string informational) => Version(informational.Split('+')[0]).ToString(3);
    public static UpdateRelease Parse(string json)
    {
        if (json.Length > 1_000_000) throw new InvalidDataException("Release-Antwort ist zu groß.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("Nur veröffentlichte stabile Releases werden unterstützt.");
        string tag = root.GetProperty("tag_name").GetString()!;
        string version = Version(tag).ToString(3);
        if (tag != "v" + version) throw new InvalidDataException("Das Release benötigt ein vX.Y.Z-Tag.");
        string page = "https://github.com/" + Repository + "/releases/tag/" + tag;
        if (root.GetProperty("html_url").GetString() != page) throw new InvalidDataException("Unerwartete Release-Adresse.");
        string name = "SetupPulse-" + version + "-win-x64.zip";
        var assets = root.GetProperty("assets").EnumerateArray().ToList();
        JsonElement Asset(string filename)
        {
            var found = assets.Where(a => a.GetProperty("name").GetString() == filename).ToList();
            if (found.Count != 1 || found[0].GetProperty("state").GetString() != "uploaded")
                throw new InvalidDataException("Das Release enthält kein vollständiges Windows-x64-Paket mit Prüfsumme.");
            string expected = "https://github.com/" + Repository + "/releases/download/" + tag + "/" + filename;
            if (found[0].GetProperty("browser_download_url").GetString() != expected)
                throw new InvalidDataException("Unerwartete Download-Adresse.");
            return found[0];
        }
        var package = Asset(name); var checksum = Asset(name + ".sha256");
        long size = package.GetProperty("size").GetInt64();
        if (size is < 1 or > MaxPackageSize || checksum.GetProperty("size").GetInt64() is < 1 or > 1024)
            throw new InvalidDataException("Ungültige Größe des Update-Pakets.");
        string? digest = package.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        if (digest != null && (!digest.StartsWith("sha256:", StringComparison.Ordinal) || !IsHash(digest[7..])))
            throw new InvalidDataException("Ungültige GitHub-Prüfsumme.");
        return new(version, tag, page, name, package.GetProperty("browser_download_url").GetString()!, size,
            checksum.GetProperty("browser_download_url").GetString()!, digest?[7..]);
    }
    public static bool IsHash(string hash) => hash.Length == 64 && hash.All(char.IsAsciiHexDigit);
    public static string PackageHash(string text, string name)
    {
        string[] parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !IsHash(parts[0]) || parts[1] != "*" + name)
            throw new InvalidDataException("Ungültige Paket-Prüfsumme.");
        return parts[0];
    }
}
