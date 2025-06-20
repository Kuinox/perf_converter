using Temp.Core;

namespace PerfConverter.Persistence;

/// <summary>
/// Interface for managing the lifetime of persistence components
/// </summary>
public interface IPersistenceLifetime : IAsyncDisposable
{
    IPersister<Entry.TraceSampleEntry> CreateTraceBatcher(string key);
}