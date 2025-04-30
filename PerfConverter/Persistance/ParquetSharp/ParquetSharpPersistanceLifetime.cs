using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetSharp;

/// <summary>
/// Manages the lifetime of ParquetSharp persistence components
/// </summary>
public class ParquetSharpPersistanceLifetime(
    Batcher<SymbolEntry> symbolBatcher,
    Batcher<AddressEntry> addressBatcher,
    Batcher<TraceSampleEntry> traceBatcher) : IPersistanceLifetime
{
    public IPersiter<SymbolEntry> SymbolBatcher => symbolBatcher;
    public IPersiter<AddressEntry> AddressBatcher => addressBatcher;
    public IPersiter<TraceSampleEntry> TraceBatcher => traceBatcher;

    public void Dispose()
    {
        // Dispose batchers in reverse order of creation
        traceBatcher.Dispose();
        addressBatcher.Dispose();
        symbolBatcher.Dispose();
    }
}