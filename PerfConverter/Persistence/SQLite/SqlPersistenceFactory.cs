using Dapper;
using Microsoft.Data.Sqlite;
using PerfConverter.Entry;

namespace PerfConverter.Persistence.Sql;

public static class SqlPersistenceFactory
{
    public static IPersistenceLifetime CreatePersistence(
        string connectionString, 
        int batchSize, 
        BatchingMode batchingMode)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        connection.Execute("PRAGMA journal_mode=OFF;");
        connection.Execute("PRAGMA synchronous=OFF;");
        connection.Execute("PRAGMA locking_mode=EXCLUSIVE;");
        
        var sqlLock = new Lock();
        
        var symbolPersister = new SqlLock<SymbolEntry>(sqlLock, SqlSymPersistence.Create(connection));
        var addressPersister = new SqlLock<AddressEntry>(sqlLock, SqlAddressPersistence.Create(connection));
        var tracePersister = new SqlLock<TraceSampleEntry>(sqlLock, SqlTracePersistence.Create(connection));
        
        var symbolBatcher = Batcher<SymbolEntry>.Create(symbolPersister, batchSize, batchingMode);
        var addressBatcher = Batcher<AddressEntry>.Create(addressPersister, batchSize, batchingMode);
        var traceBatcher = Batcher<TraceSampleEntry>.Create(tracePersister, batchSize, batchingMode);
        
        return new SqlPersistenceLifetime(connection, symbolBatcher, addressBatcher, traceBatcher);
    }
}