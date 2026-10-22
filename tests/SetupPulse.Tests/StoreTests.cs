using SetupPulse.Core;
using Xunit;

namespace SetupPulse.Tests;

public sealed class StoreTests
{
    [Fact]
    public async Task Concurrent_transaction_reads_do_not_block_atomic_replacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SetupPulse-tests-" + Guid.NewGuid().ToString("N"));
        var store = new Store(directory);
        store.Write("transaction.json", new Snapshot(0, new string('x', 16000)));
        using var start = new ManualResetEventSlim();
        try
        {
            var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < 300; i++)
                {
                    var snapshot = store.Read<Snapshot>("transaction.json");
                    Assert.NotNull(snapshot);
                    Assert.Equal(16000, snapshot.Payload.Length);
                    Assert.InRange(snapshot.Sequence, 0, 300);
                }
            })).ToArray();
            var writer = Task.Run(() =>
            {
                start.Wait();
                for (int i = 1; i <= 300; i++) store.Write("transaction.json", new Snapshot(i, new string('x', 16000)));
            });
            start.Set();
            await Task.WhenAll(readers.Append(writer));
            Assert.Equal(300, store.Read<Snapshot>("transaction.json")!.Sequence);
        }
        finally { Directory.Delete(directory, true); }
    }

    sealed record Snapshot(int Sequence, string Payload);
}
