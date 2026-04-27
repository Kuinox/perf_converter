using PerfConverter.Entry;
using System.Runtime.InteropServices;

namespace PerfConverter.Persistence.Plank;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(Func<string, IPersister<TraceEntry>> traceBatcherFactory, Func<string, IPersister<StackRange>> stackRangeBatcherFactory) : IDisposable
{
    readonly Dictionary<string, IPersister<TraceEntry>> _tracePersister = [];
    readonly Dictionary<string, IPersister<StackRange>> _stackRangePersister = [];

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
       string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        static Action<int>? CreateFlushNotifier(string key)
        {
            if (!Configuration.EnableProgressSignals)
                return null;

            return flushedCount =>
            {
                if (flushedCount <= 0)
                    return;

                Console.Error.WriteLine($"FILE_STATUS|{key}|FLUSHING");
                Console.Error.WriteLine($"FILE_ACTIVITY|{key}|FLUSHED|{flushedCount}");
                Console.Error.WriteLine($"FILE_STATUS|{key}|BUFFERING");
            };
        }

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
                return ParquetTracePersistence.Create(path, CreateFlushNotifier(key));
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
                return ParquetStackRangePersistence.Create(path, CreateFlushNotifier(key));
            });
    }
}
