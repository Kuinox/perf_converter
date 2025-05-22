using PerfConverter.Entry;
using System.Runtime.InteropServices;
using Temp.Core;

namespace PerfConverter.Persistence.ParquetDotNet;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(
    Batcher<StringEntry> symbolBatcher,
    Batcher<StringEntry> commBatcher,
    Batcher<AddressEntry> addressBatcher,
    Func<string, Batcher<TraceSampleEntry>> traceBatcherFactory) : IPersistenceLifetime
{
    readonly Dictionary<string, Batcher<TraceSampleEntry>> _tracePersister = [];

    public IPersister<StringEntry> SymbolBatcher => symbolBatcher;
    public IPersister<StringEntry> CommBatcher => commBatcher;
    public IPersister<AddressEntry> AddressBatcher => addressBatcher;
    public IPersister<TraceSampleEntry> CreateTraceBatcher(string key)
    {
        var persistence = CollectionsMarshal.GetValueRefOrAddDefault(_tracePersister, key, out _);
        persistence ??= traceBatcherFactory(key);
        return persistence;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _tracePersister.Values)
        {
            await entry.DisposeAsync();
        }
        await commBatcher.DisposeAsync();
        await addressBatcher.DisposeAsync();
        await symbolBatcher.DisposeAsync();
    }
}