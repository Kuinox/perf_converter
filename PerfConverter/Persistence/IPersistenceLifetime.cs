namespace PerfConverter.Persistence;

/// <summary>
/// Interface for managing the lifetime of persistence components
/// </summary>
public interface IPersistenceLifetime : IAsyncDisposable
{
    /// <summary>
    /// Gets the symbol batcher
    /// </summary>
    IPersiter<Entry.SymbolEntry> SymbolBatcher { get; }
    
    /// <summary>
    /// Gets the address batcher
    /// </summary>
    IPersiter<Entry.AddressEntry> AddressBatcher { get; }
    
    /// <summary>
    /// Gets the trace batcher
    /// </summary>
    IPersiter<Entry.TraceSampleEntry> TraceBatcher { get; }
}