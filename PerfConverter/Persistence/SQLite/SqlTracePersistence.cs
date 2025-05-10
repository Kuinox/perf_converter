using System.Data.Common;
using Dapper;
using PerfConverter.Entry;

namespace PerfConverter.Persistence.Sql;

public class SqlTracePersistence : IBatchPersistence<TraceSampleEntry>
{
    readonly DbConnection _connection;
    SqlTracePersistence(DbConnection connection) => _connection = connection;

    public async Task PersistAsync(IReadOnlyCollection<TraceSampleEntry> batch)
    {
        using var transaction = _connection.BeginTransaction();
        await _connection.ExecuteAsync(@"
        INSERT INTO TraceSamples (
            Id, Pid, Tid, Time, Cpu, Flags, Ip, Addr, Period,
            InsnCnt, CycCnt, Weight, Cpumode, AddrCorrelatesSym,
            Event, MachinePid, Vcpu
        ) VALUES (
            $Id, $Pid, $Tid, $Time, $Cpu, $Flags, $Ip, $Addr, $Period,
            $InsnCnt, $CycCnt, $Weight, $Cpumode, $AddrCorrelatesSym,
            $Event, $MachinePid, $Vcpu
        );", batch, transaction);
        transaction.Commit();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static IBatchPersistence<TraceSampleEntry> Create(DbConnection connection)
    {
        connection.Execute(@"
            CREATE TABLE TraceSamples (
                Id BIGINT PRIMARY KEY,
                Pid INT,
                Tid INT,
                Time BIGINT,
                Cpu INT,
                Flags INT,
                Ip BIGINT,
                Addr BIGINT,
                Period BIGINT,
                InsnCnt BIGINT,
                CycCnt BIGINT,
                Weight BIGINT,
                Cpumode TINYINT,
                AddrCorrelatesSym TINYINT,
                Event TEXT,
                MachinePid INT,
                Vcpu INT
            );
        ");
        return new SqlTracePersistence(connection);
    }
}
