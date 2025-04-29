using System.Data.Common;
using Dapper;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.Sql;

public class SqlAddressPersistance : IBatchPersistance<AddressEntry>
{
    readonly DbConnection _connection;
    SqlAddressPersistance(DbConnection connection) => _connection = connection;

    public void Persist(IReadOnlyCollection<AddressEntry> batch)
    {
        using var transaction = _connection.BeginTransaction();

        _connection.Execute(@"
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

    public static IBatchPersistance<AddressEntry> Create(DbConnection connection)
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
        return new SqlAddressPersistance(connection);
    }
}
