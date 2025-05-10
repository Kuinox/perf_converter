using System.Data.Common;
using Dapper;
using PerfConverter.Entry;

namespace PerfConverter.Persistence.Sql;

public class SqlAddressPersistence : IBatchPersistence<AddressEntry>
{
    readonly DbConnection _connection;
    SqlAddressPersistence(DbConnection connection) => _connection = connection;

    public async Task PersistAsync(IReadOnlyCollection<AddressEntry> batch)
    {
        using var transaction = _connection.BeginTransaction();

        await _connection.ExecuteAsync(@"
            INSERT INTO Addresses (
                Id,
                TraceId,
                Address, Pid, IsIp, Size, Symoff, SymStrId, SymStart, SymEnd,
                Dso, SymBinding, Is64Bit, IsKernelIp,
                BuildId, Filtered, Comm, Priv
            ) VALUES (
                $Id,
                $TraceId,
                $Address, $Pid, $IsIp, $Size, $Symoff, $SymStrId, $SymStart, $SymEnd,
                $Dso, $SymBinding, $Is64Bit, $IsKernelIp,
                $BuildId, $Filtered, $Comm, $Priv
            );
        ", batch, transaction);
        transaction.Commit();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static IBatchPersistence<AddressEntry> Create(DbConnection connection)
    {
        connection.Execute(@"
            CREATE TABLE Addresses (
                Id BIGINT PRIMARY KEY,
                TraceId BIGINT NOT NULL,
                Address BIGINT NOT NULL,
                Pid INT NOT NULL,
                IsIp TINYINT NOT NULL,
                Size INT,
                Symoff INT,
                SymStrId BIGINT,
                SymStart BIGINT,
                SymEnd BIGINT,
                Dso BIGINT,
                SymBinding TINYINT,
                Is64Bit TINYINT,
                IsKernelIp TINYINT,
                BuildId BLOB,
                Filtered TINYINT,
                Comm BIGINT,
                Priv BIGINT
            );
        ");
        return new SqlAddressPersistence(connection);
    }
}
