using Microsoft.Data.Sqlite;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.Sql;

/// <summary>
/// Manages the lifetime of SQLite persistence components
/// </summary>
public class SqlPersistanceLifetime : IPersistanceLifetime
{
    private readonly SqliteConnection _connection;
    private readonly Batcher<SymbolEntry> _symbolBatcher;
    private readonly Batcher<AddressEntry> _addressBatcher;
    private readonly Batcher<TraceSampleEntry> _traceBatcher;
    private bool _disposed = false;

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

    public void Dispose()
    {
        if (_disposed) return;
        
        _traceBatcher.Dispose();
        _addressBatcher.Dispose();
        _symbolBatcher.Dispose();
        
        _connection.Close();
        
        _disposed = true;
    }
}