using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Server;
using SetupPulse.App;
using SetupPulse.Core;

namespace SetupPulse.Tests;

public sealed class HomeAssistantMqttTests
{
    sealed class Broker : IAsyncDisposable
    {
        public int Port { get; }
        public MqttServer Server { get; }
        public ConcurrentQueue<(string Topic, string Payload)> Messages { get; } = new();
        public Broker()
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
            Port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            Server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
                .WithDefaultEndpoint().WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
                .WithDefaultEndpointBoundIPV6Address(IPAddress.IPv6Loopback).WithDefaultEndpointPort(Port).Build());
            Server.InterceptingPublishAsync += e => { Messages.Enqueue((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString())); return Task.CompletedTask; };
        }
        public async ValueTask DisposeAsync() { await Server.StopAsync(); Server.Dispose(); }
        public async Task<IMqttClient> Client()
        {
            var client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", Port).Build());
            return client;
        }
    }
    static HomeAssistantOptions Options(Broker broker) => new(true, "127.0.0.1", broker.Port, InstanceId: Guid.NewGuid().ToString("N"));
    static HomeAssistantSnapshot Snapshot => new([new("tv", "TV"), new("desk", "Büro")], "desk", false);
    static async Task Until(Func<bool> condition, int milliseconds = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(condition(), "Timed out waiting for the isolated MQTT test broker.");
    }
    static Task Publish(IMqttClient client, string topic, string payload, bool retain = false) =>
        client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag(retain).Build());

    [Fact] public async Task DiscoveryCommandsActualStateBirthRepublishAndGracefulOffline()
    {
        await using var broker = new Broker();
        broker.Server.ValidatingConnectionAsync += e => {
            if (e.ClientId.StartsWith("setuppulse_") && (e.UserName != "test-only-user" || e.Password != "test-only-password"))
                e.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };
        await broker.Server.StartAsync();
        var options = Options(broker) with { Username = "test-only-user" }; string root = HomeAssistantProtocol.Topic(options);
        int calls = 0; string? selected = null;
        using var mqtt = new HomeAssistantMqtt(options, "test-only-password", "test", (id, _) => { selected = id; Interlocked.Increment(ref calls); return Task.CompletedTask; }, () => { });
        mqtt.Update(Snapshot);
        await Until(() => mqtt.Connection.Connected);
        await Until(() => broker.Messages.Any(m => {
            if (!m.Topic.EndsWith("/setup/config")) return false;
            using var config = JsonDocument.Parse(m.Payload);
            return config.RootElement.GetProperty("options").EnumerateArray().Any(option => option.GetString() == "Büro");
        }));
        using var controller = await broker.Client();
        await Publish(controller, root + "/setup/set", "TV");
        await Until(() => Volatile.Read(ref calls) == 1);
        Assert.Equal("tv", selected);
        mqtt.Update(Snapshot with { CurrentId = "tv" });
        await Until(() => broker.Messages.Any(m => {
            if (m.Topic != root + "/state") return false;
            using var state = JsonDocument.Parse(m.Payload);
            return state.RootElement.GetProperty("profile").GetString() == "TV";
        }));
        int discoveryBefore = broker.Messages.Count(m => m.Topic.EndsWith("/setup/config"));
        await Publish(controller, "homeassistant/status", "online");
        await Until(() => broker.Messages.Count(m => m.Topic.EndsWith("/setup/config")) > discoveryBefore);
        await mqtt.StopAsync();
        await Until(() => broker.Messages.LastOrDefault(m => m.Topic == root + "/availability").Payload == "offline");
    }

    [Fact] public async Task RetainedReconnectUnknownAndBusyCommandsNeverCauseExtraActivation()
    {
        await using var broker = new Broker(); await broker.Server.StartAsync();
        var options = Options(broker); string topic = HomeAssistantProtocol.Topic(options) + "/setup/set";
        using (var sender = await broker.Client()) await Publish(sender, topic, "TV", true);
        int calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var mqtt = new HomeAssistantMqtt(options, "", "test", async (_, token) => { Interlocked.Increment(ref calls); started.TrySetResult(); await release.Task.WaitAsync(token); }, () => { });
        mqtt.Update(Snapshot);
        await Until(() => mqtt.Connection.Connected);
        Assert.Equal(0, calls);
        using (var sender = await broker.Client())
        {
            await Publish(sender, topic, "Unknown");
            await Until(() => broker.Messages.Any(m => m.Topic.EndsWith("/state") && m.Payload.Contains("error" ) && m.Payload.Contains("Dieses Setup")));
            Assert.Equal(0, calls);
            await Publish(sender, topic, "TV"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Publish(sender, topic, "Büro");
            await Until(() => broker.Messages.Any(m => m.Topic.EndsWith("/state") && m.Payload.Contains("Ein Wechsel")));
            Assert.Equal(1, calls); release.TrySetResult();
        }
        await broker.Server.StopAsync();
        await Until(() => !mqtt.Connection.Connected);
        await broker.Server.StartAsync();
        await Until(() => mqtt.Connection.Connected);
        // No command is generated by reconnecting or by a stored retained command.
        Assert.Equal(1, calls);
        await mqtt.StopAsync();
    }

    [Fact] public async Task AuthenticationErrorsAreReportedWithoutLeakingCredentials()
    {
        await using var broker = new Broker();
        broker.Server.ValidatingConnectionAsync += e => { e.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.BadUserNameOrPassword; return Task.CompletedTask; };
        await broker.Server.StartAsync();
        using var mqtt = new HomeAssistantMqtt(Options(broker) with { Username = "test-only-user" }, "test-only-password", "test", (_, _) => throw new Exception("Must not run"), () => { });
        await Until(() => mqtt.Connection.Error != null);
        Assert.False(mqtt.Connection.Connected);
        Assert.DoesNotContain("test-only-password", Json.Encode(mqtt.Connection));
        await mqtt.StopAsync();
    }

    [Fact] public async Task FailedActivationReportsErrorAndNextSuccessClearsIt()
    {
        await using var broker = new Broker(); await broker.Server.StartAsync();
        var options = Options(broker); string root = HomeAssistantProtocol.Topic(options);
        bool fail = true; int calls = 0;
        using var mqtt = new HomeAssistantMqtt(options, "", "test", (_, _) => {
            Interlocked.Increment(ref calls);
            if (fail) throw new InvalidOperationException("Audio fehlt");
            return Task.CompletedTask;
        }, () => { });
        mqtt.Update(Snapshot); await Until(() => mqtt.Connection.Connected);
        using var controller = await broker.Client();
        await Publish(controller, root + "/setup/set", "TV");
        await Until(() => broker.Messages.LastOrDefault(m => m.Topic == root + "/state").Payload?.Contains("Audio fehlt") == true);
        Assert.Equal(1, calls);
        fail = false;
        await Publish(controller, root + "/setup/set", "TV");
        await Until(() => Volatile.Read(ref calls) == 2);
        await Until(() => {
            string? payload = broker.Messages.LastOrDefault(m => m.Topic == root + "/state").Payload;
            if (payload == null) return false;
            using var state = JsonDocument.Parse(payload);
            return state.RootElement.GetProperty("status").GetString() == "ready" && state.RootElement.GetProperty("error").GetString() == "";
        });
        await mqtt.StopAsync();
    }

    [Fact] public async Task DisabledIntegrationDoesNotConnect()
    {
        await using var broker = new Broker(); int connections = 0;
        broker.Server.ValidatingConnectionAsync += _ => { Interlocked.Increment(ref connections); return Task.CompletedTask; };
        await broker.Server.StartAsync();
        using var mqtt = new HomeAssistantMqtt(Options(broker) with { Enabled = false }, "", "test", (_, _) => throw new Exception("Must not run"), () => { });
        mqtt.Update(Snapshot);
        await mqtt.StopAsync();
        Assert.Equal(0, connections); Assert.Equal("Deaktiviert", mqtt.Connection.State);
    }

    [Fact] public async Task AbruptDisposalPublishesOfflineWillToObservers()
    {
        await using var broker = new Broker(); await broker.Server.StartAsync();
        var options = Options(broker); string topic = HomeAssistantProtocol.Topic(options) + "/availability";
        using var observer = await broker.Client();
        var availability = new ConcurrentQueue<string>();
        observer.ApplicationMessageReceivedAsync += e => { availability.Enqueue(e.ApplicationMessage.ConvertPayloadToString()); return Task.CompletedTask; };
        await observer.SubscribeAsync(new MqttClientFactory().CreateSubscribeOptionsBuilder().WithTopicFilter(topic).Build());
        using var mqtt = new HomeAssistantMqtt(options, "", "test", (_, _) => Task.CompletedTask, () => { });
        mqtt.Update(Snapshot); await Until(() => availability.Contains("online"));
        mqtt.Dispose();
        await Until(() => availability.Contains("offline"));
    }

    static HomeAssistantSnapshot AudioSnapshot => Snapshot with { Audio = new(
        [new("speakers", "Speakers"), new("headset", "Headset")], [new("mic", "Microphone")],
        [new("desk-audio", "Desk audio")], "speakers", "mic", "desk-audio") };

    [Fact] public async Task SeparateAudioTopicsDispatchOnlyAudioAndPublishActualState()
    {
        await using var broker = new Broker(); await broker.Server.StartAsync();
        var options = Options(broker); string root = HomeAssistantProtocol.Topic(options);
        int setupCalls = 0;
        var audioCalls = new ConcurrentQueue<(string Kind, string Id)>();
        var actual = AudioSnapshot with { Audio = AudioSnapshot.Audio! with { OutputId = null, InputId = null, FavoriteId = null } };
        HomeAssistantMqtt? created = null;
        using var mqtt = created = new HomeAssistantMqtt(options, "", "test", (_, _) => { Interlocked.Increment(ref setupCalls); return Task.CompletedTask; }, () => { },
            (kind, id, _) => {
                var audio = actual.Audio!;
                actual = actual with { Audio = kind switch {
                    "output" => audio with { OutputId = id }, "input" => audio with { InputId = id },
                    _ => audio with { FavoriteId = id }
                } };
                created!.Update(actual);
                audioCalls.Enqueue((kind, id)); return Task.CompletedTask;
            });
        mqtt.Update(actual); await Until(() => mqtt.Connection.Connected);
        await Until(() => broker.Messages.Any(m => m.Topic.EndsWith("/audio_output/config") && m.Payload.Contains("Headset")));
        using var controller = await broker.Client();
        foreach (var (kind, label, id) in new[] { ("output", "Headset", "headset"), ("input", "Microphone", "mic"), ("favorite", "Desk audio", "desk-audio") })
        {
            int before = audioCalls.Count;
            await Publish(controller, root + "/audio/" + kind + "/set", label);
            await Until(() => audioCalls.Count == before + 1);
            await Until(() => {
                string? payload = broker.Messages.LastOrDefault(m => m.Topic == root + "/state").Payload;
                if (payload == null) return false;
                using var state = JsonDocument.Parse(payload);
                return state.RootElement.GetProperty("status").GetString() == "ready"
                    && state.RootElement.GetProperty("audio_" + kind).GetString() == label;
            });
            Assert.Equal((kind, id), audioCalls.Last());
        }
        Assert.Equal(0, setupCalls);
        mqtt.Update(AudioSnapshot with { Audio = AudioSnapshot.Audio! with { OutputId = "headset", FavoriteId = null } });
        await Until(() => {
            string? payload = broker.Messages.LastOrDefault(m => m.Topic == root + "/state").Payload;
            if (payload == null) return false;
            using var state = JsonDocument.Parse(payload);
            return state.RootElement.GetProperty("audio_output").GetString() == "Headset"
                && state.RootElement.GetProperty("audio_favorite").GetString() == "None";
        });
        await mqtt.StopAsync();
    }

    [Fact] public async Task RetainedAudioCommandsReconnectAndBusySetupNeverReplayAudio()
    {
        await using var broker = new Broker(); await broker.Server.StartAsync();
        var options = Options(broker); string root = HomeAssistantProtocol.Topic(options);
        using var controller = await broker.Client();
        foreach (var (kind, label) in new[] { ("output", "Headset"), ("input", "Microphone"), ("favorite", "Desk audio") })
            await Publish(controller, root + "/audio/" + kind + "/set", label, true);
        int audioCalls = 0, setupCalls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var mqtt = new HomeAssistantMqtt(options, "", "test", async (_, token) => {
            Interlocked.Increment(ref setupCalls); started.TrySetResult(); await release.Task.WaitAsync(token);
        }, () => { }, (_, _, _) => { Interlocked.Increment(ref audioCalls); return Task.CompletedTask; });
        mqtt.Update(AudioSnapshot); await Until(() => mqtt.Connection.Connected);
        Assert.Equal(0, audioCalls);
        await Publish(controller, root + "/audio/output/set", "Microphone");
        await Until(() => broker.Messages.LastOrDefault(m => m.Topic == root + "/state").Payload?.Contains("nicht verf") == true);
        Assert.Equal(0, audioCalls);
        await Publish(controller, root + "/setup/set", "TV"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Publish(controller, root + "/audio/output/set", "Headset");
        await Until(() => broker.Messages.LastOrDefault(m => m.Topic == root + "/state").Payload?.Contains("Ein Wechsel") == true);
        Assert.Equal(0, audioCalls); release.TrySetResult();
        await broker.Server.StopAsync(); await Until(() => !mqtt.Connection.Connected);
        await broker.Server.StartAsync(); await Until(() => mqtt.Connection.Connected);
        Assert.Equal(0, audioCalls); Assert.Equal(1, setupCalls);
        await mqtt.StopAsync();
    }
}
