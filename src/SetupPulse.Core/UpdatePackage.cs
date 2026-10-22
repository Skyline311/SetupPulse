using System.IO.Compression;
using System.Security.Cryptography;

namespace SetupPulse.Core;

public static class UpdatePackage
{
    public static readonly string[] Files = ["SetupPulse.exe", "LICENSE", "THIRD-PARTY-LICENSES.txt", "THIRD-PARTY-NOTICES.md", "README.txt", "CHANGELOG.md", "SHA256SUMS.txt"];
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static void CheckHash(string path, string expected)
    {
        if (!UpdateProtocol.IsHash(expected) || !Hash(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Die Update-Prüfsumme stimmt nicht. Es wurde nichts installiert.");
    }
    public static void Extract(string archivePath, string expectedHash, string destination)
    {
        CheckHash(archivePath, expectedHash);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Update-Verzeichnis existiert bereits.");
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count != Files.Length || archive.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Files.Length
            || archive.Entries.Any(e => !Files.Contains(e.FullName, StringComparer.Ordinal) || e.Length < 1 || e.Length > (e.FullName == "SetupPulse.exe" ? 512L * 1024 * 1024 : 5L * 1024 * 1024)
                || e.FullName == "SHA256SUMS.txt" && e.Length > 8192
                || ((e.ExternalAttributes >> 16) & 0xf000) == 0xa000)
            || archive.Entries.Sum(e => e.Length) > 600L * 1024 * 1024)
            throw new InvalidDataException("Das Update-Paket enthält unerwartete Dateien.");
        Directory.CreateDirectory(destination);
        foreach (var entry in archive.Entries)
        {
            using var source = entry.Open();
            using var target = new FileStream(Path.Combine(destination, entry.FullName), FileMode.CreateNew);
            byte[] buffer = new byte[81920]; long copied = 0; int count;
            while ((count = source.Read(buffer)) != 0)
            {
                copied += count; if (copied > entry.Length) throw new InvalidDataException("Ungültige Dateigröße im Paket.");
                target.Write(buffer, 0, count);
            }
            if (copied != entry.Length) throw new InvalidDataException("Update-Paket ist unvollständig.");
        }
        VerifyDirectory(destination);
    }
    public static void VerifyDirectory(string directory)
    {
        UpdatePaths.NoLinks(directory);
        foreach (string name in Files)
        {
            string path = Path.Combine(directory, name); UpdatePaths.NoLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length < 1) throw new InvalidDataException("Update-Dateien fehlen.");
        }
        if (new FileInfo(Path.Combine(directory, "SHA256SUMS.txt")).Length > 8192) throw new InvalidDataException("Datei-Prüfsummen sind zu groß.");
        var lines = File.ReadAllLines(Path.Combine(directory, "SHA256SUMS.txt"));
        var checkedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            string[] parts = line.Split(" *", StringSplitOptions.None);
            if (parts.Length != 2 || !Files.Contains(parts[1], StringComparer.Ordinal) || parts[1] == "SHA256SUMS.txt" || !checkedNames.Add(parts[1]))
                throw new InvalidDataException("Ungültige Datei-Prüfsummen im Update.");
            CheckHash(Path.Combine(directory, parts[1]), parts[0]);
        }
        if (checkedNames.Count != Files.Length - 1) throw new InvalidDataException("Datei-Prüfsummen fehlen.");
    }
}

// Prepared next to the destination so each individual replacement is atomic on the same volume.
public sealed class UpdateFileSwap : IDisposable
{
    readonly string target, stage;
    readonly List<(string Name, bool Existed)> applied = [];
    bool committed, preserve;
    public string RecoveryDirectory => stage;
    public UpdateFileSwap(string package, string targetDirectory)
    {
        UpdatePackage.VerifyDirectory(package);
        target = Path.GetFullPath(targetDirectory);
        UpdatePaths.NoLinks(target);
        stage = Path.Combine(target, ".setuppulse-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            Directory.CreateDirectory(Path.Combine(stage, "backup"));
            foreach (string name in UpdatePackage.Files)
            {
                UpdatePaths.NoLinks(Path.Combine(target, name));
                File.Copy(Path.Combine(package, name), Path.Combine(stage, name));
            }
            UpdatePackage.VerifyDirectory(stage);
        }
        catch { UpdatePaths.DeleteTree(stage, target); throw; }
    }
    public void Apply()
    {
        foreach (string name in UpdatePackage.Files)
        {
            string destination = Path.Combine(target, name), source = Path.Combine(stage, name);
            UpdatePaths.NoLinks(destination);
            bool exists = File.Exists(destination);
            if (exists) File.Replace(source, destination, Path.Combine(stage, "backup", name));
            else File.Move(source, destination);
            applied.Add((name, exists));
        }
    }
    public void Rollback()
    {
        foreach (var (name, existed) in applied.AsEnumerable().Reverse().ToList())
        {
            string destination = Path.Combine(target, name);
            UpdatePaths.NoLinks(destination);
            if (existed) File.Move(Path.Combine(stage, "backup", name), destination, true);
            else File.Delete(destination);
            applied.RemoveAt(applied.Count - 1);
        }
    }
    public void Commit() { committed = true; }
    public void PreserveForRecovery() { preserve = true; }
    public void Dispose()
    {
        if (preserve) return;
        if (!committed && applied.Count > 0) Rollback();
        if (applied.Count == 0 || committed) UpdatePaths.DeleteTree(stage, target);
    }
}

public static class UpdatePaths
{
    public static string Job(string root, string directory)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!string.Equals(Path.GetDirectoryName(full), fullRoot, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(full), "N", out _)) throw new InvalidDataException("Ungültiger Update-Auftrag.");
        NoLinks(full); return full;
    }
    public static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknüpfte Update-Pfade werden nicht unterstützt.");
    }
    public static void DeleteTree(string directory, string expectedParent)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedParent)), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Ungültiges temporäres Verzeichnis.");
        if (!Directory.Exists(full)) return;
        NoLinks(full);
        // Check each child before descending; never traverse junctions or symbolic links.
        foreach (string file in Directory.GetFiles(full)) { NoLinks(file); File.Delete(file); }
        foreach (string child in Directory.GetDirectories(full)) DeleteTree(child, full);
        Directory.Delete(full);
    }
}
