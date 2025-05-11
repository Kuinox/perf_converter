using PerfConverter.Entry;

namespace PerfConverter.Persistence.ParquetDotNet;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(
    Batcher<SymbolEntry> symbolBatcher,
    Batcher<AddressEntry> addressBatcher,
    Batcher<TraceSampleEntry> traceBatcher) : IPersistenceLifetime
{
    public IPersister<SymbolEntry> SymbolBatcher => symbolBatcher;   
    public IPersister<AddressEntry> AddressBatcher => addressBatcher;
    public IPersister<TraceSampleEntry> TraceBatcher => traceBatcher;

    public async ValueTask DisposeAsync()
    {
        await traceBatcher.DisposeAsync();
        await addressBatcher.DisposeAsync();
        await symbolBatcher.DisposeAsync();
    }
}