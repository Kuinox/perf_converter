using System.Data.Common;
using Dapper;
using PerfConverter.Persistance;

namespace PerfConverter.Persistance.Sql;

public class SqlTracePersistance : ITracePersistance
{
    private readonly DbConnection _connection;
    private SqlTracePersistance(DbConnection connection)
    {
        _connection = connection;
    }
    public void Persist(IReadOnlyCollection<TraceSample> batch)
    {
        using var transaction = _connection.BeginTransaction();
        _connection.Execute(@"
        INSERT INTO TraceSamples (
            Id, Pid, Tid, Time, Cpu, Ip, Addr, Period,
            InsnCnt, CycCnt, Weight, Cpumode, AddrCorrelatesSym,
            Event, MachinePid, Vcpu
        ) VALUES (
            $Id, $Pid, $Tid, $Time, $Cpu, $Ip, $Addr, $Period,
            $InsnCnt, $CycCnt, $Weight, $Cpumode, $AddrCorrelatesSym,
            $Event, $MachinePid, $Vcpu
        );", batch, transaction);
        transaction.Commit();
    }

    public static ITracePersistance Create(DbConnection connection)
    {
        connection.Execute(@"
            CREATE TABLE TraceSamples (
                Id BIGINT PRIMARY KEY,
                Pid INT,
                Tid INT,
                Time BIGINT,
                Cpu INT,
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
        return new SqlTracePersistance(connection);
    }
}
