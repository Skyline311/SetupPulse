using System.Diagnostics;
using System.Reflection;
using SetupPulse.Core;

namespace SetupPulse.App;

internal record UpdateInstallPlan(int ParentId, long ParentStartTicks, string Target, string OldHash, string Version, string NewHash);

internal static class UpdateInstaller
{
    static string Jobs => Path.Combine(Program.DataPath, "Updates");
    public static void Executable(string path, string version)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (stream.Length < 256 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Das Update enthält keine Windows-EXE.");
        stream.Position = 0x3c; int offset = reader.ReadInt32();
        if (offset < 64 || offset > stream.Length - 6) throw new InvalidDataException("Ungültige EXE im Update.");
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("Das Update ist nicht für Windows x64.");
        var info = FileVersionInfo.GetVersionInfo(path);
        if (info.ProductName != "SetupPulse" || UpdateProtocol.CurrentVersion(info.ProductVersion ?? "") != version)
            throw new InvalidDataException("Produkt oder Version der Update-EXE stimmt nicht.");
    }
    public static async Task Prepare(PreparedUpdate update, CancellationToken token)
    {
        string job = UpdatePaths.Job(Jobs, update.Folder);
        string target = Path.GetFullPath(Environment.ProcessPath!);
        if (Path.GetFileName(target) != "SetupPulse.exe") throw new IOException("Für Updates muss die App SetupPulse.exe heißen.");
        UpdatePaths.NoLinks(target);
        string candidate = Path.Combine(job, "package", "SetupPulse.exe");
        await Task.Run(() => Executable(candidate, update.Release.Version), token);
        // Fail before shutting down if this portable folder is not writable. No elevation is requested.
        string probe = Path.Combine(Path.GetDirectoryName(target)!, ".setuppulse-write-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        var parent = Process.GetCurrentProcess();
        var plan = await Task.Run(() => new UpdateInstallPlan(parent.Id, parent.StartTime.ToUniversalTime().Ticks, target,
            UpdatePackage.Hash(target), update.Release.Version, UpdatePackage.Hash(candidate)), token);
        new Store(job).Write("plan.json", plan);
        string helper = Path.Combine(job, "SetupPulse.Update.exe");
        await Task.Run(() => File.Copy(target, helper), token);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(job);
        using var process = Process.Start(start) ?? throw new IOException("Der Update-Prozess konnte nicht starten.");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(job, "ready")))
            {
                token.ThrowIfCancellationRequested();
                if (process.HasExited || DateTime.UtcNow >= deadline) throw new IOException("Der Update-Prozess ist nicht bereit. SetupPulse bleibt geöffnet.");
                await Task.Delay(100, token);
            }
        }
        catch
        {
            // The parent is still running, so this helper cannot have begun replacing files.
            if (!process.HasExited)
            {
                process.Kill();
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(stop.Token);
            }
            throw;
        }
    }
    static Mutex Acquire(string name)
    {
        var mutex = new Mutex(false, name); bool owned;
        try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) { mutex.Dispose(); throw new IOException("Eine weitere SetupPulse-Instanz oder ein Wechsel ist noch aktiv."); }
        return mutex;
    }
    static void Instance(Action action)
    {
        using var instance = Acquire(RuntimeIdentity.InstanceMutex);
        try { action(); } finally { instance.ReleaseMutex(); }
    }
    static Process Launch(string target, string? healthJob = null)
    {
        var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)!, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (healthJob != null) { start.ArgumentList.Add("--update-health"); start.ArgumentList.Add(healthJob); }
        else start.ArgumentList.Add("--tray");
        return Process.Start(start) ?? throw new IOException("SetupPulse konnte nicht neu gestartet werden.");
    }
    static UpdateInstallPlan Plan(string job)
    {
        string path = Path.Combine(job, "plan.json");
        if (new FileInfo(path).Length > 10000) throw new InvalidDataException("Ungültiger Update-Auftrag.");
        var plan = new Store(job).Read<UpdateInstallPlan>("plan.json") ?? throw new InvalidDataException("Update-Auftrag fehlt.");
        if (Path.GetFullPath(plan.Target) != plan.Target || Path.GetFileName(plan.Target) != "SetupPulse.exe"
            || !UpdateProtocol.IsHash(plan.OldHash) || !UpdateProtocol.IsHash(plan.NewHash)) throw new InvalidDataException("Ungültiges Update-Ziel.");
        _ = UpdateProtocol.Version(plan.Version); UpdatePaths.NoLinks(plan.Target);
        return plan;
    }
    public static int Run(string directory)
    {
        string? job = null; UpdateInstallPlan? plan = null; Process? next = null;
        UpdateFileSwap? swap = null; Mutex? transaction = null; bool parentExited = false, restartOld = false;
        try
        {
            job = UpdatePaths.Job(Jobs, directory); plan = Plan(job);
            if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.Combine(job, "SetupPulse.Update.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ungültiger Update-Prozess.");
            UpdatePackage.CheckHash(Environment.ProcessPath!, plan.OldHash);
            using var parent = Process.GetProcessById(plan.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTicks || !string.Equals(parent.MainModule!.FileName, plan.Target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Die bisherige App-Instanz stimmt nicht.");
            string package = Path.Combine(job, "package");
            UpdatePackage.VerifyDirectory(package); Executable(Path.Combine(package, "SetupPulse.exe"), plan.Version);
            UpdatePackage.CheckHash(Path.Combine(package, "SetupPulse.exe"), plan.NewHash);
            File.WriteAllText(Path.Combine(job, "ready"), "ready");
            if (!parent.WaitForExit(90000)) throw new IOException("SetupPulse wurde nicht beendet; Update abgebrochen.");
            parentExited = true;
            transaction = Acquire(RuntimeIdentity.TransactionMutex);
            if (new Store(Program.DataPath).Read<Transaction>("transaction.json")?.Phase == "pending")
                throw new IOException("Ein Monitortest ist noch offen; Update abgebrochen.");
            Instance(() => {
                UpdatePackage.CheckHash(plan.Target, plan.OldHash);
                swap = new(package, Path.GetDirectoryName(plan.Target)!);
                swap.Apply(); UpdatePackage.CheckHash(plan.Target, plan.NewHash);
            });
            next = Launch(plan.Target, job);
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!File.Exists(Path.Combine(job, "healthy")))
            {
                if (next.HasExited || DateTime.UtcNow >= deadline) throw new IOException("Die neue Version konnte nicht vollständig starten.");
                Thread.Sleep(150);
            }
            UpdatePackage.CheckHash(plan.Target, plan.NewHash);
            swap!.Commit();
            new Store(Program.DataPath).Write("update-result.json", new UpdateResult("installed", "SetupPulse wurde auf Version " + plan.Version + " aktualisiert.", plan.Version, DateTimeOffset.UtcNow));
            File.WriteAllText(Path.Combine(job, "done"), "installed");
            return 0;
        }
        catch (Exception error)
        {
            string message = "Das Update konnte nicht installiert werden. Die bisherige Version bleibt erhalten.";
            if (parentExited && plan != null)
            {
                try
                {
                    if (next != null && !next.HasExited) { next.Kill(); if (!next.WaitForExit(10000)) throw new IOException("Neue App konnte nicht beendet werden."); }
                    Instance(() => swap?.Rollback()); restartOld = true;
                    message = "Das Update wurde abgebrochen. Die bisherige Version wurde wiederhergestellt.";
                }
                catch (Exception)
                {
                    swap?.PreserveForRecovery();
                    message = "Das Update ist fehlgeschlagen. Bitte SetupPulse manuell neu laden; die Sicherung bleibt im App-Ordner erhalten.";
                    if (job != null) try { File.WriteAllText(Path.Combine(job, "recovery-needed"), swap?.RecoveryDirectory ?? ""); } catch (IOException) { }
                }
            }
            try
            {
                if (job != null) File.WriteAllText(Path.Combine(job, "error.txt"), error.ToString());
                new Store(Program.DataPath).Write("update-result.json", new UpdateResult("error", message, plan?.Version ?? "", DateTimeOffset.UtcNow));
                if (job != null) File.WriteAllText(Path.Combine(job, "done"), "failed");
            }
            catch (Exception) { }
            return 1;
        }
        finally
        {
            try { swap?.Dispose(); } catch (Exception) { }
            if (transaction != null) { transaction.ReleaseMutex(); transaction.Dispose(); }
            if (restartOld && plan != null) try { using var restarted = Launch(plan.Target); } catch (Exception) { }
            next?.Dispose();
        }
    }
    public static void Healthy(string directory)
    {
        string job = UpdatePaths.Job(Jobs, directory); var plan = Plan(job);
        if (!string.Equals(plan.Target, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
        string version = typeof(UpdateInstaller).Assembly.GetName().Version!.ToString(3);
        if (version != plan.Version) return;
        UpdatePackage.CheckHash(plan.Target, plan.NewHash);
        File.WriteAllText(Path.Combine(job, "healthy"), "healthy");
    }
    public static void Cleanup()
    {
        if (!Directory.Exists(Jobs)) return;
        foreach (string directory in Directory.GetDirectories(Jobs))
        {
            try
            {
                string job = UpdatePaths.Job(Jobs, directory);
                if (File.Exists(Path.Combine(job, "recovery-needed"))) continue;
                bool done = File.Exists(Path.Combine(job, "done"));
                if (!done && Directory.GetLastWriteTimeUtc(job) > DateTime.UtcNow.AddDays(-7)) continue;
                string helper = Path.Combine(job, "SetupPulse.Update.exe");
                if (File.Exists(helper)) { using var probe = new FileStream(helper, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                UpdatePaths.DeleteTree(job, Jobs);
            }
            catch (Exception) { }
        }
    }
}
