using Temp.Core;

namespace PerfConverter.Persistence;

/// <summary>
/// Interface for managing the lifetime of persistence components
/// </summary>
public interface IPersistenceLifetime : IAsyncDisposable
{
    IPersister<Entry.StringEntry> SymbolBatcher { get; }

    IPersister<Entry.StringEntry> DsoBatcher { get; }

    IPersister<Entry.StringEntry> CommBatcher { get; }
    
    IPersister<Entry.StringEntry> EventBatcher { get; }
    
    IPersister<Entry.AddressEntry> AddressBatcher { get; }

    IPersister<Entry.TraceSampleEntry> CreateTraceBatcher(string key);
}