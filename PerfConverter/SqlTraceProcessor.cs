using System.Runtime.InteropServices;
using Dapper;
using Microsoft.Data.Sqlite;

namespace PerfConverter;

public unsafe class SqlTraceProcessor(SqliteConnection connection) : ITraceProcessor
{
    public unsafe long FilterEventEarly(PerfDlFilterSample* sample)
    {
        connection.Execute(@"
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
            SampleId = sample->id,
            Pid = sample->pid,
            Tid = sample->tid,
            Time = sample->time,
            Cpu = sample->cpu,
            Ip = (long)sample->ip,
            Addr = (long)sample->addr,
            Period = sample->period,
            InsnCnt = sample->insn_cnt,
            CycCnt = sample->cyc_cnt,
            Weight = sample->weight,
            Cpumode = sample->cpumode,
            AddrCorrelatesSym = sample->addr_correlates_sym,
            Event = Marshal.PtrToStringUTF8(sample->@event),
            MachinePid = sample->machine_pid,
            Vcpu = sample->vcpu
        });

        return connection.ExecuteScalar<long>("SELECT last_insert_rowid();");
    }

    public static SqlTraceProcessor Create(SqliteConnection connection)
    {
        connection.Execute(@"
            CREATE TABLE TraceSamples (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
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
                Event TEXT,
                MachinePid INT,
                Vcpu INT
            );
        ");

        return new SqlTraceProcessor(connection);
    }
}
