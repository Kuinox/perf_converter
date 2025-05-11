namespace PerfConverter.Persistence;

/// <summary>
/// Interface for managing the lifetime of persistence components
/// </summary>
public interface IPersistenceLifetime : IAsyncDisposable
{
    /// <summary>
    /// Gets the symbol batcher
    /// </summary>
    IPersister<Entry.SymbolEntry> SymbolBatcher { get; }
    
    /// <summary>
    /// Gets the address batcher
    /// </summary>
    IPersister<Entry.AddressEntry> AddressBatcher { get; }
    
    /// <summary>
    /// Gets the trace batcher
    /// </summary>
    IPersister<Entry.TraceSampleEntry> TraceBatcher { get; }
}