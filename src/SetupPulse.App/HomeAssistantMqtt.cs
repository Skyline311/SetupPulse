using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Formatter;
using SetupPulse.Core;

namespace SetupPulse.App;

internal record HomeAssistantConnection(bool Connected, string State, string? Error = null);

internal sealed class HomeAssistantMqtt : IDisposable
{
    readonly HomeAssistantOptions options;
    readonly string password, version;
    readonly Func<string, CancellationToken, Task> activate;
    readonly Func<string, string, CancellationToken, Task>? activateAudio;
    readonly Action changed;
    readonly CancellationTokenSource stop = new();
    readonly SemaphoreSlim pulse = new(0, 1);
    sealed record Command(string Kind, string Payload);
    readonly Channel<Command> commands = Channel.CreateBounded<Command>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    readonly object sync = new();
    HomeAssistantSnapshot snapshot = new([], null, false);
    HomeAssistantConnection connection = new(false, "Deaktiviert");
    IMqttClient? client;
    string? commandError;
    int forceDiscovery, executing, disposed;
    readonly Task networkTask, commandTask;

    public HomeAssistantMqtt(HomeAssistantOptions options, string password, string version,
        Func<string, CancellationToken, Task> activate, Action changed,
        Func<string, string, CancellationToken, Task>? activateAudio = null)
    {
        HomeAssistantProtocol.Validate(options);
        this.options = options; this.password = password; this.version = version;
        this.activate = activate; this.changed = changed;
        this.activateAudio = activateAudio;
        networkTask = options.Enabled ? Task.Run(NetworkLoop) : Task.CompletedTask;
        commandTask = options.Enabled ? Task.Run(CommandLoop) : Task.CompletedTask;
    }
    public HomeAssistantConnection Connection => Volatile.Read(ref connection);
    public void Update(HomeAssistantSnapshot value)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        lock (sync)
        {
            if (snapshot.TransactionId != value.TransactionId) commandError = null;
            snapshot = value;
        }
        Signal();
    }
    void Signal() { if (Volatile.Read(ref disposed) != 0) return; try { pulse.Release(); } catch (SemaphoreFullException) { } }
    void Status(bool connected, string state, string? error = null)
    {
        Volatile.Write(ref connection, new(connected, state, error));
        try { changed(); } catch (InvalidOperationException) { }
    }
    void CommandError(string? error) { lock (sync) commandError = error; Signal(); }
    async Task CommandLoop()
    {
        try
        {
            await foreach (var command in commands.Reader.ReadAllAsync(stop.Token))
            {
                Interlocked.Exchange(ref executing, 1);
                try
                {
                    HomeAssistantSnapshot current;
                    lock (sync) current = snapshot;
                    string id = command.Kind == "setup" ? HomeAssistantProtocol.ResolveCommand(current, command.Payload, false)
                        : HomeAssistantProtocol.ResolveAudioCommand(current, command.Kind, command.Payload, false);
                    CommandError(null); Signal();
                    if (command.Kind == "setup") await activate(id, stop.Token);
                    else if (activateAudio != null) await activateAudio(command.Kind, id, stop.Token);
                    else throw new InvalidOperationException("Audio-Fernsteuerung ist nicht verfügbar.");
                    CommandError(null);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (Exception e) { CommandError(e.Message); }
                finally { Interlocked.Exchange(ref executing, 0); Signal(); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    async Task NetworkLoop()
    {
        int failures = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                Status(false, "Verbinde …");
                await Session(); failures = 0;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception)
            {
                Status(false, "Nicht verbunden", "MQTT-Verbindung fehlgeschlagen. Prüfe Broker, Port, Anmeldung und TLS.");
            }
            if (stop.IsCancellationRequested) break;
            int seconds = new[] { 1, 2, 5, 10, 30 }[Math.Min(failures++, 4)];
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    async Task Session()
    {
        var factory = new MqttClientFactory();
        using var current = factory.CreateMqttClient();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool ready = false;
        string root = HomeAssistantProtocol.Topic(options);
        Volatile.Write(ref client, current);
        current.DisconnectedAsync += _ => { ended.TrySetResult(); return Task.CompletedTask; };
        current.ApplicationMessageReceivedAsync += e => {
            if (stop.IsCancellationRequested) return Task.CompletedTask;
            var message = e.ApplicationMessage;
            if (message.Topic == "homeassistant/status" || message.Topic == options.DiscoveryPrefix + "/status")
            {
                if (message.Payload.Length <= 32 && message.ConvertPayloadToString() == "online") { Interlocked.Exchange(ref forceDiscovery, 1); Signal(); }
            }
            else if (ready && !message.Retain)
            {
                string? kind = message.Topic == root + "/setup/set" ? "setup"
                    : message.Topic == root + "/audio/output/set" ? "output"
                    : message.Topic == root + "/audio/input/set" ? "input"
                    : message.Topic == root + "/audio/favorite/set" ? "favorite" : null;
                if (kind == null) return Task.CompletedTask;
                if (message.Payload.Length > 4096) CommandError("Ungültiger Auswahl-Befehl.");
                else if (Volatile.Read(ref executing) != 0 || !commands.Writer.TryWrite(new(kind, message.ConvertPayloadToString())))
                    CommandError("Ein Wechsel läuft bereits.");
            }
            return Task.CompletedTask;
        };
        var builder = new MqttClientOptionsBuilder()
            .WithClientId("setuppulse_" + options.InstanceId).WithTcpServer(options.Host, options.Port)
            .WithProtocolVersion(MqttProtocolVersion.V311).WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(8)).WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithWillTopic(root + "/availability").WithWillPayload("offline").WithWillRetain();
        if (options.Username.Length > 0 || password.Length > 0) builder.WithCredentials(options.Username, password);
        if (options.UseTls) builder.WithTlsOptions(tls => tls.UseTls());
        try
        {
            await current.ConnectAsync(builder.Build(), stop.Token);
            var subscription = factory.CreateSubscribeOptionsBuilder().WithTopicFilter(root + "/setup/set")
                .WithTopicFilter(root + "/audio/output/set").WithTopicFilter(root + "/audio/input/set")
                .WithTopicFilter(root + "/audio/favorite/set")
                .WithTopicFilter("homeassistant/status");
            if (options.DiscoveryPrefix != "homeassistant") subscription.WithTopicFilter(options.DiscoveryPrefix + "/status");
            var result = await current.SubscribeAsync(subscription.Build(), stop.Token);
            if (result.Items.Any(item => (int)item.ResultCode >= 128)) throw new IOException("MQTT-Abonnement abgewiesen.");
            var published = new Dictionary<string, string>();
            async Task PublishChanges(bool force)
            {
                HomeAssistantSnapshot state; string? error;
                lock (sync) { state = snapshot; error = commandError; }
                if (Volatile.Read(ref executing) != 0) state = state with { Busy = true, CurrentId = null };
                var messages = HomeAssistantProtocol.Discovery(options, state, version);
                messages[root + "/state"] = HomeAssistantProtocol.State(state, error);
                foreach (var (topic, payload) in messages)
                {
                    if (force || !published.TryGetValue(topic, out string? previous) || previous != payload)
                    {
                        await Publish(current, topic, payload, stop.Token);
                        published[topic] = payload;
                    }
                }
            }
            await PublishChanges(true);
            await Publish(current, root + "/availability", "online", stop.Token);
            ready = true; Status(true, "Verbunden");
            while (!stop.IsCancellationRequested && !ended.Task.IsCompleted)
            {
                await Task.WhenAny(pulse.WaitAsync(session.Token), ended.Task);
                if (ended.Task.IsCompleted) break;
                await PublishChanges(Interlocked.Exchange(ref forceDiscovery, 0) != 0);
            }
        }
        finally
        {
            ready = false; session.Cancel();
            if (current.IsConnected)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await Publish(current, root + "/availability", "offline", deadline.Token);
                    await current.DisconnectAsync(new MqttClientDisconnectOptions(), deadline.Token);
                }
                catch (Exception) { }
            }
            Volatile.Write(ref client, null);
            if (!stop.IsCancellationRequested) Status(false, "Verbindung unterbrochen");
        }
    }
    static Task Publish(IMqttClient current, string topic, string payload, CancellationToken token) =>
        current.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag().Build(), token);

    public async Task StopAsync()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        stop.Cancel();
        try { await Task.WhenAll(networkTask, commandTask).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { }
        Dispose();
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        Volatile.Read(ref client)?.Dispose(); // An abrupt application exit also triggers the broker's offline will.
        _ = Task.WhenAll(networkTask, commandTask).ContinueWith(_ => { stop.Dispose(); pulse.Dispose(); }, TaskScheduler.Default);
    }
}
