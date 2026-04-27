using PerfConverter.Entry;
using System.Runtime.InteropServices;

namespace PerfConverter.Persistence.Plank;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(Func<string, IPersister<TraceEntry>> traceBatcherFactory, Func<string, Batcher<StackRange>> stackRangeBatcherFactory) : IDisposable
{
    readonly Dictionary<string, IPersister<TraceEntry>> _tracePersister = [];
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

    public void Dispose()
    {
        foreach (var entry in _tracePersister.Values)
            entry.Dispose();

        foreach (var entry in _stackRangePersister.Values)
            entry.Dispose();
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
                return ParquetTracePersistence.Create(path);
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
                var persister = ParquetStackRangePersistence.Create(path);
                return Batcher<StackRange>.Create(persister, batchSize, key);
            });
    }
}
