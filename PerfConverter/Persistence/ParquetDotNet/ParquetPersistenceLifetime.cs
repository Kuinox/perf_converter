using PerfConverter.Entry;
using Temp.Core;

namespace PerfConverter.Persistence.ParquetDotNet;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(
    Batcher<StringEntry> symbolBatcher,
    Batcher<StringEntry> commBatcher,
    Batcher<AddressEntry> addressBatcher,
    Batcher<TraceSampleEntry> traceBatcher) : IPersistenceLifetime
{
    public IPersister<StringEntry> SymbolBatcher => symbolBatcher;
    public IPersister<StringEntry> CommBatcher => commBatcher;
    public IPersister<AddressEntry> AddressBatcher => addressBatcher;
    public IPersister<TraceSampleEntry> TraceBatcher => traceBatcher;

    public async ValueTask DisposeAsync()
    {
        await traceBatcher.DisposeAsync();
        await commBatcher.DisposeAsync();
        await addressBatcher.DisposeAsync();
        await symbolBatcher.DisposeAsync();
    }
}