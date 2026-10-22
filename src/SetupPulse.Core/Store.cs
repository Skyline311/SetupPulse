namespace SetupPulse.Core;

public sealed class Store(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string FilePath(string name) => Path.Combine(DirectoryPath, name);
    public string? RecoveryWarning { get; private set; }
    bool recovered;
    public void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = FilePath(name); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(f)) { writer.Write(Json.Encode(value)); writer.Flush(); f.Flush(true); }
        if (name == "settings.json" && recovered && File.Exists(path)) { File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N")); recovered = false; }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
    }
    public T? Read<T>(string name)
    {
        var path = FilePath(name);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                // Atomic replacement may proceed while this handle reads its complete old snapshot.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return Json.Decode<T>(reader.ReadToEnd());
            }
            catch (FileNotFoundException) when (attempt < 3) { Thread.Sleep(10); }
            catch (FileNotFoundException) { return default; }
            catch (DirectoryNotFoundException) { return default; }
            catch (IOException e) when (attempt < 10 && (e.HResult & 0xffff) is 32 or 33) { Thread.Sleep(25); }
        }
    }
    public Settings Load()
    {
        try { var s = Read<Settings>("settings.json") ?? Settings.Empty; Validation.Settings(s); return s; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException or NullReferenceException)
        {
            RecoveryWarning = "Einstellungen beschädigt. Sicherung wurde geladen; Originaldatei bleibt erhalten.";
            var backup = Read<Settings>("settings.json.bak");
            if (backup == null) throw new InvalidDataException("Einstellungen beschädigt; keine Sicherung vorhanden. Datei bitte sichern und reparieren.", e);
            Validation.Settings(backup); recovered = true; return backup;
        }
    }
}
public interface IHardware
{
    Inventory Inventory();
    List<AudioDevice> AudioInventory();
    void ValidateDisplays(List<DisplaySetting> displays);
    void ApplyDisplays(List<DisplaySetting> displays);
    void ValidateDisplayPositions(List<DisplaySetting> displays) => ValidateDisplays(displays);
    void ApplyDisplayPositions(List<DisplaySetting> displays) => ApplyDisplays(displays);
    void VerifyDisplays(List<DisplaySetting> displays);
    void ApplyAudio(AudioFavorite favorite);
    void RestoreAudio(List<AudioDevice> devices);
}
