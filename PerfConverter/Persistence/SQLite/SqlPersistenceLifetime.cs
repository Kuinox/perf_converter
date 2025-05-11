using Microsoft.Data.Sqlite;
using PerfConverter.Entry;

namespace PerfConverter.Persistence.Sql;

/// <summary>
/// Manages the lifetime of SQLite persistence components
/// </summary>
public class SqlPersistenceLifetime : IPersistenceLifetime
{
    readonly SqliteConnection _connection;
    readonly Batcher<SymbolEntry> _symbolBatcher;
    readonly Batcher<AddressEntry> _addressBatcher;
    readonly Batcher<TraceSampleEntry> _traceBatcher;

    public SqlPersistenceLifetime(
        SqliteConnection connection,
        Batcher<SymbolEntry> symbolBatcher,
        Batcher<AddressEntry> addressBatcher,
        Batcher<TraceSampleEntry> traceBatcher)
    {
        _connection = connection;
        _symbolBatcher = symbolBatcher;
        _addressBatcher = addressBatcher;
        _traceBatcher = traceBatcher;
    }

    public IPersister<SymbolEntry> SymbolBatcher => _symbolBatcher;
    public IPersister<AddressEntry> AddressBatcher => _addressBatcher;
    public IPersister<TraceSampleEntry> TraceBatcher => _traceBatcher;

    public async ValueTask DisposeAsync()
    {
        await _traceBatcher.DisposeAsync();
        await _addressBatcher.DisposeAsync();
        await _symbolBatcher.DisposeAsync();
        
        await _connection.CloseAsync();
    }
}