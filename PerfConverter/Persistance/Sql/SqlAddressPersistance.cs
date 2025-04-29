using System.Data.Common;
using Dapper;
using PerfConverter.Persistance;

namespace PerfConverter.Persistance.Sql;

public class SqlAddressPersistance : IAddressPersistance
{
    private readonly DbConnection _connection;
    private SqlAddressPersistance(DbConnection connection)
    {
        _connection = connection;
    }

    public static SqlAddressPersistance Create(DbConnection connection)
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
                BuildIdSize INT,
                BuildId BIGINT,
                Filtered TINYINT,
                Comm BIGINT,
                Priv BIGINT
            );
        ");
        return new SqlAddressPersistance(connection);
    }

    public void Persist(IReadOnlyCollection<AddressEntry> batch)
    {
        using var transaction = _connection.BeginTransaction();

        _connection.Execute(@"
            INSERT INTO Addresses (
                Id,
                TraceId,
                Address, Pid, IsIp, Size, Symoff, SymStrId, SymStart, SymEnd,
                Dso, SymBinding, Is64Bit, IsKernelIp,
                BuildIdSize, BuildId, Filtered, Comm, Priv
            ) VALUES (
                $Id,
                $TraceId,
                $Address, $Pid, $IsIp, $Size, $Symoff, $SymStrId, $SymStart, $SymEnd,
                $Dso, $SymBinding, $Is64Bit, $IsKernelIp,
                $BuildIdSize, $BuildId, $Filtered, $Comm, $Priv
            );
        ", batch, transaction);
        transaction.Commit();
    }
}
