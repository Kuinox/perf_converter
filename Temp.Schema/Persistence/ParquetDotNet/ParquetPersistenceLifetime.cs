using PerfConverter.Entry;
using System.Runtime.InteropServices;
using Temp.Core;

namespace PerfConverter.Persistence.ParquetDotNet;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(Func<string, Batcher<TraceEntry>> traceBatcherFactory) : IPersistenceLifetime
{
    readonly Dictionary<string, Batcher<TraceEntry>> _tracePersister = [];

    public IPersister<TraceEntry> CreateTraceBatcher(string key)
    {
        ref var persistence = ref CollectionsMarshal.GetValueRefOrAddDefault(_tracePersister, key, out _);
        persistence ??= traceBatcherFactory(key);
        return persistence;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _tracePersister.Values)
        {
            await entry.DisposeAsync();
        }
    }
}