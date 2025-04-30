using Dapper;
using Microsoft.Data.Sqlite;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.Sql;

public static class SqlPersistanceFactory
{
    public static IPersistanceLifetime CreatePersistance(
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
        
        var symbolPersister = new SqlLock<SymbolEntry>(sqlLock, SqlSymPersistance.Create(connection));
        var addressPersister = new SqlLock<AddressEntry>(sqlLock, SqlAddressPersistance.Create(connection));
        var tracePersister = new SqlLock<TraceSampleEntry>(sqlLock, SqlTracePersistance.Create(connection));
        
        var symbolBatcher = Batcher<SymbolEntry>.Create(symbolPersister, batchSize, batchingMode);
        var addressBatcher = Batcher<AddressEntry>.Create(addressPersister, batchSize, batchingMode);
        var traceBatcher = Batcher<TraceSampleEntry>.Create(tracePersister, batchSize, batchingMode);
        
        return new SqlPersistanceLifetime(connection, symbolBatcher, addressBatcher, traceBatcher);
    }
}