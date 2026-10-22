using System.Diagnostics;
using System.IO.Pipes;
using SetupPulse.Core;
namespace SetupPulse.App;

internal static class Program
{
    internal static string? UpdateHealthJob;
    internal static void ReportUpdateHealth()
    {
        if (UpdateHealthJob is not { } job) return;
        try { UpdateInstaller.Healthy(job); UpdateHealthJob = null; } catch (Exception) { }
    }
    internal static readonly string DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), RuntimeIdentity.DataFolderName);
    internal const string TransactionMutex = RuntimeIdentity.TransactionMutex;
    internal static T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, TransactionMutex);
        bool taken;
        try { taken = mutex.WaitOne(TimeSpan.FromSeconds(30)); } catch (AbandonedMutexException) { taken = true; }
        if (!taken) throw new InvalidOperationException("Ein Wechsel läuft bereits.");
        try { return action(); } finally { mutex.ReleaseMutex(); }
    }
    internal static void StartWatchdog(string id, bool demo = false)
    {
        using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{(demo ? "--watchdog-demo" : "--watchdog")} {id}") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        if (process == null) throw new InvalidOperationException("Rücksetzungsprozess konnte nicht gestartet werden.");
        string ready = Path.Combine(demo ? Path.Combine(DataPath, "Demo") : DataPath, "watchdog-ready-" + id + ".json");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (!File.Exists(ready))
        {
            if (process.HasExited || DateTimeOffset.UtcNow > deadline) throw new InvalidOperationException("Rücksetzungsprozess ist nicht bereit. Es wurden keine Geräte geändert.");
            Thread.Sleep(50);
        }
        File.Delete(ready);
    }
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--apply-update") { Environment.ExitCode = UpdateInstaller.Run(args[1]); return; }
        if (args.Length == 2 && args[0] == "--update-health") UpdateHealthJob = args[1];
        ApplicationConfiguration.Initialize();
        var store = new Store(args.Contains("--watchdog-demo") ? Path.Combine(DataPath, "Demo") : DataPath);
        if (args.Length == 2 && args[0] is "--watchdog" or "--watchdog-demo")
        {
            try
            {
                if (store.Read<Transaction>("transaction.json") is not { Phase: "pending" } initial || initial.Id != args[1]) return;
                store.Write("watchdog-ready-" + args[1] + ".json", args[1]);
                while (true)
                {
                    Transaction? t;
                    try { t = store.Read<Transaction>("transaction.json"); }
                    catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33) { Thread.Sleep(100); continue; }
                    if (t?.Id != args[1] || t.Phase != "pending") return;
                    if (DateTimeOffset.UtcNow >= t.Deadline && Locked(() => new SwitchEngine(args[0] == "--watchdog-demo" ? new DemoHardware() : new Hardware(), store, _ => { }).RollbackIfExpired(args[1]))) return;
                    Thread.Sleep(500);
                }
            }
            catch (Exception e) { Directory.CreateDirectory(DataPath); File.AppendAllText(store.FilePath("watchdog.log"), $"{DateTimeOffset.Now}: {e}\n"); }
            return;
        }
        if (args.Length == 2 && args[0] is "--diagnose" or "--validate-current")
        {
            try { Directory.CreateDirectory(args[1]); var inventory = new Hardware().Inventory(); File.WriteAllText(Path.Combine(args[1], "inventory.json"), Json.Encode(inventory)); if (args[0] == "--validate-current") { Displays.Validate(inventory.Displays.Where(d => d.Active).Select(d => d.Current!).ToList()); Displays.ValidateDpiAdapter(); Audio.ValidatePolicyAdapter(); File.WriteAllText(Path.Combine(args[1], "validation.txt"), "SDC_VALIDATE, DPI adapter query and PolicyConfig QueryInterface successful. No configuration applied."); } }
            catch (Exception e) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "error.txt"), e.ToString()); }
            return;
        }
        if (args.Length == 2 && args[0] == "--mixer-diagnose")
        {
            try { Directory.CreateDirectory(args[1]); var snapshot = Task.Run(AudioMixer.Inventory).GetAwaiter().GetResult(); File.WriteAllText(Path.Combine(args[1], "mixer.json"), Json.Encode(snapshot)); }
            catch (Exception e) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "error.txt"), e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        bool demo = args.Contains("--demo");
        using var single = new Mutex(true, demo ? RuntimeIdentity.DemoMutex : RuntimeIdentity.InstanceMutex, out bool first);
        if (!first)
        {
            try { using var client = new NamedPipeClientStream(".", demo ? RuntimeIdentity.DemoPipe : RuntimeIdentity.ShowPipe, PipeDirection.Out); client.Connect(1500); client.WriteByte(1); } catch (IOException) { }
            return;
        }
        try { Application.Run(new MainWindow(demo ? new Store(Path.Combine(DataPath, "Demo")) : store, demo, args.Contains("--tray"))); }
        catch (Exception e) { MessageBox.Show(e.Message, "SetupPulse", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
