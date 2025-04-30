using Microsoft.Data.Sqlite;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.Sql;

/// <summary>
/// Manages the lifetime of SQLite persistence components
/// </summary>
public class SqlPersistanceLifetime : IPersistanceLifetime
{
    readonly SqliteConnection _connection;
    readonly Batcher<SymbolEntry> _symbolBatcher;
    readonly Batcher<AddressEntry> _addressBatcher;
    readonly Batcher<TraceSampleEntry> _traceBatcher;

    public SqlPersistanceLifetime(
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

    public IPersiter<SymbolEntry> SymbolBatcher => _symbolBatcher;
    public IPersiter<AddressEntry> AddressBatcher => _addressBatcher;
    public IPersiter<TraceSampleEntry> TraceBatcher => _traceBatcher;

    public async ValueTask DisposeAsync()
    {
        await _traceBatcher.DisposeAsync();
        await _addressBatcher.DisposeAsync();
        await _symbolBatcher.DisposeAsync();
        
        await _connection.CloseAsync();
    }
}