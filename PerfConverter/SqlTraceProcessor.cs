using System.Runtime.InteropServices;
using Dapper;
using Microsoft.Data.Sqlite;

namespace PerfConverter;

public unsafe class SqlTraceProcessor(SqliteConnection connection) : BackgroundBatching<SqlTraceProcessor.TraceSample>(200_000), ITraceProcessor
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TraceSample
    {
        public ulong Id;
        public int Pid;
        public int Tid;
        public ulong Time;
        public int Cpu;
        public long Ip;
        public long Addr;
        public ulong Period;
        public ulong InsnCnt;
        public ulong CycCnt;
        public ulong Weight;
        public byte Cpumode;
        public byte AddrCorrelatesSym;
        public string? Event;
        public int MachinePid;
        public int Vcpu;
    }
    
    private ulong _totalSamples = 0;

    public unsafe long FilterEventEarly(PerfDlFilterSample* sample)
    {
        QueueItem(new TraceSample
        {
            Id = _totalSamples++,
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
        
        return (long)sample->id;
    }

    protected override void BatchSend(IReadOnlyCollection<TraceSample> batch)
    {
        connection.Execute(@"
        INSERT INTO TraceSamples (
            Id, Pid, Tid, Time, Cpu, Ip, Addr, Period,
            InsnCnt, CycCnt, Weight, Cpumode, AddrCorrelatesSym,
            Event, MachinePid, Vcpu
        ) VALUES (
            @Id, @Pid, @Tid, @Time, @Cpu, @Ip, @Addr, @Period,
            @InsnCnt, @CycCnt, @Weight, @Cpumode, @AddrCorrelatesSym,
            @Event, @MachinePid, @Vcpu
        );", batch);
    }

    public static SqlTraceProcessor Create(SqliteConnection connection)
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

        return new SqlTraceProcessor(connection);
    }
}
