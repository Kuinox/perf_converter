using PerfConverter.Entry;
using System.Runtime.InteropServices;

namespace PerfConverter.Persistence.Plank;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(Func<string, ITracePersister> traceBatcherFactory) : IDisposable
{
    readonly Dictionary<string, ITracePersister> _tracePersister = [];

    public ITracePersister CreateTraceBatcher(string key)
    {
        ref var persistence = ref CollectionsMarshal.GetValueRefOrAddDefault(_tracePersister, key, out _);
        persistence ??= traceBatcherFactory(key);
        return persistence;
    }

    public void Dispose()
    {
        foreach (var (key, entry) in _tracePersister)
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
                return new MeteredTracePersister(
                    key,
                    ParquetTracePersistence.Create(path, CreateFlushNotifier(key)));
            });
    }

    sealed class MeteredTracePersister(string key, ITracePersister inner) : ITracePersister
    {
        public unsafe void Persist(
            ulong entryId,
            PerfStructs.PerfDlFilterSample* sample,
            PerfStructs.PerfDlfilterAl* ip,
            PerfStructs.PerfDlfilterAl* address,
            ReadOnlyMemory<byte>? srcFilePath,
            uint lineNumber,
            ReadOnlyMemory<byte> eventName)
        {
            PerfConverterMetrics.FileBuffered(key, 1);
            inner.Persist(entryId, sample, ip, address, srcFilePath, lineNumber, eventName);
        }

        public void Dispose()
            => inner.Dispose();
    }
}
