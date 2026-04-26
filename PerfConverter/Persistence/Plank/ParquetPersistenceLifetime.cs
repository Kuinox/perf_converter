using PerfConverter.Entry;
using System.Runtime.InteropServices;

namespace PerfConverter.Persistence.Plank;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(Func<string, Batcher<TraceEntry>> traceBatcherFactory, Func<string, Batcher<StackRange>> stackRangeBatcherFactory) : IAsyncDisposable
{
    const int MaxTraceRowGroupSize = 250_000;
    readonly Dictionary<string, Batcher<TraceEntry>> _tracePersister = [];
    readonly Dictionary<string, Batcher<StackRange>> _stackRangePersister = [];

    public IPersister<TraceEntry> CreateTraceBatcher(string key)
    {
        ref var persistence = ref CollectionsMarshal.GetValueRefOrAddDefault(_tracePersister, key, out _);
        persistence ??= traceBatcherFactory(key);
        return persistence;
    }

    public IPersister<StackRange> CreateStackRangeBatcher(string key)
    {
        ref var persistence = ref CollectionsMarshal.GetValueRefOrAddDefault(_stackRangePersister, key, out _);
        persistence ??= stackRangeBatcherFactory(key);
        return persistence;
    }

    public async ValueTask DisposeAsync()
    {
        var traceTasks = _tracePersister.Values.Select(entry => entry.DisposeAsync().AsTask());
        var stackRangeTasks = _stackRangePersister.Values.Select(entry => entry.DisposeAsync().AsTask());
        
        await Task.WhenAll(traceTasks.Concat(stackRangeTasks));
    }

    public static ParquetPersistenceLifetime Create(
       string outputDirectory,
       int batchSize)
    {
        Directory.CreateDirectory(outputDirectory);

        return new ParquetPersistenceLifetime(
            traceBatcherFactory: (key) =>
            {
                if (Configuration.EnableProgressSignals)
                {
                    Console.Error.WriteLine($"FILE_STATUS|{key}|BUFFERING");
                }
                var path = Path.Combine(outputDirectory, key);
                var dir = Path.GetDirectoryName(path)!; // key can be a path.
                Directory.CreateDirectory(dir);
                var rowBatchSize = Math.Min(batchSize, MaxTraceRowGroupSize);
                var persister = ParquetTracePersistence.Create(path, rowBatchSize).GetAwaiter().GetResult();
                return Batcher<TraceEntry>.Create(persister, batchSize, key);
            },
            stackRangeBatcherFactory: (key) =>
            {
                if (Configuration.EnableProgressSignals)
                {
                    Console.Error.WriteLine($"FILE_STATUS|{key}|BUFFERING");
                }
                var path = Path.Combine(outputDirectory, key);
                var dir = Path.GetDirectoryName(path)!; // key can be a path.
                Directory.CreateDirectory(dir);
                var persister = ParquetStackRangePersistence.Create(path).GetAwaiter().GetResult();
                return Batcher<StackRange>.Create(persister, batchSize, key);
            });
    }
}
