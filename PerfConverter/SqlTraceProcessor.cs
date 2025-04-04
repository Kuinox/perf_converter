using Dapper;
using Microsoft.Data.Sqlite;

namespace PerfConverter;

public unsafe class SqlTraceProcessor : ITraceProcessor
{
    private readonly SqliteConnection _connection;

    private SqlTraceProcessor(SqliteConnection connection)
    {
        _connection = connection;
    }

    public unsafe void FilterEventEarly(PerfDlFilterSample* sample)
    {
        InsertSample(sample);
    }

    private unsafe void InsertSample(PerfDlFilterSample* s)
    {
        _connection.Execute(@"
            INSERT INTO TraceSamples (
                SampleId, Pid, Tid, Time, Cpu, Ip, Addr, Period,
                InsnCnt, CycCnt, Weight, Cpumode, AddrCorrelatesSym,
                Event, MachinePid, Vcpu
            ) VALUES (
                @SampleId, @Pid, @Tid, @Time, @Cpu, @Ip, @Addr, @Period,
                @InsnCnt, @CycCnt, @Weight, @Cpumode, @AddrCorrelatesSym,
                @Event, @MachinePid, @Vcpu
            );
        ", new
        {
            SampleId = s->id,
            Pid = s->pid,
            Tid = s->tid,
            Time = s->time,
            Cpu = s->cpu,
            Ip = (long)s->ip,
            Addr = (long)s->addr,
            Period = s->period,
            InsnCnt = s->insn_cnt,
            CycCnt = s->cyc_cnt,
            Weight = s->weight,
            Cpumode = s->cpumode,
            AddrCorrelatesSym = s->addr_correlates_sym,
            Event = (long)s->@event,
            MachinePid = s->machine_pid,
            Vcpu = s->vcpu
        });
    }

    public static SqlTraceProcessor Create(SqliteConnection connection)
    {
        connection.Execute(@"
            CREATE TABLE TraceSamples (
                Id BIGINT IDENTITY(1,1) PRIMARY KEY,
                SampleId BIGINT NOT NULL,
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
                Event BIGINT,
                MachinePid INT,
                Vcpu INT
            );
        ");

        return new SqlTraceProcessor(connection);
    }
}
