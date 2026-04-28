using PerfConverter.Entry;
using System.Runtime.InteropServices;

namespace PerfConverter.Persistence.Plank;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(Func<string, ITracePersister> traceBatcherFactory, Func<string, IPersister<StackRange>> stackRangeBatcherFactory) : IDisposable
{
    readonly Dictionary<string, ITracePersister> _tracePersister = [];
    readonly Dictionary<string, IPersister<StackRange>> _stackRangePersister = [];

    public ITracePersister CreateTraceBatcher(string key)
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
        foreach (var (key, entry) in _tracePersister)
        {
            entry.Dispose();
            PerfConverterMetrics.FileClosed(key);
        }

        foreach (var (key, entry) in _stackRangePersister)
        {
            entry.Dispose();
            PerfConverterMetrics.FileClosed(key);
        }
    }

    public static ParquetPersistenceLifetime Create(
       string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        static Action<int> CreateFlushNotifier(string key)
            => flushedCount => PerfConverterMetrics.FileFlushed(key, flushedCount);

        return new ParquetPersistenceLifetime(
            traceBatcherFactory: (key) =>
            {
                PerfConverterMetrics.FileOpened(key);
                var path = Path.Combine(outputDirectory, key);
                var dir = Path.GetDirectoryName(path)!; // key can be a path.
                Directory.CreateDirectory(dir);
                return ParquetTracePersistence.Create(path, CreateFlushNotifier(key));
            },
            stackRangeBatcherFactory: (key) =>
            {
                PerfConverterMetrics.FileOpened(key);
                var path = Path.Combine(outputDirectory, key);
                var dir = Path.GetDirectoryName(path)!; // key can be a path.
                Directory.CreateDirectory(dir);
                return ParquetStackRangePersistence.Create(path, CreateFlushNotifier(key));
            });
    }
}
