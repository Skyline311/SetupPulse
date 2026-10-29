using System.IO.Pipes;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using SetupPulse.Core;

namespace SetupPulse.App;

internal sealed partial class MainWindow : Form
{
    readonly Store store;
    readonly IHardware hardware;
    readonly SwitchEngine engine;
    readonly DemoMixer? demoMixer;
    readonly bool demo, startHidden;
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly NotifyIcon tray = new() { Text = "SetupPulse" };
    readonly Icon appIcon, trayIcon;
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 10000 };
    readonly System.Windows.Forms.Timer volumeRefresh = new() { Interval = 300 };
    readonly TrayClickActions trayClicks;
    readonly Dictionary<int, (Func<Task> Action, bool RefreshState)> hotkeys = [];
    readonly CancellationTokenSource shutdown = new();
    Settings settings;
    bool exiting, busy, busyMixer;
    string? activeProfile;
    Inventory? trayInventory;
    string? captureId;
    ShortcutCapture? shortcutCapture;
    TrayVolumeScroll? trayVolumeScroll;
    int queuedVolumeSteps;
    bool changingVolume;
    string? requestedView;
    TrayMixerWindow? quickMixerWindow;
    bool movingSurface, mainVisibleBeforeMixer, passiveRestore;
    bool initialVisibilityPending = true, startupInitialized;
    Task? webInitialization;
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public MainWindow(Store store, bool demo, bool startHidden)
    {
        this.store = store; this.demo = demo; this.startHidden = startHidden;
        settings = store.Load(); hardware = demo ? new DemoHardware() : new Hardware();
        if (demo) demoMixer = new DemoMixer(hardware);
        engine = new(hardware, store, id => Program.StartWatchdog(id, demo));
        InitializeHomeAssistantSettings();
        InitializeUpdates();
        Text = demo ? "SetupPulse · Demo" : "SetupPulse"; Size = new(1240, 860); MinimumSize = new(960, 680); StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(23, 23, 23);
        using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon")!) appIcon = new Icon(iconStream);
        Icon = appIcon; trayIcon = TrayIconArtwork.Create(); tray.Icon = trayIcon; tray.Visible = true;
        Controls.Add(web);
        Shown += async (_, _) => await InitializeWebOnce();
        FormClosing += (_, e) => { if (!exiting && settings.Preferences.CloseToTray) { e.Cancel = true; Hide(); } };
        // NotifyIcon sets its double-click flag after this callback returns.
        // Showing/reparenting windows here can pump the release message too early.
        trayClicks = new(() => ShowAudio("quickMixer"), () => BeginInvoke(OpenMainWindow));
        tray.MouseClick += (_, e) => trayClicks.MouseClick(e.Button);
        tray.MouseDoubleClick += (_, e) => trayClicks.MouseDoubleClick(e.Button);
        tray.MouseDown += (_, e) => trayClicks.MouseDown(e.Button, e.Clicks);
        VisibleChanged += (_, _) => PostVisibility();
        Resize += (_, _) => PostVisibility();
        refresh.Tick += async (_, _) => { if (!busy) await SendState(); };
        volumeRefresh.Tick += async (_, _) => { volumeRefresh.Stop(); if (busy) volumeRefresh.Start(); else await SendState(); };
        SystemEvents.PowerModeChanged += PowerChanged;
        Load += (_, _) => InitializeStartup();
        _ = ListenForInstance();
    }
    void InitializeStartup()
    {
        if (startupInitialized) return;
        startupInitialized = true;
        try { RegisterShortcuts(settings); } catch (Exception e) { tray.ShowBalloonTip(6000, "Shortcut nicht verfügbar", e.Message, ToolTipIcon.Warning); }
        if (!demo && settings.Preferences.StartWithWindows) {
            try { SetAutostart(true); } catch (Exception e) { tray.ShowBalloonTip(6000, "Autostart nicht aktualisiert", e.Message, ToolTipIcon.Warning); }
        }
        RebuildTray();
        try { UpdateTrayScroll(); } catch (Exception e) { tray.ShowBalloonTip(6000, "Tray-Lautstärke nicht verfügbar", e.Message, ToolTipIcon.Warning); }
        StartHomeAssistant();
        _ = StartUpdateCheck();
    }
    Task InitializeWebOnce() => webInitialization ??= InitializeWeb();
    protected override void SetVisibleCore(bool value)
    {
        if (value && initialVisibilityPending)
        {
            initialVisibilityPending = false;
            if (startHidden)
            {
                // Create the message/hotkey handle without ever showing the form.
                _ = Handle;
                InitializeStartup();
                _ = InitializeWebOnce();
                base.SetVisibleCore(false);
                return;
            }
        }
        base.SetVisibleCore(value);
    }
    void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume && IsHandleCreated) BeginInvoke(async () => await SendState()); }
    protected override bool ShowWithoutActivation => passiveRestore || base.ShowWithoutActivation;
    void ShowWindow() { DismissQuickMixer(false); Show(); WindowState = FormWindowState.Normal; Activate(); }
    void OpenMainWindow() { trayClicks.Cancel(); requestedView = "mainWindow"; ShowWindow(); Post(new { @event = "navigate", data = requestedView }); }
    void ShowAudio(string target)
    {
        trayClicks.Cancel();
        if (target == "quickMixer") { ShowQuickMixer(); return; }
        ShowWindow(); requestedView = target; Post(new { @event = "navigate", data = target });
    }
    bool SurfaceVisible => quickMixerWindow?.Visible == true || (Visible && WindowState != FormWindowState.Minimized);
    void PostVisibility() => Post(new { @event = "windowVisibility", data = SurfaceVisible });
    void ShowQuickMixer()
    {
        // Keep the confirmation/alignment surface accessible during monitor transactions.
        if (engine.Pending != null || (busy && !busyMixer)) { ShowWindow(); return; }
        if (quickMixerWindow?.Visible == true) { quickMixerWindow.Activate(); return; }
        quickMixerWindow ??= new TrayMixerWindow(() => DismissQuickMixer());
        movingSurface = true;
        try
        {
            mainVisibleBeforeMixer = Visible && WindowState != FormWindowState.Minimized;
            Hide(); quickMixerWindow.Controls.Add(web);
            requestedView = "quickMixer";
            Post(new { @event = "navigate", data = requestedView });
            quickMixerWindow.PositionAtTray(tray);
            WindowTheme.Apply(quickMixerWindow.Handle, WindowTheme.IsDark(settings.Preferences.Theme));
            quickMixerWindow.Show(); quickMixerWindow.PositionAtTray(tray); quickMixerWindow.Activate(); web.Focus();
        }
        finally { movingSurface = false; PostVisibility(); }
    }
    void DismissQuickMixer(bool restoreMain = true)
    {
        if (movingSurface || quickMixerWindow == null || web.Parent != quickMixerWindow) return;
        movingSurface = true;
        try
        {
            quickMixerWindow.Hide(); Controls.Add(web);
            requestedView = "mainWindow";
            Post(new { @event = "navigate", data = requestedView });
            if (restoreMain && mainVisibleBeforeMixer && !exiting)
            {
                passiveRestore = true;
                try { Show(); } finally { passiveRestore = false; }
            }
        }
        finally { movingSurface = false; PostVisibility(); }
    }
    async Task ListenForInstance()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try { using var pipe = new NamedPipeServerStream(demo ? RuntimeIdentity.DemoPipe : RuntimeIdentity.ShowPipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); await pipe.WaitForConnectionAsync(shutdown.Token); if (IsHandleCreated) BeginInvoke(OpenMainWindow); }
            catch (OperationCanceledException) { return; }
            catch (IOException) { await Task.Delay(1000); }
        }
    }
    async Task InitializeWeb()
    {
        try
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
            string assets = store.FilePath("web-" + version);
            foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(x => x.StartsWith("Web/")))
            {
                var path = Path.Combine(assets, name[4..].Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!; using var output = File.Create(path); input.CopyTo(output);
            }
            var environment = await CoreWebView2Environment.CreateAsync(null, store.FilePath("WebView2"));
            await web.EnsureCoreWebView2Async(environment);
            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = demo; core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping("appassets.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://appassets.local/", StringComparison.Ordinal)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += OnMessage;
            core.ProcessFailed += (_, _) => { tray.ShowBalloonTip(5000, "Oberfläche neu starten", "Die Oberfläche wurde beendet. Die Monitor-Rücksetzung läuft unabhängig weiter.", ToolTipIcon.Warning); };
            web.Source = new Uri("https://appassets.local/index.html?v=" + version); refresh.Start();
        }
        catch (WebView2RuntimeNotFoundException) { MessageBox.Show("Microsoft Edge WebView2 Runtime fehlt. Bitte die Evergreen Runtime von Microsoft installieren und SetupPulse erneut öffnen.", "SetupPulse"); exiting = true; Close(); }
    }
    sealed record Request(string Id, string Method, JsonElement Payload);
    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? id = null;
        try
        {
            if (!e.Source.StartsWith("https://appassets.local/", StringComparison.Ordinal) || e.WebMessageAsJson.Length > 1_000_000) throw new InvalidDataException("Ungültige Anfrage.");
            var r = Json.Decode<Request>(e.WebMessageAsJson); id = r.Id;
            if (id.Length > 80) throw new InvalidDataException("Ungültige Anfragekennung.");
            object? result = r.Method switch
            {
                "state" => await State(),
                "save" => Save(Json.Decode<Settings>(r.Payload.GetRawText())),
                "mixerState" => demo ? demoMixer!.Inventory() : await Task.Run(AudioMixer.Inventory),
                "bluetoothState" => demo ? BluetoothInventory.Demo() : await Task.Run(BluetoothInventory.Inventory),
                "homeAssistantSettings" => HomeAssistantView(),
                "updates" => UpdatesView(),
                "checkUpdate" => await CheckUpdate(),
                "updateAutomatic" => updates.Automatic(r.Payload.GetBoolean()),
                "installUpdate" => await InstallUpdate(),
                "saveHomeAssistant" => await SaveHomeAssistant(Json.Decode<HomeAssistantSaveRequest>(r.Payload.GetRawText())),
                "mixerEndpoint" => await MixerEndpoint(Json.Decode<MixerEndpointRequest>(r.Payload.GetRawText())),
                "mixerSession" => await MixerSessionChange(Json.Decode<MixerSessionRequest>(r.Payload.GetRawText())),
                "mixerRoute" => await MixerRoute(Json.Decode<MixerRouteRequest>(r.Payload.GetRawText())),
                "openAudioSettings" => OpenAudioSettings(r.Payload.GetString()!),
                "applyProfile" => await ApplyProfile(r.Payload.GetString()!),
                "applyAudio" => await ApplyAudio(r.Payload.GetString()!),
                "beginAlignment" => await BeginAlignment(),
                "updateAlignment" => await UpdateAlignment(Json.Decode<AlignmentRequest>(r.Payload.GetRawText())),
                "confirm" => Confirm(r.Payload.GetString()!),
                "rollback" => await Rollback(),
                "rescue" => await Rescue(),
                "export" => Export(),
                "import" => Import(),
                "licenses" => Licenses(),
                "captureShortcut" => CaptureShortcut(r.Payload.ValueKind == JsonValueKind.Null ? null : r.Payload.GetString()),
                "releaseShortcut" => ReleaseShortcut(r.Payload.GetString()!),
                "windowTheme" => ApplyWindowTheme(r.Payload.GetBoolean()),
                "closeQuickMixer" => CloseQuickMixer(),
                "openFullMixer" => OpenFullMixer(),
                "exit" => ExitApp(),
                _ => throw new InvalidDataException("Unbekannte Aktion.")
            };
            Post(new { id, result });
            if (r.Method == "state") Program.ReportUpdateHealth();
            if (r.Method == "installUpdate") { ExitApp(); return; }
            if (r.Method is not ("state" or "mixerState" or "bluetoothState" or "mixerEndpoint" or "mixerSession" or "mixerRoute")) await SendState();
        }
        catch (Exception ex) { Post(new { id, error = ex.Message }); await SendState(); }
    }
    void Post(object message) { if (web.CoreWebView2 != null && !IsDisposed) web.CoreWebView2.PostWebMessageAsJson(Json.Encode(message)); }
    async Task<object> State()
    {
        var transaction = store.Read<Transaction>("transaction.json");
        if (transaction?.Phase is "rolledBack" or "failed" && transaction.ProfileId == activeProfile) activeProfile = null;
        string? warning = store.RecoveryWarning ?? (transaction?.Phase is "failed" or "rolledBack" && !string.IsNullOrWhiteSpace(transaction.Error)
            ? (transaction.Phase == "rolledBack" ? "Letzter Wechsel fehlgeschlagen; vorheriger Zustand wiederhergestellt: " : "Letzter Wechsel fehlgeschlagen: ") + transaction.Error : null);
        var inventory = await Task.Run(hardware.Inventory);
        trayInventory = inventory; UpdateTrayChecks();
        UpdateHomeAssistantState(inventory, transaction);
        return new { settings, inventory, pending = engine.Pending, busy, busyMixer, activeProfile, demo, warning, requestedView, visible = SurfaceVisible };
    }
    async Task SendState() { try { Post(new { @event = "state", data = await State() }); } catch (Exception e) { Post(new { @event = "error", error = e.Message }); } }
    object Save(Settings incoming)
    {
        if (busy || engine.Pending != null) throw new InvalidOperationException("Einstellungen können während eines Wechsels nicht gespeichert werden.");
        Validation.Settings(incoming);
        incoming = incoming with { Profiles = incoming.Profiles.Select(p => p with { VerifiedHash = settings.Profiles.FirstOrDefault(old => old.Id == p.Id && old.Monitors != null && p.Monitors != null && Validation.MonitorHash(old.Monitors) == Validation.MonitorHash(p.Monitors))?.VerifiedHash }).ToList() };
        var old = settings;
        bool autostartChanged = incoming.Preferences.StartWithWindows != old.Preferences.StartWithWindows;
        try { RegisterShortcuts(incoming); SetTrayScroll(incoming.Preferences.TrayScrollVolume); if (!demo && autostartChanged) SetAutostart(incoming.Preferences.StartWithWindows); store.Write("settings.json", incoming); settings = incoming; }
        catch (Exception original)
        {
            var failures = new List<string>();
            void Restore(string name, Action restore) { try { restore(); } catch (Exception e) { failures.Add(name + ": " + e.Message); } }
            Restore("Tray-Mausrad", () => SetTrayScroll(old.Preferences.TrayScrollVolume));
            if (!demo && autostartChanged) Restore("Autostart", () => SetAutostart(old.Preferences.StartWithWindows));
            Restore("Bisherige Shortcuts", () => RegisterShortcuts(old));
            if (failures.Count > 0) throw new InvalidOperationException(original.Message + " Wiederherstellung unvollständig: " + string.Join("; ", failures), original);
            throw;
        }
        RebuildTray(); return true;
    }
    static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("SetupPulse", $"\"{Environment.ProcessPath}\" {StartupBehavior.AutostartArgument}"); else key.DeleteValue("SetupPulse", false);
        key.DeleteValue(RuntimeIdentity.LegacyAppName, false);
    }
    async Task<T> Run<T>(Func<T> action, bool mixerAction = false)
    {
        if (busy) throw new InvalidOperationException("Ein Wechsel läuft bereits."); busy = true; busyMixer = mixerAction; Post(new { @event = "busy", data = true, scope = mixerAction ? "mixer" : "switch" });
        if (!mixerAction) homeAssistant?.Update(homeAssistantSnapshot with { Busy = true, CurrentId = null });
        try { return await Task.Run(() => Program.Locked(action)); } finally { busy = false; busyMixer = false; Post(new { @event = "busy", data = false, scope = mixerAction ? "mixer" : "switch" }); }
    }
    async Task<object?> ApplyProfile(string id)
    {
        var p = settings.Profiles.Single(p => p.Id == id); var f = ProfileAudioSelection.Resolve(p, settings);
        var t = await Run(() => engine.Apply(p, f).GetAwaiter().GetResult()); activeProfile = id;
        if (t != null) ShowWindow(); return t;
    }
    async Task<object?> ApplyAudio(string id) { var f = settings.AudioFavorites.Single(f => f.Id == id); return await Run(() => engine.Apply(null, f).GetAwaiter().GetResult()); }
    sealed record MixerEndpointRequest(string DeviceId, float? Volume = null, bool? Muted = null, string? DefaultRole = null);
    sealed record MixerSessionRequest(string DeviceId, string SessionId, uint ProcessId, float? Volume = null, bool? Muted = null);
    sealed record MixerRouteRequest(string DeviceId, string SessionId, uint ProcessId, string? TargetDeviceId = null);
    void CheckMixerChange() { if (busy || engine.Pending != null) throw new InvalidOperationException("Bitte zuerst den laufenden Monitorwechsel abschließen."); }
    async Task<object> MixerEndpoint(MixerEndpointRequest request)
    {
        CheckMixerChange();
        return await Run(() => { if (demo) demoMixer!.SetEndpoint(request.DeviceId, request.Volume, request.Muted, request.DefaultRole); else AudioMixer.SetEndpoint(request.DeviceId, request.Volume, request.Muted, request.DefaultRole); return true; }, mixerAction: true);
    }
    async Task<object> MixerSessionChange(MixerSessionRequest request)
    {
        CheckMixerChange();
        return await Run(() => { if (demo) demoMixer!.SetSession(request.DeviceId, request.SessionId, request.ProcessId, request.Volume, request.Muted); else AudioMixer.SetSession(request.DeviceId, request.SessionId, request.ProcessId, request.Volume, request.Muted); return true; }, mixerAction: true);
    }
    async Task<object> MixerRoute(MixerRouteRequest request)
    {
        CheckMixerChange();
        return await Run(() => { if (demo) demoMixer!.Route(request.DeviceId, request.SessionId, request.ProcessId, request.TargetDeviceId); else AudioMixer.Route(request.DeviceId, request.SessionId, request.ProcessId, request.TargetDeviceId); return true; }, mixerAction: true);
    }
    Task ChangeDefaultVolume(int steps)
    {
        CheckMixerChange();
        return Run(() => { var device = hardware.AudioInventory().FirstOrDefault(d => d.Available && !d.Input && d.Default) ?? throw new DeviceUnavailableException("Kein Standard-Ausgabegerät verfügbar."); if (demo) demoMixer!.SetEndpoint(device.Id, Math.Clamp(device.Volume + steps * 2, 0, 100), null, null); else AudioMixer.SetEndpoint(device.Id, Math.Clamp(device.Volume + steps * 2, 0, 100), null, null); return true; }, mixerAction: true);
    }
    async Task QueueVolume(int steps)
    {
        queuedVolumeSteps += steps;
        if (changingVolume) return;
        changingVolume = true;
        try { while (queuedVolumeSteps != 0) { int next = queuedVolumeSteps; queuedVolumeSteps = 0; await ChangeDefaultVolume(next); } }
        finally { queuedVolumeSteps = 0; changingVolume = false; volumeRefresh.Stop(); volumeRefresh.Start(); }
    }
    void UpdateTrayScroll() => SetTrayScroll(settings.Preferences.TrayScrollVolume);
    void SetTrayScroll(bool enabled)
    {
        if (!enabled || demo) { trayVolumeScroll?.Dispose(); trayVolumeScroll = null; }
        else trayVolumeScroll ??= new TrayVolumeScroll(tray, steps => { if (!IsDisposed && IsHandleCreated) BeginInvoke(async () => await Safe(() => QueueVolume(steps), refreshState: false)); });
    }
    object OpenAudioSettings(string target)
    {
        if (demo) throw new InvalidOperationException("Windows-Audioeinstellungen sind in der Demo nicht verfügbar.");
        var info = target switch {
            "sound" => new ProcessStartInfo("ms-settings:sound"),
            "mixer" => new ProcessStartInfo("sndvol.exe"),
            "playback" => new ProcessStartInfo("control.exe", "mmsys.cpl,,0"),
            "recording" => new ProcessStartInfo("control.exe", "mmsys.cpl,,1"),
            "sounds" => new ProcessStartInfo("control.exe", "mmsys.cpl,,2"),
            _ => throw new InvalidDataException("Unbekannte Audioeinstellung.")
        };
        info.UseShellExecute = true; Process.Start(info); return true;
    }
    sealed record AlignmentRequest(string Id, List<DisplaySetting> Monitors);
    async Task<object> BeginAlignment() { var transaction = await Run(engine.BeginAlignment); activeProfile = null; return transaction; }
    async Task<object> UpdateAlignment(AlignmentRequest request) => await Run(() => engine.UpdateAlignment(request.Id, request.Monitors));
    object Confirm(string id)
    {
        if (busy) throw new InvalidOperationException("Der Monitorwechsel wird noch angewendet. Bitte warten.");
        var t = Program.Locked(() => engine.Confirm(id));
        if (t.LiveAlignment) return true;
        settings = settings with { Profiles = settings.Profiles.Select(p => p.Id == t.ProfileId ? p with { VerifiedHash = t.Hash } : p).ToList() };
        store.Write("settings.json", settings); return true;
    }
    async Task<object> Rollback() { await Run(() => { if (engine.Pending is { } t) engine.Rollback(t); return true; }); activeProfile = null; return true; }
    async Task<object> Rescue() { await Run(() => { engine.Rescue(); return true; }); activeProfile = null; return true; }
    object Export()
    {
        using var dialog = new SaveFileDialog { Filter = "SetupPulse JSON|*.json", FileName = "SetupPulse-Profile.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, Json.Encode(settings with { Profiles = settings.Profiles.Select(p => p with { VerifiedHash = null }).ToList(), Preferences = settings.Preferences with { StartWithWindows = false } }));
        return true;
    }
    object Import()
    {
        using var dialog = new OpenFileDialog { Filter = "SetupPulse JSON|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        if (new FileInfo(dialog.FileName).Length > 1_000_000) throw new InvalidDataException("Datei ist zu groß.");
        var imported = Json.Decode<Settings>(File.ReadAllText(dialog.FileName));
        Validation.Settings(imported);
        // Import adds profiles; local preferences and hotkeys remain under this user's control.
        var favoriteIds = imported.AudioFavorites.ToDictionary(f => f.Id, _ => Guid.NewGuid().ToString("N"));
        return Save(settings with { AudioFavorites = [.. settings.AudioFavorites, .. imported.AudioFavorites.Select(f => f with { Id = favoriteIds[f.Id], Shortcut = null })], Profiles = [.. settings.Profiles, .. imported.Profiles.Select(p => p with { Id = Guid.NewGuid().ToString("N"), Shortcut = null, VerifiedHash = null, AudioFavoriteId = p.AudioFavoriteId == null ? null : favoriteIds[p.AudioFavoriteId] })] });
    }
    object ExitApp() { exiting = true; Close(); return true; }
    object CaptureShortcut(string? id)
    {
        shortcutCapture?.Dispose(); shortcutCapture = null; captureId = id;
        if (id != null)
        {
            if (id.Length > 80) throw new InvalidDataException("Ungültige Eingabekennung.");
            shortcutCapture = new ShortcutCapture(Handle, shortcut => BeginInvoke(() => { if (captureId == id) Post(new { @event = "shortcutCaptured", data = new { captureId = id, shortcut } }); }));
            shortcutCapture.Start();
        }
        return true;
    }
    object ReleaseShortcut(string id) { if (captureId == id) CaptureShortcut(null); return true; }
    object CloseQuickMixer() { DismissQuickMixer(); return true; }
    object OpenFullMixer() { ShowAudio("mixer"); return true; }
    object ApplyWindowTheme(bool dark) { WindowTheme.Apply(Handle, dark); if (quickMixerWindow?.IsHandleCreated == true) WindowTheme.Apply(quickMixerWindow.Handle, dark); return true; }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (settings != null) WindowTheme.Apply(Handle, WindowTheme.IsDark(settings.Preferences.Theme));
    }
    static string Licenses()
    {
        using var own = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("OwnLicense")!);
        using var third = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Licenses")!);
        return own.ReadToEnd() + "\n\n" + third.ReadToEnd();
    }
    void RegisterShortcuts(Settings s)
    {
        if (demo) return;
        foreach (var id in hotkeys.Keys) UnregisterHotKey(Handle, id); hotkeys.Clear();
        var entries = new List<(string? Key, Func<Task> Action)>();
        entries.AddRange(s.Profiles.Select(p => (p.Shortcut, (Func<Task>)(async () => { await ApplyProfile(p.Id); }))));
        entries.AddRange(s.AudioFavorites.Select(f => (f.Shortcut, (Func<Task>)(async () => { await ApplyAudio(f.Id); }))));
        entries.Add((s.Preferences.NextAudioShortcut, () => Cycle(1))); entries.Add((s.Preferences.PreviousAudioShortcut, () => Cycle(-1))); entries.Add((s.Preferences.RescueShortcut, async () => { await Rescue(); }));
        entries.Add((s.Preferences.MixerShortcut, () => { ShowAudio("mixer"); return Task.CompletedTask; }));
        entries.Add((s.Preferences.QuickMixerShortcut, () => { ShowAudio("quickMixer"); return Task.CompletedTask; }));
        entries.Add((s.Preferences.SettingsShortcut, () => { ShowAudio("settings"); return Task.CompletedTask; }));
        entries.Add((s.Preferences.VolumeUpShortcut, () => QueueVolume(1)));
        entries.Add((s.Preferences.VolumeDownShortcut, () => QueueVolume(-1)));
        try
        {
            int id = 100;
            foreach (var (key, action) in entries.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                var (mod, vk) = SetupPulse.Core.Shortcut.Parse(key!);
                if (!RegisterHotKey(Handle, id, mod | 0x4000, vk))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(error == 1409
                        ? $"{key} wird bereits von Windows oder einem anderen Programm verwendet. Prüfe auch Tastenkombinationen alter Desktop-Verknüpfungen. Windows-Fehler 1409."
                        : $"{key} konnte nicht registriert werden. Windows-Fehler {error}: {new Win32Exception(error).Message}");
                }
                hotkeys.Add(id++, (action, key != s.Preferences.VolumeUpShortcut && key != s.Preferences.VolumeDownShortcut));
            }
        }
        catch { foreach (var id in hotkeys.Keys) UnregisterHotKey(Handle, id); hotkeys.Clear(); throw; }
    }
    async Task Cycle(int direction)
    {
        var devices = await Task.Run(hardware.AudioInventory);
        await ApplyAudio(AudioMatching.Cycle(settings.AudioFavorites, devices, direction).Id);
    }
    async Task Safe(Func<Task> action, bool refreshState = true)
    {
        try { await action(); } catch (Exception e) { tray.ShowBalloonTip(6000, "SetupPulse", e.GetBaseException().Message, ToolTipIcon.Warning); Post(new { @event = "error", error = e.GetBaseException().Message }); }
        if (refreshState) await SendState();
    }
    void RebuildTray()
    {
        var menu = new DarkTrayMenu(); menu.Items.Add("SetupPulse öffnen", null, (_, _) => OpenMainWindow());
        menu.Items.Add("Audio-Mixer öffnen", null, (_, _) => ShowAudio("mixer"));
        menu.Items.Add("Schnellmixer öffnen", null, (_, _) => ShowAudio("quickMixer"));
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(new ToolStripMenuItem("Setups") { Enabled = false });
        foreach (var p in settings.Profiles) menu.Items.Add(new ToolStripMenuItem(p.Name, null, async (_, _) => await Safe(async () => { await ApplyProfile(p.Id); })) { Tag = p, ShortcutKeyDisplayString = p.Shortcut ?? "—", ShowShortcutKeys = true });
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(new ToolStripMenuItem("Audiofavoriten") { Enabled = false });
        foreach (var f in settings.AudioFavorites) menu.Items.Add(new ToolStripMenuItem(f.Name, null, async (_, _) => await Safe(async () => { await ApplyAudio(f.Id); })) { Tag = f, ShortcutKeyDisplayString = f.Shortcut ?? "—", ShowShortcutKeys = true });
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(new ToolStripMenuItem("Sichere Konfiguration wiederherstellen", null, async (_, _) => await Safe(async () => { await Rescue(); })) { ShortcutKeyDisplayString = settings.Preferences.RescueShortcut ?? "", ShowShortcutKeys = true });
        menu.Items.Add("Beenden", null, (_, _) => ExitApp()); var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; old?.Dispose();
        foreach (ToolStripItem item in menu.Items) if (item is ToolStripMenuItem) item.Padding = new Padding(3, 6, 3, 6);
        menu.Opening += async (_, _) => {
            // Invalidate old checks while refreshing, including external Windows changes.
            trayInventory = null; UpdateTrayChecks();
            try { trayInventory = await Task.Run(hardware.Inventory); }
            catch { trayInventory = null; }
            if (!IsDisposed && tray.ContextMenuStrip == menu) UpdateTrayChecks();
        };
        UpdateTrayChecks();
    }
    void UpdateTrayChecks()
    {
        if (tray.ContextMenuStrip is not { } menu) return;
        bool stable = !busy && engine.Pending == null;
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is not ToolStripMenuItem entry) continue;
            entry.Checked = trayInventory != null && stable && (entry.Tag switch {
                SetupProfile profile => ProfileMatching.Matches(profile, settings, trayInventory),
                AudioFavorite favorite => AudioMatching.Matches(favorite, trayInventory.Audio),
                _ => false
            });
        }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && hotkeys.TryGetValue(m.WParam.ToInt32(), out var entry)) _ = Safe(entry.Action, entry.RefreshState);
        if (m.Msg == 0x007E && IsHandleCreated) BeginInvoke(async () => await SendState());
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                void Cleanup(Action action) { try { action(); } catch (Exception e) { Debug.WriteLine("SetupPulse cleanup: " + e); } }
                Cleanup(() => trayVolumeScroll?.Dispose());
                Cleanup(() => shortcutCapture?.Dispose());
                Cleanup(() => homeAssistant?.Dispose());
                Cleanup(() => updates?.Dispose());
                Cleanup(shutdown.Cancel);
                SystemEvents.PowerModeChanged -= PowerChanged;
                Cleanup(refresh.Dispose);
                Cleanup(volumeRefresh.Dispose);
                Cleanup(trayClicks.Dispose);
                foreach (var id in hotkeys.Keys) if (IsHandleCreated) UnregisterHotKey(Handle, id);
                Cleanup(tray.Dispose); Cleanup(trayIcon.Dispose); Cleanup(appIcon.Dispose); Cleanup(web.Dispose); Cleanup(() => quickMixerWindow?.Dispose()); Cleanup(shutdown.Dispose);
            }
        }
        finally { base.Dispose(disposing); }
    }
}
